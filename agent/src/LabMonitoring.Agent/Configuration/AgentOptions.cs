using System.ComponentModel.DataAnnotations;

namespace LabMonitoring.Agent.Configuration;

/// <summary>Binds the "Agent" configuration section on the client machine.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base address of the Lab Monitoring API, e.g. https://labmonitoring.local .</summary>
    [Required]
    public required string ServerBaseUrl { get; init; }

    /// <summary>Room registration code entered by the technician during first-time install.</summary>
    public string? RoomCode { get; init; }

    /// <summary>Heartbeat interval in seconds (server config overrides this after registration).</summary>
    [Range(3, 60)]
    public int HeartbeatSeconds { get; init; } = 5;

    /// <summary>Offline outbox capacity before oldest records are dropped (NFR: 500).</summary>
    [Range(50, 5000)]
    public int OutboxCapacity { get; init; } = 500;
}
