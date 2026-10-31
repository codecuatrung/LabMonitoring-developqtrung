namespace LabMonitoring.Infrastructure.Agent;

/// <summary>Binds "AgentDefaults" — the polling/threshold values handed to Agents via /agent/config.</summary>
public sealed class AgentDefaultsOptions
{
    public const string SectionName = "AgentDefaults";

    public int HeartbeatSeconds { get; init; } = 5;
    public int OfflineThresholdSeconds { get; init; } = 30;
    public int ScreenshotIntervalSeconds { get; init; } = 60;

    /// <summary>A command with no terminal result after this long is marked Timeout (SRS FR-4.5).</summary>
    public int CommandTimeoutSeconds { get; init; } = 60;

    /// <summary>Longer timeout for <c>UpdateAgent</c> specifically (SRS H1 "auto-update") — download +
    /// verify + service restart legitimately takes longer than the default window, and the Agent's
    /// final result is reported late (by the process that comes back up after the restart), not
    /// synchronously like every other command kind.</summary>
    public int AgentUpdateTimeoutSeconds { get; init; } = 300;

    /// <summary>Timeout for <c>PushFile</c>/<c>CollectFiles</c>: a 500 MB download or a zip+upload of a folder can take minutes.</summary>
    public int FileTransferTimeoutSeconds { get; init; } = 900;

    /// <summary>Nonce validity window for signed commands (SRS 8.5).</summary>
    public int CommandNonceTtlSeconds { get; init; } = 60;
}
