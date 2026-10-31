namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// One active screen-presentation session: an admin's browser streams JPEG frames of their screen
/// to every machine in a room. Start/Stop reach the Agents as signed commands; frames flow through
/// <c>LabHub</c> to the <c>presentation:{sessionId}</c> group the Agents join.
/// </summary>
public sealed class PresentationSession
{
    public required Guid SessionId { get; init; }
    public required int RoomId { get; init; }
    public required string AdminConnectionId { get; init; }
    public required int AdminUserId { get; init; }
    public required bool LockInput { get; init; }
    public required DateTime StartedAt { get; init; }
    public DateTime LastFrameAt { get; set; }
}

/// <summary>In-memory registry (single API instance — same accepted tradeoff as <see cref="IRemoteControlSessionRegistry"/>).</summary>
public interface IPresentationSessionRegistry
{
    /// <summary>Returns null when the room already has a live session.</summary>
    PresentationSession? TryStart(int roomId, string adminConnectionId, int adminUserId, bool lockInput);

    PresentationSession? TryGet(Guid sessionId);

    void Touch(Guid sessionId);

    PresentationSession? End(Guid sessionId);

    PresentationSession? EndByConnectionId(string connectionId);

    /// <summary>Removes and returns sessions with no frame for longer than <paramref name="idle"/>.</summary>
    IReadOnlyList<PresentationSession> EndIdle(TimeSpan idle);
}
