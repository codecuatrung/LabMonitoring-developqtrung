namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Signs a control-command payload for one machine (SRS 8.5: "ký số phía server"). The concrete
/// implementation recovers that machine's AgentSecret (encrypted at rest) and computes
/// <c>base64(HMAC-SHA256(payloadJson, AgentSecret))</c> — kept out of Application so this layer
/// never touches key material directly.
/// </summary>
public interface ICommandSigner
{
    Task<string> SignForComputerAsync(int computerId, string payloadJson, CancellationToken ct = default);
}
