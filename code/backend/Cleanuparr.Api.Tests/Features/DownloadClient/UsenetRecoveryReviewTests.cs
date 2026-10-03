using Cleanuparr.Api.Features.DownloadClient.Controllers;
using Cleanuparr.Api.Tests.TestHelpers;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Features.DownloadClient;
using Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;
using Cleanuparr.Infrastructure.Http.DynamicHttpClientSystem;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.State;
using Cleanuparr.Persistence.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Cleanuparr.Api.Tests.Features.DownloadClient;

public sealed class UsenetRecoveryReviewTests : IDisposable
{
    private readonly DataContext _data = ConfigControllerTestDataFactory.CreateDataContext();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly EventsContext _events;
    private readonly DownloadClientController _controller;
    private readonly DownloadClientConfig _config = new() { Name = "nzb", Enabled = true, TypeName = DownloadClientTypeName.NZBGet,
        Type = DownloadClientType.Usenet, Host = new Uri("https://nzb.test"), Username = "rpc", Password = "public-fixture" };
    private readonly IDownloadService _service = Substitute.For<IDownloadService, IUsenetDownloadService>();

    public UsenetRecoveryReviewTests()
    {
        _connection.Open();
        _events = new(new DbContextOptionsBuilder<EventsContext>().UseSqlite(_connection).Options, new SqliteDatabaseProvider());
        _events.Database.EnsureCreated();
        _data.DownloadClients.Add(_config); _data.SaveChanges();
        _events.UsenetObservations.AddRange(new UsenetObservation { Id = _config.Id + ":client", ClientId = _config.Id, ActionState = "Halted" },
            new UsenetObservation { Id = _config.Id + ":7", ClientId = _config.Id, ActionState = "Uncertain" });
        _events.SaveChanges();
        var factory = Substitute.For<IDownloadServiceFactory>(); factory.GetDownloadService(Arg.Any<DownloadClientConfig>()).Returns(_service);
        ((IUsenetDownloadService)_service).InspectAsync(Arg.Any<CancellationToken>()).Returns(new UsenetSnapshot([], 100, 10, true, "Ready"));
        _controller = new(NullLogger<DownloadClientController>.Instance, _data, Substitute.For<IDynamicHttpClientFactory>(), factory, _events);
    }

    [Fact]
    public async Task Review_requires_explicit_acknowledgement_and_never_retries_previous_jobs()
    {
        (await _controller.ReviewUsenetRecovery(_config.Id, new(false), default)).ShouldBeOfType<BadRequestObjectResult>();
        await ((IUsenetDownloadService)_service).DidNotReceive().InspectAsync(Arg.Any<CancellationToken>());
        (await _controller.ReviewUsenetRecovery(_config.Id, new(true), default)).ShouldBeOfType<OkObjectResult>();
        (await _events.UsenetObservations.FindAsync(_config.Id + ":client"))!.ActionState.ShouldBe("AwaitingProgress");
        (await _events.UsenetObservations.FindAsync(_config.Id + ":7"))!.ActionState.ShouldBe("Uncertain");
    }

    [Fact]
    public async Task Unsafe_inspection_keeps_the_recovery_hold()
    {
        ((IUsenetDownloadService)_service).InspectAsync(Arg.Any<CancellationToken>()).Returns(new UsenetSnapshot([], 100, 10, false, "Paused"));
        (await _controller.ReviewUsenetRecovery(_config.Id, new(true), default)).ShouldBeOfType<BadRequestObjectResult>();
        (await _events.UsenetObservations.FindAsync(_config.Id + ":client"))!.ActionState.ShouldBe("Halted");
    }

    public void Dispose() { _data.Dispose(); _events.Dispose(); _connection.Dispose(); }
}
