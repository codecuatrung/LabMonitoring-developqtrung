using System.Collections.Concurrent;
using LabMonitoring.Application.Common.Abstractions;

namespace LabMonitoring.Infrastructure.RemoteControl;

/// <summary>In-memory implementation of <see cref="IRemoteControlSessionRegistry"/> (ADR-0013).</summary>
internal sealed class RemoteControlSessionRegistry : IRemoteControlSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, RemoteControlSession> _sessions = new();

    public RemoteControlSession Start(int computerId, int roomId, string adminConnectionId, int adminUserId)
    {
        var session = new RemoteControlSession
        {
            SessionId = Guid.NewGuid(),
            ComputerId = computerId,
            RoomId = roomId,
            AdminConnectionId = adminConnectionId,
            AdminUserId = adminUserId,
            StartedAt = DateTime.UtcNow,
        };
        _sessions[session.SessionId] = session;
        return session;
    }

    public RemoteControlSession? TryGet(Guid sessionId)
        => _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public void SetAgentConnection(Guid sessionId, string agentConnectionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            session.AgentConnectionId = agentConnectionId;
    }

    public bool HasActiveSession(int computerId)
        => _sessions.Values.Any(s => s.ComputerId == computerId);

    public RemoteControlSession? End(Guid sessionId)
        => _sessions.TryRemove(sessionId, out var session) ? session : null;

    public RemoteControlSession? EndByConnectionId(string connectionId)
    {
        var match = _sessions.Values.FirstOrDefault(
            s => s.AdminConnectionId == connectionId || s.AgentConnectionId == connectionId);
        if (match is null)
            return null;

        _sessions.TryRemove(match.SessionId, out _);
        return match;
    }
}
