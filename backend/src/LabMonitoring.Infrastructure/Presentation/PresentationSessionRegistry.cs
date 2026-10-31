using System.Collections.Concurrent;
using LabMonitoring.Application.Common.Abstractions;

namespace LabMonitoring.Infrastructure.Presentation;

internal sealed class PresentationSessionRegistry : IPresentationSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, PresentationSession> _sessions = new();
    private readonly object _startGate = new();

    public PresentationSession? TryStart(int roomId, string adminConnectionId, int adminUserId, bool lockInput)
    {
        lock (_startGate)
        {
            if (_sessions.Values.Any(s => s.RoomId == roomId))
                return null;

            var now = DateTime.UtcNow;
            var session = new PresentationSession
            {
                SessionId = Guid.NewGuid(),
                RoomId = roomId,
                AdminConnectionId = adminConnectionId,
                AdminUserId = adminUserId,
                LockInput = lockInput,
                StartedAt = now,
                LastFrameAt = now,
            };
            _sessions[session.SessionId] = session;
            return session;
        }
    }

    public PresentationSession? TryGet(Guid sessionId)
        => _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public void Touch(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            session.LastFrameAt = DateTime.UtcNow;
    }

    public PresentationSession? End(Guid sessionId)
        => _sessions.TryRemove(sessionId, out var session) ? session : null;

    public PresentationSession? EndByConnectionId(string connectionId)
    {
        var match = _sessions.Values.FirstOrDefault(s => s.AdminConnectionId == connectionId);
        return match is not null && _sessions.TryRemove(match.SessionId, out var removed) ? removed : null;
    }

    public IReadOnlyList<PresentationSession> EndIdle(TimeSpan idle)
    {
        var cutoff = DateTime.UtcNow - idle;
        var ended = new List<PresentationSession>();
        foreach (var s in _sessions.Values.Where(s => s.LastFrameAt < cutoff))
            if (_sessions.TryRemove(s.SessionId, out var removed))
                ended.Add(removed);
        return ended;
    }
}
