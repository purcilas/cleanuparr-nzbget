using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Events.Interfaces;
using Cleanuparr.Infrastructure.Features.Arr.Interfaces;
using Cleanuparr.Infrastructure.Features.Arr.ForceImport;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.DownloadClient;
using Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;
using Cleanuparr.Infrastructure.Features.DownloadRemover.Interfaces;
using Cleanuparr.Infrastructure.Features.DownloadRemover.Models;
using Cleanuparr.Infrastructure.Interceptors;
using Cleanuparr.Infrastructure.Tests.Features.Jobs.TestHelpers;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.Configuration.Arr;
using Cleanuparr.Persistence.Models.Configuration.General;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient.Usenet;

public sealed class UsenetQueueCoordinatorTests : IDisposable
{
    private readonly DataContext _data = TestDataContextFactory.Create();
    private readonly EventsContext _events = TestEventsContextFactory.Create();
    private readonly IArrClient _arr = Substitute.For<IArrClient>();
    private readonly IDownloadServiceFactory _downloads = Substitute.For<IDownloadServiceFactory>();
    private readonly IArrClientFactory _arrs = Substitute.For<IArrClientFactory>();
    private readonly IQueueItemRemover _remover = Substitute.For<IQueueItemRemover>();
    private readonly IDryRunInterceptor _dry = Substitute.For<IDryRunInterceptor>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();
    private readonly IForceImportService _forceImport = Substitute.For<IForceImportService>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-03T12:00:00Z"));
    private readonly DownloadClientConfig _config = NzbGetServiceTests.Config with { Enabled = true, UsenetOptions = new() { StallMinutes = 5, RequiredObservations = 2,
        LiveCleanupEnabled = true, LiveValidationConfirmed = true } };
    private UsenetSnapshot _snapshot = UsenetEvaluatorTests.Snapshot();
    private QueueRecord _record = new() { Id = 1, DownloadId = "drone-id", DownloadClient = "nzb", Protocol = "usenet", Title = "release", EpisodeId = 2, SeriesId = 3, SeasonNumber = 1 };
    private readonly ArrInstance _instance;

    public UsenetQueueCoordinatorTests()
    {
        _data.DownloadClients.Add(_config);
        _instance = new() { Name = "sonarr", Enabled = true, Url = new Uri("https://sonarr.test"), ApiKey = "test", Version = 4,
            ArrConfigId = _data.ArrConfigs.Single(x => x.Type == InstanceType.Sonarr).Id };
        _data.ArrInstances.Add(_instance); _data.SaveChanges();
        _arrs.GetClient(Arg.Any<InstanceType>(), Arg.Any<float>()).Returns(_arr);
        _arr.GetQueueItemsAsync(Arg.Any<ArrInstance>(), Arg.Any<int>()).Returns(_ => new QueueListResponse { TotalRecords = 1, Records = [_record] });
        _arr.IsRecordValid(Arg.Any<QueueRecord>()).Returns(true); _arr.HasContentId(Arg.Any<QueueRecord>()).Returns(true);
        var service = Substitute.For<IDownloadService, IUsenetDownloadService>();
        ((IUsenetDownloadService)service).InspectAsync(Arg.Any<CancellationToken>()).Returns(_ => _snapshot);
        _downloads.GetDownloadService(Arg.Any<DownloadClientConfig>()).Returns(service);
        _forceImport.TryImportAsync(Arg.Any<IArrClient>(), Arg.Any<ArrInstance>(), Arg.Any<QueueRecord>()).Returns(ForceImportOutcome.NotApplicable);
    }

    private UsenetQueueCoordinator Coordinator() => new(_data, _events, _downloads, _arrs, _remover, _dry, _publisher,
        NullLogger<UsenetQueueCoordinator>.Instance, _time, _forceImport);
    private async Task Poll()
    {
        ContextProvider.Set(new GeneralConfig { IgnoredDownloads = [] });
        ContextProvider.Set(new QueueCleanerConfig { IgnoredDownloads = [] });
        ContextProvider.SetJobRunId(Guid.NewGuid());
        await Coordinator().ProcessAsync(default);
    }
    private async Task Stall()
    {
        await Poll(); _time.Advance(TimeSpan.FromMinutes(3)); await Poll(); _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
    }

    [Fact]
    public async Task Dry_run_never_removes_or_force_imports_and_observations_survive_new_coordinators()
    {
        _dry.IsDryRunEnabled().Returns(true);
        await Stall();
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
        await _forceImport.DidNotReceive().TryImportAsync(Arg.Any<IArrClient>(), Arg.Any<ArrInstance>(), Arg.Any<QueueRecord>());
        var row = await _events.UsenetObservations.SingleAsync(x => x.Id == $"{_config.Id}:7");
        row.Incident.ShouldContain("observation/dry-run"); row.Samples.ShouldBe(3); row.ActionState.ShouldBeEmpty();
    }

    [Fact]
    public async Task Observation_mode_stays_non_mutating_even_when_global_dry_run_is_off()
    {
        _config.UsenetOptions = _config.UsenetOptions with { LiveCleanupEnabled = false };
        await _data.SaveChangesAsync(); await Stall();
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
    }

