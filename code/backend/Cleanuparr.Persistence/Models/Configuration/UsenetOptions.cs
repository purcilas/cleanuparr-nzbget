using Cleanuparr.Domain.Exceptions;

namespace Cleanuparr.Persistence.Models.Configuration;

public sealed record UsenetOptions
{
    public bool LiveCleanupEnabled { get; init; }
    public bool LiveValidationConfirmed { get; init; }
    public int StallMinutes { get; init; } = 60;
    public int ProcessingStallMinutes { get; init; } = 240;
    public int RequiredObservations { get; init; } = 3;
    public int MaxObservationGapMinutes { get; init; } = 10;
    public int MaxActionsPerRun { get; init; } = 1;
    public int MaxActionsPerHour { get; init; } = 1;
    public int ReplacementCooldownMinutes { get; init; } = 120;
    public int RecoveryTimeoutMinutes { get; init; } = 30;
    public int MinimumFreeDiskMiB { get; init; } = 1024;

    public void Validate()
    {
        if (StallMinutes < 5 || StallMinutes > 10080 || ProcessingStallMinutes < 60 || ProcessingStallMinutes > 10080
            || RequiredObservations < 2 || RequiredObservations > 100 || MaxObservationGapMinutes < 1 || MaxObservationGapMinutes > 60
            || MaxActionsPerRun < 1 || MaxActionsPerRun > 5 || MaxActionsPerHour < 1 || MaxActionsPerHour > 10
            || ReplacementCooldownMinutes < 30 || ReplacementCooldownMinutes > 10080 || RecoveryTimeoutMinutes < 5
            || RecoveryTimeoutMinutes > 1440 || MinimumFreeDiskMiB < 256)
            throw new ValidationException("Usenet rule limits are outside the supported safety range");
        if (LiveCleanupEnabled && !LiveValidationConfirmed)
            throw new ValidationException("Validate read-only and dry-run observations against real stalls before enabling live Usenet cleanup");
    }
}
