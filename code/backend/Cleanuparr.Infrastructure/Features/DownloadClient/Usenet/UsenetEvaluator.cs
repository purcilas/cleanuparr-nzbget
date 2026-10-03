using Cleanuparr.Persistence.Models.Configuration;
using Cleanuparr.Persistence.Models.State;

namespace Cleanuparr.Infrastructure.Features.DownloadClient.Usenet;

public static class UsenetEvaluator
{
    public static string Evaluate(UsenetObservation state, UsenetJob job, UsenetSnapshot snapshot,
        UsenetOptions options, Guid owner, DateTimeOffset now)
    {
        long ticks = now.UtcTicks;
        bool eligible = job.IdentifiersKnown && !string.IsNullOrWhiteSpace(job.Name) && snapshot.Safe && !job.HasPausedFiles && job.ArrIds.Count <= 1 &&
            (job.Phase == UsenetPhase.Downloading && job.RemainingBytes > 0 && job.DownloadedBytes.HasValue
                && job.SuccessfulArticles >= 0 && job.FailedArticles >= 0
             || job.Phase == UsenetPhase.Processing && job.StageProgress is >= 0 and <= 1000);
        bool reset = state.OwnerId != owner || state.Fingerprint != job.Fingerprint || state.Progress != job.Progress
            || state.LastSeenTicks == 0 || ticks <= state.LastSeenTicks
            || ticks - state.LastSeenTicks > TimeSpan.FromMinutes(options.MaxObservationGapMinutes).Ticks || !eligible;
        if (reset) { state.UnchangedSinceTicks = ticks; state.Samples = eligible ? 1 : 0; }
        else state.Samples++;
        state.OwnerId = owner; state.Fingerprint = job.Fingerprint; state.Progress = job.Progress; state.LastSeenTicks = ticks;
        if (!eligible) return state.Incident = snapshot.Safe ? "Suspended: waiting, paused, unknown or incomplete job" : "Suspended: " + snapshot.SafetyReason;
        int threshold = job.Phase == UsenetPhase.Processing ? options.ProcessingStallMinutes : options.StallMinutes;
        bool stalled = state.Samples >= options.RequiredObservations && ticks - state.UnchangedSinceTicks >= TimeSpan.FromMinutes(threshold).Ticks;
        return state.Incident = stalled ? "Candidate: no progress" : "Observing";
    }
}
