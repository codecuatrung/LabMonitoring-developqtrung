namespace LabMonitoring.Agent.RemoteControl;

/// <summary>
/// Newline-delimited JSON message exchanged over the local named pipe between the Worker Service
/// (this process, which alone holds the SignalR/AgentToken connection) and the RemoteHelper process
/// (which runs inside the interactive session and owns the actual WebRTC peer connection). One
/// tagged-union shape for every message keeps the protocol trivial to parse on both ends.
///
/// Deliberately duplicated in <c>LabMonitoring.Agent.RemoteHelper</c> rather than shared via a
/// project reference — same "duplicate contracts on purpose" convention used for
/// <c>CommandEnvelope</c> (ADR-0002/0006), since the two processes are independently deployed.
///
/// Helper → main: "offer" (Sdp set), "localIce" (Candidate/SdpMid/SdpMLineIndex set).
/// Main → helper: "answer" (Sdp set), "remoteIce" (Candidate/SdpMid/SdpMLineIndex set), "stop".
/// </summary>
internal sealed class PipeMessage
{
    public string Type { get; set; } = "";
    public string? Sdp { get; set; }
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int? SdpMLineIndex { get; set; }
}
