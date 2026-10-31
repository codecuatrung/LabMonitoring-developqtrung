namespace LabMonitoring.Domain.Policies;

/// <summary>
/// A known application referenced by whitelist/blacklist policies (SRS FR-4.4).
/// Named <c>ManagedApplication</c> to avoid colliding with the <c>LabMonitoring.Application</c> assembly;
/// the table stays "Applications".
/// </summary>
public class ManagedApplication
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string ExecutablePath { get; set; } = null!;

    public ICollection<ApplicationPolicy> Policies { get; set; } = [];
}
