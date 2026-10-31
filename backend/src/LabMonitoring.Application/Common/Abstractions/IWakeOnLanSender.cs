namespace LabMonitoring.Application.Common.Abstractions;

/// <summary>
/// Sends a Wake-on-LAN magic packet to power on a machine that is fully off (SRS FR-4 extension —
/// "khởi động máy đồng loạt"). Unlike the signed Lock/Restart/Shutdown/... commands, this never
/// reaches the Agent (it isn't running yet) — it's a raw UDP broadcast on the local network segment,
/// so it only works when the API host shares a broadcast domain with the target machines.
/// </summary>
public interface IWakeOnLanSender
{
    Task SendAsync(string macAddress, CancellationToken ct = default);
}
