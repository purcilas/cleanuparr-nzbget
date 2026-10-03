using Cleanuparr.Domain.Entities.Arr;
using Cleanuparr.Domain.Entities.Arr.Queue;
using Cleanuparr.Domain.Enums;
using Cleanuparr.Infrastructure.Events.Interfaces;
using Cleanuparr.Infrastructure.Features.Arr.Interfaces;
using Cleanuparr.Infrastructure.Features.Arr.ForceImport;
using Cleanuparr.Infrastructure.Features.Context;
using Cleanuparr.Infrastructure.Features.DownloadRemover.Interfaces;
using Cleanuparr.Infrastructure.Features.DownloadRemover.Models;
using Cleanuparr.Infrastructure.Interceptors;
using Cleanuparr.Persistence;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.Configuration.Arr;
using Cleanuparr.Persistence.Models.Configuration.General;
using Cleanuparr.Persistence.Models.Configuration.QueueCleaner;
using Cleanuparr.Persistence.Models.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;

/// <summary>Serialized, persisted recovery workflow. No direct NZBGet mutation is permitted.</summary>
public sealed class UsenetQueueCoordinator(
    DataContext data, EventsContext events, IDownloadServiceFactory downloads, IArrClientFactory arrs,
    IQueueItemRemover remover, IDryRunInterceptor dryRun, IEventPublisher publisher,
    ILogger<UsenetQueueCoordinator> logger, TimeProvider time, IForceImportService forceImport)
{
    public static SemaphoreSlim RecoveryLock { get; } = new(1, 1);
    private sealed record Owner(ArrInstance Instance, IArrClient Client, QueueRecord Record);

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await RecoveryLock.WaitAsync(cancellationToken);
        try { await ProcessSerializedAsync(cancellationToken); }
        finally { RecoveryLock.Release(); }
    }

    private async Task ProcessSerializedAsync(CancellationToken ct)
    {
        var configs = await data.DownloadClients.AsNoTracking().Where(x => x.Enabled && x.TypeName == DownloadClientTypeName.NZBGet).ToListAsync(ct);
        if (configs.Count == 0) return;
        var instances = await data.ArrInstances.AsNoTracking().Include(x => x.ArrConfig).Where(x => x.Enabled).ToListAsync(ct);
        List<Owner> owners = [];
        // Every owner must be readable before deciding that an item belongs to only one instance.
        foreach (var instance in instances.Where(x => x.ArrConfig.Type != InstanceType.LazyLibrarian))
        {
            var client = arrs.GetClient(instance.ArrConfig.Type, instance.Version);
            owners.AddRange((await ReadQueueAsync(client, instance, ct)).Where(x => x.Protocol.Equals("usenet", StringComparison.OrdinalIgnoreCase))
                .Select(x => new Owner(instance, client, x)));
        }
        var general = ContextProvider.Get<GeneralConfig>(nameof(GeneralConfig));
        var rules = ContextProvider.Get<QueueCleanerConfig>();
        var ignored = general.IgnoredDownloads.Concat(rules.IgnoredDownloads).ToList();
        foreach (var config in configs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var service = downloads.GetDownloadService(config);
                if (service is not IUsenetDownloadService usenet) continue;
                var snapshot = await usenet.InspectAsync(ct);
                var options = config.UsenetOptions;
                var now = time.GetUtcNow();
                string clientKey = $"{config.Id}:client";
                var clientState = await GetStateAsync(clientKey, config.Id, ct);
                // A restart, counter reset or observation gap cannot prove recovery.
                bool continuous = clientState.LastSeenTicks > 0 && now.UtcTicks > clientState.LastSeenTicks
                    && now.UtcTicks - clientState.LastSeenTicks <= TimeSpan.FromMinutes(options.MaxObservationGapMinutes).Ticks;
                bool restarted = clientState.Fingerprint != string.Empty && long.TryParse(clientState.Fingerprint, out var oldUptime)
                    && snapshot.UptimeSeconds < oldUptime;
                if (clientState.ActionState == "AwaitingProgress")
                {
                    if (snapshot.Safe && continuous && !restarted && ulong.TryParse(clientState.RecoveryProgress, out ulong previous)
                        && snapshot.TotalDownloadedBytes > previous)
                        clientState.ActionState = string.Empty;
                    else if (now.UtcTicks - clientState.ActionTicks >= TimeSpan.FromMinutes(options.RecoveryTimeoutMinutes).Ticks)
                    {
                        clientState.ActionState = "Halted";
                        clientState.Incident = "Recovery did not restore observed progress; manual review required";
                    }
                }
                // Pending means the process stopped between persisting intent and confirming the outcome.
                if (clientState.ActionState == "Pending")
                {
                    clientState.ActionState = "Halted";
                    clientState.Incident = "Interrupted or uncertain removal; inspect arr and NZBGet before manual reset";
                }
                clientState.Fingerprint = snapshot.UptimeSeconds?.ToString() ?? string.Empty;
                clientState.LastSeenTicks = now.UtcTicks;
                clientState.RecoveryProgress = snapshot.TotalDownloadedBytes?.ToString() ?? string.Empty;
                await events.SaveChangesAsync(ct);
                int runActions = 0;
                foreach (var job in snapshot.Jobs)
                {
                    string key = $"{config.Id}:{job.Id}";
                    var state = await GetStateAsync(key, config.Id, ct);
                    var matchingOwners = owners.Where(x => x.Record.DownloadClient != null
                        && string.Equals(x.Record.DownloadClient, config.Name, StringComparison.OrdinalIgnoreCase)
                        && job.Matches(x.Record.DownloadId)).ToList();
                    bool uniqueClient = matchingOwners.All(x => snapshot.Jobs.Count(j => j.Matches(x.Record.DownloadId)) == 1) && !owners.Any(x => job.Matches(x.Record.DownloadId)
                        && configs.Count(c => string.Equals(c.Name, x.Record.DownloadClient, StringComparison.OrdinalIgnoreCase)) != 1);
                    bool ignoredJob = ignored.Any(x => job.Name.Contains(x, StringComparison.OrdinalIgnoreCase)
                        || job.ArrIds.Contains(x, StringComparer.OrdinalIgnoreCase));
                    if (!uniqueClient || matchingOwners.Select(x => x.Instance.Id).Distinct().Count() != 1 || ignoredJob)
                    {
                        state.Samples = 0; state.LastSeenTicks = now.UtcTicks; state.UnchangedSinceTicks = now.UtcTicks;
                        state.Incident = ignoredJob ? "Ignored" : "Suspended: missing or ambiguous ownership";
                        await events.SaveChangesAsync(ct); continue;
                    }
                    var owner = matchingOwners[0];
                    bool supportedOwner = owner.Instance.ArrConfig.Type is InstanceType.Sonarr or InstanceType.Radarr;
                    bool consistentContent = owner.Instance.ArrConfig.Type == InstanceType.Sonarr
                        ? matchingOwners.All(x => x.Record.SeriesId == owner.Record.SeriesId && x.Record.SeasonNumber == owner.Record.SeasonNumber)
                        : matchingOwners.All(x => x.Record.MovieId == owner.Record.MovieId);
                    if (!supportedOwner || !consistentContent || instances.Any(x => x.ArrConfig.Type == InstanceType.LazyLibrarian))
                    {
                        state.Samples = 0; state.LastSeenTicks = now.UtcTicks;
                        state.Incident = "Suspended: unsupported owner or ambiguous content pack";
                        await events.SaveChangesAsync(ct); continue;
                    }
                    if (!owner.Client.IsRecordValid(owner.Record) || !owner.Client.HasContentId(owner.Record)) continue;
                    if (restarted) state.LastSeenTicks = 0;
                    string previousIncident = state.Incident;
                    string decision = UsenetEvaluator.Evaluate(state, job, snapshot, options, owner.Instance.Id, now);
                    bool candidate = decision.StartsWith("Candidate", StringComparison.Ordinal);
                    SetContext(config, owner);
                    // Successful history jobs remain eligible for existing failed-import rules. Active and unknown jobs never enter that path.
                    bool failedImport = false;
                    if (job.Phase == UsenetPhase.Completed && snapshot.Safe)
                    {
                        if (options.LiveCleanupEnabled && options.LiveValidationConfirmed && !await dryRun.IsDryRunEnabled()
                            && await forceImport.TryImportAsync(owner.Client, owner.Instance, owner.Record) != ForceImportOutcome.NotApplicable) continue;
                        failedImport = await owner.Client.ShouldRemoveFromQueue(owner.Instance.ArrConfig.Type, owner.Record, false, owner.Instance.ArrConfig.FailedImportMaxStrikes);
                    }
                    if (failedImport) state.Incident = "Candidate: failed import of completed history job";
                    bool terminalBadRelease = job.Phase == UsenetPhase.Downloading && job.Health.HasValue && job.CriticalHealth.HasValue
                        && job.Health < job.CriticalHealth && job.FailedArticles > 0;
                    // Pure zero progress can be a provider outage, and processing may be slow without reporting finer progress.
                    // Record candidates, but only a known bad release or completed failed import may be recovered automatically.
                    bool supportedAction = failedImport || candidate && terminalBadRelease;
                    if (candidate && !supportedAction) state.Incident = "Candidate: no progress; insufficient evidence for safe automatic removal";
                    await events.SaveChangesAsync(ct);
                    if (!supportedAction) continue;
                    if (!options.LiveCleanupEnabled || !options.LiveValidationConfirmed || await dryRun.IsDryRunEnabled())
                    {
                        const string message = "Would recover through owning arr (observation/dry-run)";
                        state.Incident = message;
                        await events.SaveChangesAsync(ct);
                        if (previousIncident != message) await publisher.PublishAsync(EventType.UsenetIncident, "Usenet: " + message, EventSeverity.Important);
                        continue;
                    }
                    if (state.ActionState.Length > 0 || clientState.ActionState.Length > 0 || runActions >= options.MaxActionsPerRun) continue;
                    if (clientState.ActionTicks > 0 && now.UtcTicks - clientState.ActionTicks < TimeSpan.FromMinutes(options.ReplacementCooldownMinutes).Ticks) continue;
                    int hourlyActions = await events.UsenetObservations.CountAsync(x => x.ClientId == config.Id && x.Id != clientKey
                        && x.ActionTicks >= now.UtcTicks - TimeSpan.FromHours(1).Ticks, ct);
                    if (hourlyActions >= options.MaxActionsPerHour) continue;
                    // Re-read all owners and client immediately before recording action intent.
                    var fresh = await usenet.InspectAsync(ct);
                    var freshJob = fresh.Jobs.SingleOrDefault(x => x.Id == job.Id && x.Fingerprint == job.Fingerprint);
                    if (!fresh.Safe || freshJob is null || freshJob.Progress != job.Progress || freshJob.Phase != job.Phase) continue;
                    bool freshOwnership = true;
                    foreach (var instance in instances.Where(x => x.ArrConfig.Type != InstanceType.LazyLibrarian))
                    {
                        var records = await ReadQueueAsync(arrs.GetClient(instance.ArrConfig.Type, instance.Version), instance, ct);
                        var matches = records.Where(x => freshJob.Matches(x.DownloadId)
                            && string.Equals(x.DownloadClient, config.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (instance.Id == owner.Instance.Id)
                            freshOwnership &= matches.Any(x => x.Id == owner.Record.Id && x.DownloadId == owner.Record.DownloadId
                                && x.Status == owner.Record.Status && x.TrackedDownloadState == owner.Record.TrackedDownloadState
                                && x.TrackedDownloadStatus == owner.Record.TrackedDownloadStatus);
                        else freshOwnership &= matches.Count == 0;
                    }
                    if (!freshOwnership || !failedImport && !(freshJob.Health < freshJob.CriticalHealth && freshJob.FailedArticles > 0)) continue;
                    var currentConfig = await data.DownloadClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == config.Id, ct);
                    if (currentConfig?.Enabled != true || currentConfig.UsenetOptions != options
                        || currentConfig.Host != config.Host || currentConfig.UrlBase != config.UrlBase
                        || currentConfig.Username != config.Username || currentConfig.Password != config.Password
                        || await dryRun.IsDryRunEnabled()) continue;
                    // Reserve the client with a database compare-and-set as well as the process lock.
                    // Intent and the job receipt commit together before any external mutation.
                    await using (var transaction = await events.Database.BeginTransactionAsync(ct))
                    {
                        long cooldownBoundary = now.UtcTicks - TimeSpan.FromMinutes(options.ReplacementCooldownMinutes).Ticks;
                        string recoveryProgress = fresh.TotalDownloadedBytes?.ToString() ?? string.Empty;
                        int reserved = await events.UsenetObservations
                            .Where(x => x.Id == clientKey && x.ActionState == "" && (x.ActionTicks == 0 || x.ActionTicks <= cooldownBoundary))
                            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ActionState, "Pending")
                                .SetProperty(x => x.ActionTicks, now.UtcTicks).SetProperty(x => x.RecoveryProgress, recoveryProgress), ct);
                        if (reserved != 1) continue;
                        state.ActionState = "Pending"; state.ActionTicks = now.UtcTicks;
                        clientState.ActionState = "Pending"; clientState.ActionTicks = now.UtcTicks;
                        clientState.RecoveryProgress = recoveryProgress;
                        await events.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                    }
                    forceImport.Forget(owner.Instance, owner.Record.DownloadId);
                    try
                    {
                        var search = owner.Instance.ArrConfig.Type == InstanceType.Sonarr
                            ? new SeriesSearchItem { Id = matchingOwners.Count > 1 ? owner.Record.SeasonNumber : owner.Record.EpisodeId,
                                SeriesId = owner.Record.SeriesId, SearchType = matchingOwners.Count > 1 ? SeriesSearchType.Season : SeriesSearchType.Episode }
                            : new SearchItem { Id = owner.Record.MovieId };
                        await remover.RemoveQueueItemAsync(new QueueItemRemoveRequest
                        {
                            Instance = owner.Instance, Target = new ArrRemovalTarget { Record = owner.Record, SearchItem = search,
                                RemoveFromClient = true, ChangeCategory = false },
                            DeleteReason = failedImport ? DeleteReason.FailedImport : DeleteReason.Stalled,
                            JobRunId = ContextProvider.GetJobRunId(), DownloadClient = config, SkipSearch = false,
                        });
                        state.ActionState = "Completed"; clientState.ActionState = "AwaitingProgress";
                        await RecordIncidentAsync(state, "Removed through arr; awaiting renewed queue progress", ct);
                        runActions++;
                    }
                    catch (Exception)
                    {
                        state.ActionState = "Uncertain"; clientState.ActionState = "Halted";
                        string outcome = "Uncertain removal or search outcome; automatic retries suspended";
                        try
                        {
                            var reconciled = await usenet.InspectAsync(ct);
                            var remaining = await ReadQueueAsync(owner.Client, owner.Instance, ct);
                            if (!reconciled.Jobs.Any(x => x.Id == job.Id) && !remaining.Any(x => job.Matches(x.DownloadId)))
                                outcome = "Removal observed after uncertain response; search outcome uncertain, no retry";
                        }
                        catch (Exception) { /* Preserve the uncertain action and halt; never infer success from a failed read. */ }
                        await RecordIncidentAsync(state, outcome, CancellationToken.None);
                    }
                    await events.SaveChangesAsync(CancellationToken.None);
                }
                // Retain unresolved action evidence indefinitely. Observations without actions expire after 30 days.
                long retention = now.AddDays(-30).UtcTicks;
                await events.UsenetObservations.Where(x => x.ClientId == config.Id && x.LastSeenTicks < retention
                    && x.ActionState == "" && x.ActionTicks == 0).ExecuteDeleteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning("Usenet inspection suspended for client {ClientId}: {ExceptionType}", config.Id, ex.GetType().Name); }
        }
    }

    private async Task<UsenetObservation> GetStateAsync(string id, Guid client, CancellationToken ct)
    {
        var state = await events.UsenetObservations.FindAsync([id], ct);
        if (state != null) return state;
        state = new() { Id = id, ClientId = client };
        events.UsenetObservations.Add(state);
        return state;
    }

    private async Task RecordIncidentAsync(UsenetObservation state, string message, CancellationToken ct)
    {
        if (state.Incident == message) return;
        state.Incident = message;
        await events.SaveChangesAsync(ct);
        await publisher.PublishAsync(EventType.UsenetIncident, "Usenet: " + message, EventSeverity.Important);
    }

    private static void SetContext(DownloadClientConfig config, Owner owner)
    {
        ContextProvider.SetDownloadClient(config);
        ContextProvider.Set(nameof(QueueRecord), owner.Record);
        ContextProvider.Set(nameof(InstanceType), owner.Instance.ArrConfig.Type);
        ContextProvider.Set(ContextProvider.Keys.ArrInstanceId, owner.Instance.Id);
        ContextProvider.Set(ContextProvider.Keys.ArrInstanceUrl, owner.Instance.ExternalOrInternalUrl);
        ContextProvider.Set(ContextProvider.Keys.Version, owner.Instance.Version);
        ContextProvider.Set(ContextProvider.Keys.Hash, owner.Record.DownloadId);
        ContextProvider.Set(ContextProvider.Keys.ItemName, owner.Record.Title);
    }

    private static async Task<List<QueueRecord>> ReadQueueAsync(IArrClient client, ArrInstance instance, CancellationToken ct)
    {
        List<QueueRecord> records = [];
        int? total = null;
        for (int page = 1; page <= 1000; page++)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetQueueItemsAsync(instance, page);
            total ??= response.TotalRecords;
            if (total != response.TotalRecords) throw new InvalidOperationException("Arr queue changed during ownership inspection");
            records.AddRange(response.Records);
            if (records.Count == total && records.Select(x => x.Id).Distinct().Count() == records.Count) return records;
            if (records.Count > total || response.Records.Count == 0) break;
        }
        throw new InvalidOperationException("Arr ownership queue read was incomplete");
    }
}
