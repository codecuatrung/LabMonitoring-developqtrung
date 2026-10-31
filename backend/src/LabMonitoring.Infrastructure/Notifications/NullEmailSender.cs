using LabMonitoring.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Infrastructure.Notifications;

/// <summary>
/// Dev-safe fallback used while no real SMTP server is configured (<c>Email:Enabled=false</c>,
/// the default) — logs what would have been sent instead of failing or silently dropping it.
/// Swap for <see cref="SmtpEmailSender"/> at any time by setting <c>Email:Enabled=true</c> +
/// <c>Email:Host</c>, no code change needed.
/// </summary>
internal sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("[Email disabled] Would send to {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
