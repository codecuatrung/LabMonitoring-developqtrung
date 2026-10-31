using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Domain.Identity;
using LabMonitoring.Domain.Sessions;

namespace LabMonitoring.Domain.Commands;

/// <summary>
/// A control command issued to one machine or a whole room (SRS FR-4). Signed server-side;
/// <see cref="Nonce"/> is unique for replay protection (SRS FR-4.2).
/// </summary>
public class Command
{
    public long Id { get; set; }

    /// <summary>Null when the command targets a whole room.</summary>
    public int? ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    public int? SessionId { get; set; }
    public Session? Session { get; set; }

    public CommandKind CommandType { get; set; }

    /// <summary>JSON: message text, app path, URL, …</summary>
    public string? Payload { get; set; }

    public int IssuedById { get; set; }
    public User IssuedBy { get; set; } = null!;

    public DateTime IssuedAt { get; set; }

    public Guid Nonce { get; set; }

    /// <summary>base64(HMAC-SHA256(Payload, AgentSecret)).</summary>
    public string Signature { get; set; } = null!;

    public ICollection<CommandResult> Results { get; set; } = [];
}
