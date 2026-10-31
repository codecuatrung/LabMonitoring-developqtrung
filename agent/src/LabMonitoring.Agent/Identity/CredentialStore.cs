using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Identity;

public sealed record AgentCredentials(int ComputerId, string ComputerName, string AgentToken, string AgentSecret);

/// <summary>
/// Persists the AgentToken + AgentSecret. On Windows the file is encrypted with DPAPI
/// (LocalMachine scope, so the LocalSystem service and admins can read it). Elsewhere it is
/// written 0600 as plaintext (a warning is logged) until a platform keystore is wired in.
/// </summary>
public sealed class CredentialStore(AgentPaths paths, ILogger<CredentialStore> logger)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LabMonitoring.Agent.v1");

    public AgentCredentials? Load()
    {
        if (!File.Exists(paths.CredentialsFile))
            return null;

        try
        {
            var raw = File.ReadAllBytes(paths.CredentialsFile);
            var json = OperatingSystem.IsWindows()
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.LocalMachine))
                : Encoding.UTF8.GetString(raw);
            return JsonSerializer.Deserialize<AgentCredentials>(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read agent credentials; will re-register.");
            return null;
        }
    }

    public void Save(AgentCredentials credentials)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(paths.CredentialsFile, ProtectedData.Protect(json, Entropy, DataProtectionScope.LocalMachine));
        }
        else
        {
            logger.LogWarning("DPAPI unavailable on this platform; storing agent credentials as a 0600 plaintext file.");
            File.WriteAllBytes(paths.CredentialsFile, json);
            try { File.SetUnixFileMode(paths.CredentialsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best effort */ }
        }
    }

    public void Clear()
    {
        if (File.Exists(paths.CredentialsFile))
            File.Delete(paths.CredentialsFile);
    }
}
