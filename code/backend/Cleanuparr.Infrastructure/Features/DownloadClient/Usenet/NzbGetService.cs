using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cleanuparr.Domain.Entities;
using Cleanuparr.Domain.Entities.HealthCheck;
using Cleanuparr.Infrastructure.Http;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.Configuration.DownloadCleaner;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;

/// <summary>Read-only adapter. Recovery mutations are made exclusively through the owning arr.</summary>
public sealed class NzbGetService : IDownloadService, IUsenetDownloadService
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    public DownloadClientConfig ClientConfig { get; }
    public DownloadCapabilities Capabilities => DownloadCapabilities.UsenetQueue;

    public NzbGetService(DownloadClientConfig config, IDynamicHttpClientProvider provider)
        : this(config, provider.CreateClient(config)) { }

    public NzbGetService(DownloadClientConfig config, HttpClient http)
    {
        config.Validate();
        ClientConfig = config;
        _http = http;
        _endpoint = new Uri(config.Url.ToString().TrimEnd('/') + "/jsonrpc");
    }

    public async Task<JsonElement> CallAsync(string method, object[] parameters, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{ClientConfig.Username}:{ClientConfig.Password}")));
        request.Content = JsonContent.Create(new { method, @params = parameters, id = 1 });
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"NZBGet RPC HTTP {(int)response.StatusCode}", null, response.StatusCode);
        // Bound both duration and response size; never copy error bodies into logs or API responses.
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > 32 * 1024 * 1024) throw new InvalidOperationException("NZBGet response exceeds the safety limit");
            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("NZBGet RPC returned an error");
        if (!root.TryGetProperty("result", out var result)) throw new InvalidOperationException("NZBGet RPC result is missing");
        return result.Clone();
    }

    public async Task LoginAsync() => _ = await CallAsync("version", []);

    public async Task<HealthCheckResult> HealthCheckAsync()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await CallAsync("version", []);
            await InspectAsync(); // version alone also works with add-only credentials, which are insufficient.
            return new() { IsHealthy = true, ResponseTime = timer.Elapsed };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new() { IsHealthy = false, ErrorMessage = "NZBGet connection or queue read failed; verify RPC access and URL base", ResponseTime = timer.Elapsed };
        }
    }

    public async Task<UsenetSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        var status = await CallAsync("status", [], cancellationToken);
        var queue = await CallAsync("listgroups", [0], cancellationToken);
        var history = await CallAsync("history", [false], cancellationToken);
        var logs = await CallAsync("log", [0, 100], cancellationToken);
        if (queue.ValueKind != JsonValueKind.Array || history.ValueKind != JsonValueKind.Array || logs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("NZBGet queue, history or log response is incomplete");
        var jobs = queue.EnumerateArray().Select(x => Normalize(x, false)).Concat(history.EnumerateArray().Select(x => Normalize(x, true))).ToList();
        if (jobs.Any(x => x.Id <= 0) || jobs.Select(x => x.Id).Distinct().Count() != jobs.Count)
            throw new InvalidOperationException("NZBGet job identity is incomplete or changed during inspection");
        bool required = Bool(status, "DownloadPaused") == false && Bool(status, "PostPaused") == false
            && Bool(status, "QuotaReached") == false && Number(status, "ResumeTime") == 0
            && Counter(status, "FreeDiskSpace") >= (ulong)ClientConfig.UsenetOptions.MinimumFreeDiskMiB * 1024 * 1024;
        // Newer versions expose the intermediate disk separately. Missing intermediate disk is unknown, not free space.
        bool interSafe = Counter(status, "FreeInterDiskSpace") >= (ulong)ClientConfig.UsenetOptions.MinimumFreeDiskMiB * 1024 * 1024;
        bool activeServer = status.TryGetProperty("NewsServers", out var servers) && servers.ValueKind == JsonValueKind.Array
            && servers.EnumerateArray().Any(x => Bool(x, "Active") == true);
        long? serverTime = Number(status, "ServerTime");
        bool logSafe = serverTime.HasValue && logs.EnumerateArray().All(x => Number(x, "Time") >= 0
            && Text(x, "Kind") is "INFO" or "DETAIL" or "DEBUG" or "WARNING" or "ERROR")
            && !logs.EnumerateArray().Any(x =>
            Number(x, "Time") is long t && t >= serverTime.Value - 300 && Text(x, "Kind") is "ERROR" or "WARNING");
        bool safe = required && interSafe && activeServer && logSafe && Counter(status, "DownloadedSize").HasValue && Number(status, "UpTimeSec") >= 0;
        return new(jobs, Counter(status, "DownloadedSize"), Number(status, "UpTimeSec"), safe,
            safe ? "Ready" : "Paused, quota/disk/provider warning, or incomplete safety data");
    }

    public static UsenetJob Normalize(JsonElement row, bool history)
    {
        string stage = Text(row, "Status");
        var phase = history ? (stage.StartsWith("SUCCESS/", StringComparison.Ordinal) ? UsenetPhase.Completed
            : stage.StartsWith("FAILURE/", StringComparison.Ordinal) ? UsenetPhase.Failed : UsenetPhase.Unknown)
            : stage switch
            {
                "DOWNLOADING" => UsenetPhase.Downloading,
                "QUEUED" or "PP_QUEUED" => UsenetPhase.Waiting,
                "PAUSED" => UsenetPhase.Paused,
                "LOADING_PARS" or "VERIFYING_SOURCES" or "REPAIRING" or "VERIFYING_REPAIRED" or "RENAMING"
                    or "UNPACKING" or "MOVING" or "POST_UNPACK_RENAMING" or "POST_DOWNLOAD_RENAMING" or "EXECUTING_SCRIPT" => UsenetPhase.Processing,
                _ => UsenetPhase.Unknown,
            };
        List<string> ids = [];
        bool identifiersKnown = row.TryGetProperty("Parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array;
        if (identifiersKnown)
            ids = parameters.EnumerateArray().Where(x => string.Equals(Text(x, "Name"), "drone", StringComparison.OrdinalIgnoreCase))
                .Select(x => Text(x, "Value")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ulong? paused = Counter(row, "PausedSize");
        return new(Number(row, "NZBID") ?? 0, Text(row, "NZBName") is { Length: > 0 } name ? name : Text(row, "Name"), Text(row, "Category"), ids,
            phase, stage, Counter(row, "RemainingSize"), Counter(row, "DownloadedSize"), Permille(row, "PostStageProgress"),
            Number(row, "SuccessArticles"), Number(row, "FailedArticles"), !paused.HasValue || paused.Value > 0,
            Permille(row, "Health"), Permille(row, "CriticalHealth")) { IdentifiersKnown = identifiersKnown };
    }

    public static ulong? Counter(JsonElement row, string prefix)
    {
        long? hi = Number(row, prefix + "Hi"), lo = Number(row, prefix + "Lo");
        return hi is >= 0 and <= uint.MaxValue && lo is >= 0 and <= uint.MaxValue
            ? ((ulong)hi.Value << 32) | (ulong)lo.Value : null;
    }
    private static int? Permille(JsonElement row, string key) => Number(row, key) is long value && value is >= 0 and <= 1000 ? (int)value : null;
    private static long? Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result) ? result : null;
    private static bool? Bool(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    public void Dispose() => _http.Dispose();
    private static NotSupportedException Unsupported() => new("NZBGet does not support torrent operations");
    public Task<DownloadCheckResult> ShouldRemoveFromArrQueueAsync(string hash, IReadOnlyList<string> ignoredDownloads) => throw Unsupported();
    public Task<List<ITorrentItemWrapper>> GetSeedingDownloads() => throw Unsupported();
    public Task<List<ITorrentItemWrapper>> GetAllTorrentsLite() => throw Unsupported();
    public Task<IReadOnlyList<string>> GetClaimedPathsAsync(IReadOnlyList<ITorrentItemWrapper> torrents) => throw Unsupported();
    public List<ITorrentItemWrapper>? FilterDownloadsToBeCleanedAsync(List<ITorrentItemWrapper>? downloads, List<ISeedingRule> rules) => throw Unsupported();
    public List<ITorrentItemWrapper>? FilterDownloadsToChangeCategoryAsync(List<ITorrentItemWrapper>? downloads, UnlinkedConfig config) => throw Unsupported();
    public Task CleanDownloadsAsync(List<ITorrentItemWrapper>? downloads, List<ISeedingRule> rules) => throw Unsupported();
    public Task ChangeCategoryForNoHardLinksAsync(List<ITorrentItemWrapper>? downloads, UnlinkedConfig config) => throw Unsupported();
    public Task ChangeTorrentCategoryAsync(ITorrentItemWrapper torrent, string targetCategory, bool useTag) => throw Unsupported();
    public Task DeleteDownload(ITorrentItemWrapper torrent, bool deleteSourceFiles) => throw Unsupported();
    public Task StopDownload(ITorrentItemWrapper torrent) => throw Unsupported();
    public Task CreateCategoryAsync(string name) => throw Unsupported();
    public Task<BlockFilesResult> BlockUnwantedFilesAsync(string hash, IReadOnlyList<string> ignoredDownloads) => throw Unsupported();
}
