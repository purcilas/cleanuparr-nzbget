using System.Net;
using System.Text;
using System.Text.Json;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;
using Cleanuparr.Persistence.Models.Configuration;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient.Usenet;

public sealed class NzbGetServiceTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
    public static DownloadClientConfig Config => new() { Name = "nzb", Type = DownloadClientType.Usenet, TypeName = DownloadClientTypeName.NZBGet,
        Host = new Uri("https://example.test"), Username = "test-user", Password = "test-password", UrlBase = "/proxy/nzbget/" };

    [Fact]
    public async Task Rpc_uses_basic_auth_positional_parameters_and_reverse_proxy_base()
    {
        using var client = new HttpClient(new Handler(async (request, ct) =>
        {
            request.RequestUri!.AbsoluteUri.ShouldBe("https://example.test/proxy/nzbget/jsonrpc");
            request.Headers.Authorization!.Scheme.ShouldBe("Basic");
            Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)).ShouldBe("test-user:test-password");
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            body.RootElement.GetProperty("method").GetString().ShouldBe("listgroups");
            body.RootElement.GetProperty("params")[0].GetInt32().ShouldBe(0);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":[]}") };
        }));
        using var service = new NzbGetService(Config, client);
        (await service.CallAsync("listgroups", [0])).ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"test-password\"}}")]
    [InlineData("{}")]
    public async Task Rpc_errors_do_not_expose_bodies(string response)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) })));
        using var service = new NzbGetService(Config, client);
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => service.CallAsync("version", []));
        ex.Message.ShouldNotContain("test-password");
    }

    [Fact]
    public async Task Cancellation_stops_requests()
    {
        using var client = new HttpClient(new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); }));
        using var service = new NzbGetService(Config, client);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => service.CallAsync("version", [], cancellation.Token));
    }

    [Fact]
    public async Task Authentication_failure_is_sanitized_and_torrent_operations_are_unavailable()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        using var service = new NzbGetService(Config, client);
        (await service.HealthCheckAsync()).IsHealthy.ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => service.GetSeedingDownloads());
        service.Capabilities.ShouldBe(DownloadCapabilities.UsenetQueue);
    }

    [Theory]
    [InlineData("DownloadPaused", "true")]
    [InlineData("PostPaused", "true")]
    [InlineData("QuotaReached", "true")]
    [InlineData("ResumeTime", "300")]
    [InlineData("FreeDiskSpaceLo", "0")]
    [InlineData("FreeInterDiskSpaceLo", "0")]
    public async Task Safety_flags_suspend_the_entire_snapshot(string property, string value)
    {
        var status = new Dictionary<string, object> { ["DownloadPaused"] = false, ["PostPaused"] = false, ["QuotaReached"] = false,
            ["ResumeTime"] = 0, ["FreeDiskSpaceHi"] = 0, ["FreeDiskSpaceLo"] = uint.MaxValue,
            ["FreeInterDiskSpaceHi"] = 0, ["FreeInterDiskSpaceLo"] = uint.MaxValue,
            ["DownloadedSizeHi"] = 0, ["DownloadedSizeLo"] = 1, ["UpTimeSec"] = 100,
            ["ServerTime"] = 1000, ["NewsServers"] = new[] { new { Active = true } } };
        status[property] = JsonSerializer.Deserialize<JsonElement>(value);
        using var client = new HttpClient(new Handler(async (request, ct) =>
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            string result = doc.RootElement.GetProperty("method").GetString() == "status" ? JsonSerializer.Serialize(status) : "[]";
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":" + result + "}") };
        }));
        using var service = new NzbGetService(Config, client);
        (await service.InspectAsync()).Safe.ShouldBeFalse();
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Complete_safety_data_is_eligible_and_recent_provider_errors_suspend_it(bool providerError, bool expected)
    {
        const string status = """{"DownloadPaused":false,"PostPaused":false,"QuotaReached":false,"ResumeTime":0,"FreeDiskSpaceHi":1,"FreeDiskSpaceLo":0,"FreeInterDiskSpaceHi":1,"FreeInterDiskSpaceLo":0,"DownloadedSizeHi":0,"DownloadedSizeLo":1,"UpTimeSec":100,"ServerTime":1000,"NewsServers":[{"Active":true}]}""";
        using var client = new HttpClient(new Handler(async (request, ct) =>
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            string result = doc.RootElement.GetProperty("method").GetString() switch
            {
                "status" => status,
                "log" when providerError => """[{"Time":999,"Kind":"ERROR","Text":"provider unavailable"}]""",
                _ => "[]",
            };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":" + result + "}") };
        }));
        using var service = new NzbGetService(Config, client);
        (await service.InspectAsync()).Safe.ShouldBe(expected);
    }

}
