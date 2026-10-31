using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Commands;

/// <summary>Client-side re-check of the whitelist synced via /agent/config (SRS 8.5, FR-4.4). Pure —
/// no I/O — so it's straightforward to unit test.</summary>
public static class WhitelistChecker
{
    public static bool IsAppAllowed(string executablePath, AgentConfigResponse? config)
    {
        if (config is null) return false; // no synced config yet -> fail closed
        if (Contains(config.BlockedApplications, executablePath)) return false;
        return Contains(config.AllowedApplications, executablePath);
    }

    public static bool IsUrlAllowed(string url, AgentConfigResponse? config)
    {
        if (config is null) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host.ToLowerInvariant();
        if (config.BlockedUrls.Any(d => Matches(host, d))) return false;
        return config.AllowedUrls.Any(d => Matches(host, d));
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
        => list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    private static bool Matches(string host, string domain)
    {
        domain = domain.ToLowerInvariant();
        return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    }
}
