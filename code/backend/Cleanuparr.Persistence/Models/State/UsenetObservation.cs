using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Cleanuparr.Persistence.Models.State;

[Table("usenet_observations")]
public sealed class UsenetObservation
{
    [Key] public string Id { get; set; } = string.Empty;
    public Guid ClientId { get; set; }
    public Guid OwnerId { get; set; }
    public long LastSeenTicks { get; set; }
    public long UnchangedSinceTicks { get; set; }
    public int Samples { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public string Incident { get; set; } = string.Empty;
    public string ActionState { get; set; } = string.Empty;
    public long ActionTicks { get; set; }
    public string RecoveryProgress { get; set; } = string.Empty;
}
