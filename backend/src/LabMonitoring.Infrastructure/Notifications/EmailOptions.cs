namespace LabMonitoring.Infrastructure.Notifications;

/// <summary>Binds "Email" — SMTP settings for alert emails (SRS FR-7.2). Unset/disabled by default;
/// see <see cref="NullEmailSender"/> for the dev-safe fallback.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = "no-reply@labmonitoring.local";
    public string FromName { get; init; } = "Lab Monitoring";
}
