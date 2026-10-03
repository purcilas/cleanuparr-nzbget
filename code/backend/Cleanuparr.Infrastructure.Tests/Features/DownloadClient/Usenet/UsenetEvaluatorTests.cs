using System.Text.Json;
using Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;
using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.State;
using Shouldly;
using Xunit;

namespace Cleanuparr.Infrastructure.Tests.Features.DownloadClient.Usenet;

public sealed class UsenetEvaluatorTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    public static UsenetJob Job => new(7, "release", "tv", ["drone-id"], UsenetPhase.Downloading, "DOWNLOADING", 200, 100, null, 3, 1, false, 100, 200);
    public static UsenetSnapshot Snapshot(UsenetJob? job = null) => new([job ?? Job], 100, 200, true, "Ready");
    private static readonly UsenetOptions Options = new() { StallMinutes = 5, RequiredObservations = 2 };
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T12:00:00Z");

    [Fact]
    public void Active_job_requires_elapsed_time_and_multiple_samples()
    {
        var state = new UsenetObservation();
        UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Owner, Start).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Owner, Start.AddMinutes(3)).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Owner, Start.AddMinutes(6)).ShouldBe("Candidate: no progress");
    }

    [Theory]
    [InlineData(UsenetPhase.Waiting)] [InlineData(UsenetPhase.Paused)] [InlineData(UsenetPhase.Unknown)]
    [InlineData(UsenetPhase.Completed)] [InlineData(UsenetPhase.Failed)]
    public void Ineligible_states_never_accumulate_samples(UsenetPhase phase)
    {
        var state = new UsenetObservation(); var job = Job with { Phase = phase };
        UsenetEvaluator.Evaluate(state, job, Snapshot(job), Options, Owner, Start);
        UsenetEvaluator.Evaluate(state, job, Snapshot(job), Options, Owner, Start.AddMinutes(9));
        state.Samples.ShouldBe(0);
    }

    [Fact]
    public void Progress_pause_missing_counter_and_gaps_reset_the_window()
    {
        foreach (var changed in new[] { Job with { DownloadedBytes = 101 }, Job with { HasPausedFiles = true }, Job with { RemainingBytes = null } })
        {
            var state = new UsenetObservation();
            UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Owner, Start);
            UsenetEvaluator.Evaluate(state, changed, Snapshot(changed), Options, Owner, Start.AddMinutes(6)).ShouldNotStartWith("Candidate");
        }
        var gap = new UsenetObservation();
        UsenetEvaluator.Evaluate(gap, Job, Snapshot(), Options, Owner, Start);
        UsenetEvaluator.Evaluate(gap, Job, Snapshot(), Options, Owner, Start.AddMinutes(11)).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(gap, Job, Snapshot() with { Safe = false }, Options, Owner, Start.AddMinutes(12)).ShouldStartWith("Suspended");
        gap.Samples.ShouldBe(0);
    }

    [Fact]
    public void Processing_uses_stage_progress_and_a_separate_threshold()
    {
        var options = Options with { ProcessingStallMinutes = 60, MaxObservationGapMinutes = 60 };
        var job = Job with { Phase = UsenetPhase.Processing, Stage = "UNPACKING", StageProgress = 1 };
        var state = new UsenetObservation();
        UsenetEvaluator.Evaluate(state, job, Snapshot(job), options, Owner, Start);
        UsenetEvaluator.Evaluate(state, job, Snapshot(job), options, Owner, Start.AddMinutes(30)).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(state, job with { StageProgress = 2 }, Snapshot(job), options, Owner, Start.AddMinutes(60)).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(state, job with { StageProgress = 2 }, Snapshot(job), options, Owner, Start.AddMinutes(120)).ShouldBe("Candidate: no progress");
        UsenetEvaluator.Evaluate(state, job with { Stage = "MOVING", StageProgress = 2 }, Snapshot(job), options, Owner, Start.AddMinutes(121)).ShouldBe("Observing");
    }

    [Fact]
    public void Owner_or_fingerprint_changes_reset_observations()
    {
        var state = new UsenetObservation();
        UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Owner, Start);
        UsenetEvaluator.Evaluate(state, Job, Snapshot(), Options, Guid.NewGuid(), Start.AddMinutes(6)).ShouldBe("Observing");
        UsenetEvaluator.Evaluate(state, Job with { Name = "replacement" }, Snapshot(), Options, Owner, Start.AddMinutes(7)).ShouldBe("Observing");
    }

    [Fact]
    public void Drone_mapping_has_priority_over_numeric_id_and_large_counters_are_unsigned()
    {
        using var doc = JsonDocument.Parse("""{"NZBID":7,"NZBName":"r","Status":"DOWNLOADING","DownloadedSizeHi":4294967295,"DownloadedSizeLo":4294967295,"Parameters":[{"Name":"drone","Value":"abc"}]}""");
        var job = NzbGetService.Normalize(doc.RootElement, false);
        job.Matches("ABC").ShouldBeTrue(); job.Matches("7").ShouldBeFalse();
        job.DownloadedBytes.ShouldBe(ulong.MaxValue);
        (job with { ArrIds = [] }).Matches("7").ShouldBeTrue();
        using var missing = JsonDocument.Parse("{}");
        NzbGetService.Counter(missing.RootElement, "DownloadedSize").ShouldBeNull();
        using var noParameters = JsonDocument.Parse("{\"NZBID\":7}");
        NzbGetService.Normalize(noParameters.RootElement, false).Matches("7").ShouldBeFalse();
    }
}
