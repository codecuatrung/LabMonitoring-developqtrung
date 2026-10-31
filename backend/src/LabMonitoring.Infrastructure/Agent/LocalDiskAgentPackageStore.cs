using System.Security.Cryptography;
using LabMonitoring.Application.Common.Abstractions;

namespace LabMonitoring.Infrastructure.Agent;

/// <summary>Stores uploaded LabAgent update packages under <see cref="ApiPaths.AgentPackagesDirectory"/>
/// (SRS H1 "auto-update"). Single-server MVP tradeoff, same as the rest of this directory's
/// abstractions — swap for an object-storage implementation later without touching callers.</summary>
internal sealed class LocalDiskAgentPackageStore : IAgentPackageStore
{
    public async Task<(string Sha256, long SizeBytes)> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var path = ResolvePath(fileName);
        using var sha256 = SHA256.Create();
        await using (var file = File.Create(path))
        await using (var hashing = new CryptoStream(file, sha256, CryptoStreamMode.Write))
        {
            await content.CopyToAsync(hashing, ct);
        }

        var hash = Convert.ToHexStringLower(sha256.Hash!);
        return (hash, new FileInfo(path).Length);
    }

    public Task<Stream> OpenReadAsync(string fileName, CancellationToken ct = default) =>
        Task.FromResult<Stream>(File.OpenRead(ResolvePath(fileName)));

    private static string ResolvePath(string fileName)
    {
        // fileName is built from an admin-supplied version string (AgentUpdatesController) — guard
        // against path traversal here too, defense in depth beyond the controller's own validation.
        if (fileName.Contains("..") || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException($"Invalid package file name '{fileName}'.", nameof(fileName));

        return Path.Combine(ApiPaths.AgentPackagesDirectory, fileName);
    }
}
