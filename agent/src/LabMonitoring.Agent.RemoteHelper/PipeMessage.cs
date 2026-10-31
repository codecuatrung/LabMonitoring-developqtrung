namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// Mirrors <c>LabMonitoring.Agent.RemoteControl.PipeMessage</c> in the main Worker Service project —
/// deliberately duplicated rather than shared, same convention as <c>CommandEnvelope</c>
/// (ADR-0002/0006/0013).
/// </summary>
internal sealed class PipeMessage
{
    public string Type { get; set; } = "";
    public string? Sdp { get; set; }
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}
