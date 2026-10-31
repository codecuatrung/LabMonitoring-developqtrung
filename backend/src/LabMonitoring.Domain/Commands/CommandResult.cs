using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;

namespace LabMonitoring.Domain.Commands;

/// <summary>Per-machine execution outcome of a <see cref="Command"/> (SRS FR-4.5).</summary>
public class CommandResult
{
    public long Id { get; set; }

    public long CommandId { get; set; }
    public Command Command { get; set; } = null!;

    public int ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public CommandResultStatus Status { get; set; } = CommandResultStatus.Sent;
    public DateTime? ExecutedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