    [Fact]
    public async Task Verified_stall_uses_arr_once_and_waits_for_recovery_across_restart()
    {
        await Stall();
        await _remover.Received(1).RemoveQueueItemAsync(Arg.Is<QueueItemRemoveRequest>(x => x.DownloadClient!.Id == _config.Id
            && x.DeleteReason == DeleteReason.Stalled && ((ArrRemovalTarget)x.Target).RemoveFromClient));
        _events.ChangeTracker.Clear(); // Re-load persisted action state rather than keeping tracked objects.
        _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
        await _remover.Received(1).RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
        var client = await _events.UsenetObservations.FindAsync($"{_config.Id}:client");
        client!.ActionState.ShouldBe("AwaitingProgress");
        _snapshot = _snapshot with { TotalDownloadedBytes = 101 }; _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
        client.ActionState.ShouldBeEmpty();
    }

    [Fact]
    public async Task Uncertain_removal_halts_client_and_is_never_retried()
    {
        _remover.RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>()).ThrowsAsync(new HttpRequestException("timeout"));
        await Stall(); _events.ChangeTracker.Clear(); _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
        await _remover.Received(1).RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
        (await _events.UsenetObservations.FindAsync($"{_config.Id}:client"))!.ActionState.ShouldBe("Halted");
        (await _events.UsenetObservations.FindAsync($"{_config.Id}:7"))!.ActionState.ShouldBe("Uncertain");
    }

    [Fact]
    public async Task Removing_a_bad_item_without_restoring_progress_halts_further_actions()
    {
        await Stall(); _time.Advance(TimeSpan.FromMinutes(31)); await Poll();
        (await _events.UsenetObservations.FindAsync($"{_config.Id}:client"))!.ActionState.ShouldBe("Halted");
        await _remover.Received(1).RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
    }

    [Fact]
    public async Task Queue_progress_after_a_gap_does_not_prove_recovery()
    {
        await Stall(); _snapshot = _snapshot with { TotalDownloadedBytes = 500 };
        _time.Advance(TimeSpan.FromMinutes(11)); await Poll();
        (await _events.UsenetObservations.FindAsync($"{_config.Id}:client"))!.ActionState.ShouldBe("AwaitingProgress");
    }

    [Fact]
    public async Task Same_identifier_on_multiple_owners_is_protected()
    {
        _data.ArrInstances.Add(new() { Name = "radarr", Enabled = true, Url = new Uri("https://radarr.test"), ApiKey = "test", Version = 3,
            ArrConfigId = _data.ArrConfigs.Single(x => x.Type == InstanceType.Radarr).Id });
        await _data.SaveChangesAsync(); await Stall();
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
        (await _events.UsenetObservations.FindAsync($"{_config.Id}:7"))!.Incident.ShouldContain("ownership");
    }

    [Fact]
    public async Task Season_pack_is_one_removal_with_one_season_search_target()
    {
        _arr.GetQueueItemsAsync(Arg.Any<ArrInstance>(), Arg.Any<int>()).Returns(new QueueListResponse { TotalRecords = 2, Records = [_record, _record with { Id = 2, EpisodeId = 4 }] });
        await Stall();
        await _remover.Received(1).RemoveQueueItemAsync(Arg.Is<QueueItemRemoveRequest>(x => ((ArrRemovalTarget)x.Target).SearchItem is Cleanuparr.Domain.Entities.Arr.SeriesSearchItem
            && ((Cleanuparr.Domain.Entities.Arr.SeriesSearchItem)((ArrRemovalTarget)x.Target).SearchItem).SearchType == SeriesSearchType.Season));
    }

    [Fact]
    public async Task Active_stall_without_bad_release_evidence_does_not_delete()
    {
        _snapshot = _snapshot with { Jobs = [UsenetEvaluatorTests.Job with { Health = 1000, CriticalHealth = 500, FailedArticles = 0 }] };
        await Stall();
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
    }

    [Fact]
    public async Task Fresh_progress_prevents_action_after_threshold()
    {
        await Poll(); _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
        var service = _downloads.GetDownloadService(_config);
        int reads = 0;
        ((IUsenetDownloadService)service).InspectAsync(Arg.Any<CancellationToken>()).Returns(_ => ++reads == 1 ? _snapshot
            : _snapshot with { Jobs = [UsenetEvaluatorTests.Job with { DownloadedBytes = 101 }] });
        _time.Advance(TimeSpan.FromMinutes(3)); await Poll();
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
    }

    [Fact]
    public async Task Failed_import_of_active_or_unknown_job_cannot_bypass_observation_mode()
    {
        _arr.ShouldRemoveFromQueue(Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>()).Returns(true);
        _snapshot = _snapshot with { Jobs = [UsenetEvaluatorTests.Job with { Phase = UsenetPhase.Unknown }] };
        await Stall();
        await _arr.DidNotReceive().ShouldRemoveFromQueue(Arg.Any<InstanceType>(), Arg.Any<QueueRecord>(), Arg.Any<bool>(), Arg.Any<short>());
        await _remover.DidNotReceive().RemoveQueueItemAsync(Arg.Any<QueueItemRemoveRequest>());
    }

    public void Dispose() { _events.Dispose(); _data.Dispose(); }
}
