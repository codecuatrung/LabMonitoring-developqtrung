namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>Where uploaded LabAgent update packages live (SRS H1 "auto-update"). Kept behind an
/// abstraction the same way <see cref="IWakeOnLanSender"/>/<see cref="IEmailSender"/> are — v1 stores
/// on local disk (<c>LocalDiskAgentPackageStore</c>), but the download/upload paths in
/// <c>AgentUpdatesController</c>/<c>AgentController</c> never touch the filesystem directly.</summary>
public interface IAgentPackageStore
{
    /// <summary>Saves the stream under <paramref name="fileName"/> and returns its SHA-256 (lowercase
    /// hex) and size, computed while writing so the caller never has to re-read the file.</summary>
    Task<(string Sha256, long SizeBytes)> SaveAsync(Stream content, string fileName, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string fileName, CancellationToken ct = default);
}
