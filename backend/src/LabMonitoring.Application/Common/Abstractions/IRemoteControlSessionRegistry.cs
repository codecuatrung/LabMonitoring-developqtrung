namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// One active WebRTC remote-control session (ADR-0013). Tracks the two SignalR connections being
/// relayed between — the admin's Web Admin connection and the target Agent's connection — so
/// <c>LabHub</c> can forward SDP/ICE signaling 1:1 without broadcasting to the whole computer group.
/// </summary>
public sealed class RemoteControlSession
{
    public required Guid SessionId { get; init; }
    public required int ComputerId { get; init; }
    public required int RoomId { get; init; }
    public required string AdminConnectionId { get; init; }
    public required int AdminUserId { get; init; }
    public string? AgentConnectionId { get; set; }
    public required DateTime StartedAt { get; init; }
}

/// <summary>
/// In-memory registry of active remote-control sessions (ADR-0013). Single-instance MVP, same
/// accepted tradeoff as <see cref="IScreenAccessService"/>'s cache — no cross-instance coordination;
/// revisit only if the API ever scales out to multiple instances.
/// </summary>
public interface IRemoteControlSessionRegistry
{
    RemoteControlSession Start(int computerId, int roomId, string adminConnectionId, int adminUserId);

    RemoteControlSession? TryGet(Guid sessionId);

    /// <summary>Records the Agent's connection id once it starts signaling (first <c>SubmitOffer</c>).</summary>
    void SetAgentConnection(Guid sessionId, string agentConnectionId);

    /// <summary>True when the given machine already has a live session (enforces one-at-a-time).</summary>
    bool HasActiveSession(int computerId);

    RemoteControlSession? End(Guid sessionId);

    /// <summary>Ends whichever session (if any) the given SignalR connection is a party to.</summary>
    RemoteControlSession? EndByConnectionId(string connectionId);
}
