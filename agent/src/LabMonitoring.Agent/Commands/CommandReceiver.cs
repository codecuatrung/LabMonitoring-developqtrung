using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Commands;

/// <summary>
/// Verifies an incoming <see cref="CommandEnvelope"/>: signature, nonce (replay, TTL 60s) and
/// clock-skew (±5 min) — SRS 8.5. Execution is a separate step (<see cref="ICommandExecutor"/>),
/// kept out of here so this class stays a single-purpose gate.
/// </summary>
public sealed class CommandReceiver(CredentialStore credentials, ILogger<CommandReceiver> logger)
{
    private static readonly TimeSpan NonceTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly HashSet<Guid> _seenNonces = [];
    private readonly Lock _gate = new();

    public Task<CommandEnvelope?> VerifyAsync(JsonElement payload)
    {
        CommandEnvelope? cmd;
        try
        {
            cmd = payload.Deserialize<CommandEnvelope>(JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discarded malformed CommandIssued payload");
            return Task.FromResult<CommandEnvelope?>(null);
        }

        if (cmd is null)
            return Task.FromResult<CommandEnvelope?>(null);

        if (Math.Abs((DateTimeOffset.UtcNow - cmd.IssuedAt).TotalMinutes) > MaxClockSkew.TotalMinutes)
        {
            logger.LogWarning("Rejected command {CommandId}: clock skew too large ({IssuedAt:o})", cmd.CommandId, cmd.IssuedAt);
            return Task.FromResult<CommandEnvelope?>(null);
        }

        lock (_gate)
        {
            if (!_seenNonces.Add(cmd.Nonce))
            {
                logger.LogWarning("Rejected command {CommandId}: nonce replay", cmd.CommandId);
                return Task.FromResult<CommandEnvelope?>(null);
            }
        }
        _ = ExpireNonceLater(cmd.Nonce);

        if (!VerifySignature(cmd))
        {
            logger.LogWarning("Rejected command {CommandId}: bad signature", cmd.CommandId);
            return Task.FromResult<CommandEnvelope?>(null);
        }

        logger.LogInformation("Verified command {CommandId} type={Type}", cmd.CommandId, cmd.Type);
        return Task.FromResult<CommandEnvelope?>(cmd);
    }

    private bool VerifySignature(CommandEnvelope cmd)
    {
        var secret = credentials.Load()?.AgentSecret;
        if (string.IsNullOrEmpty(secret))
            return false;

        var expected = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(cmd.PayloadJson)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(cmd.Signature));
    }

    private async Task ExpireNonceLater(Guid nonce)
    {
        await Task.Delay(NonceTtl);
        lock (_gate) _seenNonces.Remove(nonce);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
