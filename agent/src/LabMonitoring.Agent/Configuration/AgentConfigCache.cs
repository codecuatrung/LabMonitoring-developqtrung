using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Configuration;

/// <summary>
/// Latest <c>/agent/config</c> response, refreshed periodically by <see cref="AgentWorker"/>. Read
/// by the command executor for the client-side whitelist re-check (SRS 8.5: "Open Application /
/// Open URL chỉ thực thi nếu nằm trong whitelist đã được đồng bộ qua GET /agent/config") — the
/// server also enforces the whitelist at issue time, so this is defense in depth against a stale
/// or tampered envelope, not the only check.
/// </summary>
public sealed class AgentConfigCache
{
    private volatile AgentConfigResponse? _current;

    public AgentConfigResponse? Current => _current;

    public void Update(AgentConfigResponse config) => _current = config;
}
