using System.Security.Cryptography;
using System.Text;

namespace LabMonitoring.Infrastructure.Security;

/// <summary>
/// Generates and verifies the opaque per-machine AgentToken and AgentSecret. Only SHA-256 hashes
/// are persisted (<c>Computers.AgentTokenHash</c> / <c>AgentSecretHash</c>); plaintext is shown once.
/// </summary>
public static class SecretHasher
{
    /// <summary>A URL-safe random secret with ~256 bits of entropy.</summary>
    public static string NewSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool Verify(string value, string expectedHash)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(value)),
            Encoding.ASCII.GetBytes(expectedHash));
}
