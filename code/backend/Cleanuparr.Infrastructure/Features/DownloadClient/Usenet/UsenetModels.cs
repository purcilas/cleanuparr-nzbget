namespace Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;

public enum UsenetPhase { Unknown, Waiting, Paused, Downloading, Processing, Completed, Failed }

public sealed record UsenetJob(
    long Id, string Name, string Category, IReadOnlyList<string> ArrIds,
    UsenetPhase Phase, string Stage, ulong? RemainingBytes, ulong? DownloadedBytes,
    int? StageProgress, long? SuccessfulArticles, long? FailedArticles, bool HasPausedFiles,
    int? Health, int? CriticalHealth)
{
    public bool IdentifiersKnown { get; init; } = true;
    public string Fingerprint => $"{Name}|{Category}|{string.Join(',', ArrIds.Order())}";
    // Drone takes precedence. Numeric NZBID is only a fallback when the job has no drone identifier.
    public bool Matches(string id) => IdentifiersKnown && (ArrIds.Count > 0
        ? ArrIds.Contains(id, StringComparer.OrdinalIgnoreCase)
        : string.Equals(Id.ToString(System.Globalization.CultureInfo.InvariantCulture), id, StringComparison.Ordinal));
    public string Progress => Phase == UsenetPhase.Downloading
        ? $"{RemainingBytes}:{DownloadedBytes}:{SuccessfulArticles}:{FailedArticles}"
        : $"{Stage}:{StageProgress}";
}

public sealed record UsenetSnapshot(
    IReadOnlyList<UsenetJob> Jobs, ulong? TotalDownloadedBytes, long? UptimeSeconds,
    bool Safe, string SafetyReason);

public interface IUsenetDownloadService
{
    Task<UsenetSnapshot> InspectAsync(CancellationToken cancellationToken = default);
}

[Flags]
public enum DownloadCapabilities { None = 0, TorrentQueue = 1, Seeding = 2, FileBlocking = 4, UsenetQueue = 8 }
