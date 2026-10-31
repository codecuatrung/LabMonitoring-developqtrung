namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Sends alert emails (SRS FR-7.2). Best-effort by contract — a failure here must never break the
/// caller (heartbeat processing, offline sweep, …); implementations log and swallow their own errors.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
