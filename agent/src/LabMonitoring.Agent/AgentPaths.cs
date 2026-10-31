namespace LabMonitoring.Agent;

/// <summary>Filesystem locations used by the Agent (survive service restarts). Injectable so tests
/// can redirect it to a scratch directory.</summary>
public sealed class AgentPaths
{
    public AgentPaths() : this(ResolveDefaultRoot()) { }

    public AgentPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string CredentialsFile => Path.Combine(DataDirectory, "credentials.dat");
    public string OutboxFile => Path.Combine(DataDirectory, "outbox.jsonl");
    public string MachineIdFile => Path.Combine(DataDirectory, "machine-id");

    private static string ResolveDefaultRoot()
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LabMonitoring", "Agent")
            : "/var/lib/labmonitoring-agent";
    }
}
