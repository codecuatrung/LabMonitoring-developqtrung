using Microsoft.AspNetCore.DataProtection;

namespace LabMonitoring.Infrastructure.Security;

/// <summary>
/// Encrypts/decrypts the per-machine AgentSecret at rest (<c>Computers.AgentSecretHash</c>).
/// </summary>
/// <remarks>
/// The ERD names the column "…Hash", but a one-way hash cannot satisfy SRS 8.1/8.5: the SERVER
/// signs each command ("ký số phía server") with <c>HMAC-SHA256(payload, AgentSecret)</c>, which
/// requires the server to recover the plaintext secret. So this stores a reversible, encrypted
/// value (ASP.NET Core Data Protection) rather than a SHA-256 digest — see ADR-0009.
/// <c>Computers.AgentTokenHash</c> is unaffected: that one only needs to be *verified* (find the
/// computer whose stored hash matches an incoming token), never reproduced, so a true one-way
/// hash (<see cref="SecretHasher"/>) remains correct for it.
/// </remarks>
public sealed class AgentSecretProtector
{
    private const string Purpose = "LabMonitoring.AgentSecret.v1";
    private readonly IDataProtector _protector;

    public AgentSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintextSecret) => _protector.Protect(plaintextSecret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
