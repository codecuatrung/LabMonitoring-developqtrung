namespace LabMonitoring.Infrastructure;

/// <summary>Filesystem locations used by the API host (Data Protection key ring, uploaded Agent
/// update packages).</summary>
public static class ApiPaths
{
    public static string DataProtectionKeyDirectory { get; } = Resolve("dp-keys");

    /// <summary>Uploaded LabAgent build zips (SRS H1 "auto-update") — see <c>AgentUpdatesController</c>.</summary>
    public static string AgentPackagesDirectory { get; } = Resolve("agent-packages");

    /// <summary>Files pushed to clients and zips collected from them (file transfer) - see <c>FileTransfersController</c>.</summary>
    public static string FileTransfersDirectory { get; } = Resolve("file-transfers");

    private static string Resolve(string subdirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabMonitoring", "Api", subdirectory);
            Directory.CreateDirectory(root);
            return root;
        }

        var linuxRoot = $"/var/lib/labmonitoring-api/{subdirectory}";
        try
        {
            Directory.CreateDirectory(linuxRoot);
            return linuxRoot;
        }
        catch (UnauthorizedAccessException)
        {
            // Docker (deploy/docker-compose.dev.yml) mounts /var/lib/labmonitoring-api/* as volumes
            // the container can write to, but a bare non-root process — CI runners, `dotnet test` on
            // a dev machine's WSL, etc. — can't create anything under /var/lib. Neither directory
            // needs to survive there, so fall back to a per-run temp location instead of taking the
            // whole app down.
            var fallback = Path.Combine(Path.GetTempPath(), $"labmonitoring-api-{subdirectory}");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
