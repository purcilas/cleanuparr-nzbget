using Cleanuparr.Api.Features.DownloadClient.Contracts.Requests;
using Cleanuparr.Api.Features.DownloadClient.Contracts.Responses;
using Cleanuparr.Api.Tests.TestHelpers;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Domain.Exceptions;
using Cleanuparr.Persistence.Models.Configuration;
using Shouldly;
using Xunit;

namespace Cleanuparr.Api.Tests.Features.DownloadClient;

public sealed class UsenetConfigurationTests
{
    private static CreateDownloadClientRequest Request => new() { Name = "nzb", Enabled = true, Type = DownloadClientType.Usenet,
        TypeName = DownloadClientTypeName.NZBGet, Host = "https://nzb.test", Username = "rpc", Password = "test-password" };

    [Fact]
    public void NZBGet_preserves_enum_numbers_and_defaults_to_observation()
    {
        ((int)DownloadClientTypeName.rTorrent).ShouldBe(4);
        ((int)DownloadClientTypeName.NZBGet).ShouldBe(5);
        var config = Request.ToEntity(); config.Validate();
        config.UsenetOptions.LiveCleanupEnabled.ShouldBeFalse();
        config.UsenetOptions.LiveValidationConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void Invalid_protocol_and_unvalidated_live_configuration_are_rejected()
    {
        Should.Throw<ValidationException>(() => (Request with { Type = DownloadClientType.Torrent }).ToEntity().Validate());
        Should.Throw<ValidationException>(() => (Request with { UsenetOptions = new() { LiveCleanupEnabled = true } }).ToEntity().Validate());
        Should.Throw<ValidationException>(() => (Request with { Host = "https://user:test-password@nzb.test" }).ToEntity().Validate());
    }

    [Fact]
    public void Usenet_configuration_roundtrips_without_exposing_the_internal_json_column()
    {
        var config = Request.ToEntity();
        config.UsenetOptions = new() { StallMinutes = 90 };
        var response = DownloadClientConfigResponse.From(config);
        response.UsenetOptions.StallMinutes.ShouldBe(90);
        typeof(DownloadClientConfigResponse).GetProperty("UsenetOptionsJson").ShouldBeNull();
        // SensitiveData resolver remains on the response's password property for both protocols.
        typeof(DownloadClientConfigResponse).GetProperty("Password")!.GetCustomAttributes(false)
            .Any(x => x.GetType().Name == "SensitiveDataAttribute").ShouldBeTrue();
    }
}
