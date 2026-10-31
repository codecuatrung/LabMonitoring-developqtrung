using System.Text.Json;
using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Abstractions;
using LabMonitoring.Application.Common.Realtime;
using LabMonitoring.Domain.Auditing;
using LabMonitoring.Domain.Common;
using LabMonitoring.Domain.Facilities;
using LabMonitoring.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LabMonitoring.Api.Realtime;

/// <summary>
/// Realtime hub (SRS 8.4). Web Admin clients subscribe to the rooms they are viewing;
/// Agents are placed in their room group (dashboards) and their own computer group
/// (targeted command delivery) on connect.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemes.Jwt + "," + AuthSchemes.AgentToken)]
public sealed class LabHub(
    IRoomScope roomScope,
    IAppDbContext db,
    IRealtimeNotifier realtime,
    IRemoteControlSessionRegistry remoteControl,
    IPresentationSessionRegistry presentations,
    IMediator mediator) : Hub
{
    internal static string RoomGroup(int roomId) => $"room:{roomId}";
    internal static string ComputerGroup(int computerId) => $"computer:{computerId}";
    internal static string UserGroup(int userId) => $"user:{userId}";
    internal static string PresentationGroup(Guid sessionId) => $"presentation:{sessionId}";

    public override async Task OnConnectedAsync()
    {
        // Agents join their own room (so dashboards can address "everyone in this room" events
        // like NewAlert) and their own computer group (so commands can be addressed 1:1).
        var roomClaim = Context.User?.FindFirst(LabClaimTypes.RoomId)?.Value;
        var computerClaim = Context.User?.FindFirst(LabClaimTypes.ComputerId)?.Value;

        if (int.TryParse(roomClaim, out var agentRoomId))
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(agentRoomId));
        if (int.TryParse(computerClaim, out var agentComputerId))
            await Groups.AddToGroupAsync(Context.ConnectionId, ComputerGroup(agentComputerId));

        // Web Admin clients join their own user group, so in-app notifications (SRS FR-7.2) can be
        // addressed 1:1 without the client having to subscribe explicitly.
        var userClaim = Context.User?.FindFirst(LabClaimTypes.LocalUserId)?.Value;
        if (int.TryParse(userClaim, out var localUserId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(localUserId));

        await base.OnConnectedAsync();
    }

    /// <summary>Web Admin: start receiving updates for a room (subject to room scope, SRS FR-8.2).</summary>
    public async Task SubscribeToRoom(int roomId)
    {
        if (!await roomScope.CanAccessRoomAsync(roomId, Context.ConnectionAborted))
            throw new HubException("Room is outside your assigned scope.");

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));
    }

    public Task UnsubscribeFromRoom(int roomId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));

    /// <summary>Agent → Server: acknowledges receipt of a command before executing (SRS 8.4).</summary>
    public async Task AckCommand(long commandId)
    {
        var ct = Context.ConnectionAborted;
        if (!int.TryParse(Context.User?.FindFirst(LabClaimTypes.ComputerId)?.Value, out var computerId)
            || !int.TryParse(Context.User?.FindFirst(LabClaimTypes.RoomId)?.Value, out var roomId))
            return;

        var result = await db.CommandResults
            .FirstOrDefaultAsync(r => r.CommandId == commandId && r.ComputerId == computerId, ct);
        if (result is null || result.Status != CommandResultStatus.Sent)
            return; // unknown command, or already moved past Sent — ignore late/duplicate acks

        result.Status = CommandResultStatus.Acknowledged;
        await db.SaveChangesAsync(ct);

        await realtime.CommandResultReceivedAsync(
            new CommandResultEvent(commandId, computerId, roomId, result.Status.ToString(), null, null), ct);
    }

    // ---- Remote control signaling (ADR-0013) -------------------------------------------------
    // A live WebRTC session is a two-way, long-lived negotiation — a different shape from the
    // signed, one-shot CommandEnvelope pipeline, so it gets its own relay here instead. The server
    // never parses or signs the SDP/ICE payloads it forwards; authorization happens once, when the
    // session starts (room-scope check below), not per signaling message.

    /// <summary>Web Admin: start a remote-control session with a specific machine.</summary>
    public async Task<Guid> RequestRemoteControl(int computerId)
    {
        var ct = Context.ConnectionAborted;
        var computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == computerId, ct)
            ?? throw new HubException("Computer not found.");
        if (!await roomScope.CanAccessRoomAsync(computer.RoomId, ct))
            throw new HubException("Room is outside your assigned scope.");
        if (!computer.IsActive || !computer.IsApproved)
            throw new HubException("This machine is not active/approved.");
        if (remoteControl.HasActiveSession(computerId))
            throw new HubException("This machine already has an active remote-control session.");

        var userId = int.Parse(Context.User!.FindFirst(LabClaimTypes.LocalUserId)!.Value);
        var session = remoteControl.Start(computerId, computer.RoomId, Context.ConnectionId, userId);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "RemoteControl.Start",
            EntityType = nameof(Computer),
            EntityId = computerId.ToString(),
            Detail = JsonSerializer.Serialize(new { session.SessionId, computerId, roomId = computer.RoomId }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await Clients.Group(ComputerGroup(computerId))
            .SendAsync(RealtimeEvents.RemoteControlRequested, session.SessionId, ct);
        return session.SessionId;
    }

    /// <summary>Agent → Server → Admin: forwards the Agent's SDP offer (it owns the media source).</summary>
    public Task SubmitOffer(Guid sessionId, string sdp)
        => RelayFromAgentAsync(sessionId, RealtimeEvents.RemoteControlOffer, sdp);

    /// <summary>Web Admin → Server → Agent: forwards the browser's SDP answer.</summary>
    public Task SubmitAnswer(Guid sessionId, string sdp)
        => RelayFromAdminAsync(sessionId, RealtimeEvents.RemoteControlAnswer, sdp);

    /// <summary>Either side → Server → the other side: forwards one ICE candidate.</summary>
    public Task SubmitIceCandidate(Guid sessionId, string candidate, string? sdpMid, int? sdpMLineIndex)
    {
        var session = remoteControl.TryGet(sessionId)
            ?? throw new HubException("Unknown or ended remote-control session.");
        var payload = new { candidate, sdpMid, sdpMLineIndex };

        if (Context.ConnectionId == session.AdminConnectionId)
            return RelayFromAdminAsync(sessionId, RealtimeEvents.RemoteControlIceCandidate, payload);

        return RelayFromAgentAsync(sessionId, RealtimeEvents.RemoteControlIceCandidate, payload);
    }

    /// <summary>Either side: ends an active remote-control session.</summary>
    public async Task EndRemoteControl(Guid sessionId)
    {
        var session = remoteControl.End(sessionId);
        if (session is null)
            return;

        await NotifyOtherPartyEndedAsync(session);
        await WriteEndAuditLogAsync(session, reason: "ended");
    }

    // ---- Screen presentation (admin browser → every machine in a room) -----------------------
    // Start/Stop reach the Agents as signed commands (tracked per machine like any command); the
    // JPEG frames themselves are plain display data relayed to a per-session group the Agents join.

    /// <summary>Web Admin: start presenting their screen to a whole room. Returns the session id.</summary>
    public async Task<Guid> StartPresentation(int roomId, bool lockInput)
    {
        var ct = Context.ConnectionAborted;
        if (!await roomScope.CanAccessRoomAsync(roomId, ct))
            throw new HubException("Room is outside your assigned scope.");

        var userId = int.Parse(Context.User!.FindFirst(LabClaimTypes.LocalUserId)!.Value);
        var session = presentations.TryStart(roomId, Context.ConnectionId, userId, lockInput)
            ?? throw new HubException("This room already has an active presentation.");

        try
        {
            await mediator.Send(new IssueCommandCommand(
                null, roomId, null, CommandKind.StartPresentation, null, null, null, Confirm: true,
                PresentationSessionId: session.SessionId, LockInput: lockInput), ct);
        }
        catch
        {
            presentations.End(session.SessionId);
            throw;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "Presentation.Start",
            EntityType = nameof(Room),
            EntityId = roomId.ToString(),
            Detail = JsonSerializer.Serialize(new { session.SessionId, lockInput }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return session.SessionId;
    }

    /// <summary>Agent: joins the frame group of a presentation running in its own room.</summary>
    public async Task JoinPresentation(Guid sessionId)
    {
        var session = presentations.TryGet(sessionId)
            ?? throw new HubException("Unknown or ended presentation.");
        if (!int.TryParse(Context.User?.FindFirst(LabClaimTypes.RoomId)?.Value, out var roomId) || roomId != session.RoomId)
            throw new HubException("This machine is not in the presentation's room.");

        await Groups.AddToGroupAsync(Context.ConnectionId, PresentationGroup(sessionId));
    }

    /// <summary>Web Admin: pushes one JPEG frame of the shared screen to every joined machine.</summary>
    public Task SendPresentationFrame(Guid sessionId, byte[] jpeg)
    {
        var session = presentations.TryGet(sessionId)
            ?? throw new HubException("Unknown or ended presentation.");
        if (Context.ConnectionId != session.AdminConnectionId)
            throw new HubException("This connection did not start this presentation.");

        presentations.Touch(sessionId);
        return Clients.Group(PresentationGroup(sessionId))
            .SendAsync(RealtimeEvents.PresentationFrame, sessionId, jpeg, Context.ConnectionAborted);
    }

    /// <summary>Web Admin: ends the presentation and unlocks every machine.</summary>
    public async Task StopPresentation(Guid sessionId)
    {
        var session = presentations.TryGet(sessionId);
        if (session is null)
            return;
        if (Context.ConnectionId != session.AdminConnectionId)
            throw new HubException("This connection did not start this presentation.");

        presentations.End(sessionId);
        await Clients.Group(PresentationGroup(sessionId)).SendAsync(RealtimeEvents.PresentationEnded, sessionId);

        try
        {
            await mediator.Send(new IssueCommandCommand(
                null, session.RoomId, null, CommandKind.StopPresentation, null, null, null, Confirm: true,
                PresentationSessionId: sessionId));
        }
        catch (Exception)
        {
            // Best effort: the group message above and the Agent's own dead-man timer already unlock.
        }
        await WritePresentationEndAuditAsync(session, "stopped");
    }

    private async Task WritePresentationEndAuditAsync(PresentationSession session, string reason)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = session.AdminUserId,
            Action = "Presentation.End",
            EntityType = nameof(Room),
            EntityId = session.RoomId.ToString(),
            Detail = JsonSerializer.Serialize(new
            {
                session.SessionId,
                reason,
                durationSeconds = (DateTime.UtcNow - session.StartedAt).TotalSeconds,
            }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var presentation = presentations.EndByConnectionId(Context.ConnectionId);
        if (presentation is not null)
        {
            await Clients.Group(PresentationGroup(presentation.SessionId))
                .SendAsync(RealtimeEvents.PresentationEnded, presentation.SessionId);
            await WritePresentationEndAuditAsync(presentation, "admin disconnected");
        }

        var session = remoteControl.EndByConnectionId(Context.ConnectionId);
        if (session is not null)
        {
            await NotifyOtherPartyEndedAsync(session);
            await WriteEndAuditLogAsync(session, reason: "disconnected");
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task RelayFromAgentAsync(Guid sessionId, string method, object payload)
    {
        var session = remoteControl.TryGet(sessionId)
            ?? throw new HubException("Unknown or ended remote-control session.");

        // An agent may only relay on behalf of its own machine — never another one.
        if (!int.TryParse(Context.User?.FindFirst(LabClaimTypes.ComputerId)?.Value, out var callerComputerId)
            || callerComputerId != session.ComputerId)
            throw new HubException("This connection is not the target machine of this session.");

        remoteControl.SetAgentConnection(sessionId, Context.ConnectionId);
        return Clients.Client(session.AdminConnectionId).SendAsync(method, sessionId, payload, Context.ConnectionAborted);
    }

    private Task RelayFromAdminAsync(Guid sessionId, string method, object payload)
    {
        var session = remoteControl.TryGet(sessionId)
            ?? throw new HubException("Unknown or ended remote-control session.");
        if (Context.ConnectionId != session.AdminConnectionId)
            throw new HubException("This connection did not start this session.");

        return Clients.Group(ComputerGroup(session.ComputerId))
            .SendAsync(method, sessionId, payload, Context.ConnectionAborted);
    }

    private Task NotifyOtherPartyEndedAsync(RemoteControlSession session)
    {
        var isAdmin = Context.ConnectionId == session.AdminConnectionId;
        var target = isAdmin
            ? Clients.Group(ComputerGroup(session.ComputerId))
            : Clients.Client(session.AdminConnectionId);
        return target.SendAsync(RealtimeEvents.RemoteControlEnded, session.SessionId);
    }

    private async Task WriteEndAuditLogAsync(RemoteControlSession session, string reason)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = session.AdminUserId,
            Action = "RemoteControl.End",
            EntityType = nameof(Computer),
            EntityId = session.ComputerId.ToString(),
            Detail = JsonSerializer.Serialize(new
            {
                session.SessionId,
                reason,
                durationSeconds = (DateTime.UtcNow - session.StartedAt).TotalSeconds,
            }),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
