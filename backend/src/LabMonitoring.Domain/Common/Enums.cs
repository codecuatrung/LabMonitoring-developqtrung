namespace LabMonitoring.Domain.Common;

/// <summary>Realtime state of a workstation (SRS FR-1.1 / ComputerStatus.CurrentStatus).</summary>
public enum ComputerState
{
    Online,
    Offline,
    Warning,
    InUse,
}

/// <summary>Lifecycle of a lab session (SRS Sessions.Status).</summary>
public enum SessionStatus
{
    Scheduled,
    Active,
    Ended,
    Cancelled,
}

/// <summary>Control command kinds (SRS FR-4.1 / Commands.CommandType).</summary>
public enum CommandKind
{
    Lock,
    Restart,
    Shutdown,
    Message,
    OpenApp,
    OpenUrl,
    UpdateAgent,
    StartPresentation,
    StopPresentation,
    PushFile,
    CollectFiles,
}

/// <summary>Per-machine execution state of a command (SRS FR-4.5 / CommandResults.Status).</summary>
public enum CommandResultStatus
{
    Sent,
    Acknowledged,
    Executed,
    Failed,
    Timeout,
}

/// <summary>Whitelist / blacklist policy decision (SRS ApplicationPolicies / WebsitePolicies).</summary>
public enum PolicyType
{
    Allow,
    Block,
}

/// <summary>Where a screenshot file lives (SRS Screenshots.StorageProvider).</summary>
public enum StorageProvider
{
    Nas,
    ObjectStorage,
}

/// <summary>Notification severity (SRS Notifications.Severity).</summary>
public enum NotificationSeverity
{
    Info,
    Warning,
    Critical,
}
