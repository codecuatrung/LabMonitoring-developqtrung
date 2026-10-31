using System.Net;
using System.Net.Sockets;
using LabMonitoring.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Infrastructure.Network;

/// <summary>
/// Builds and broadcasts a standard Wake-on-LAN magic packet (6x 0xFF followed by the target MAC
/// repeated 16 times) to UDP port 9 on the local subnet's broadcast address. The target machine's
/// NIC/BIOS must already have Wake-on-LAN enabled — that's a one-time hardware setting outside this
/// app's control.
/// </summary>
internal sealed class WakeOnLanSender(ILogger<WakeOnLanSender> logger) : IWakeOnLanSender
{
    private const int WakeOnLanPort = 9;

    public async Task SendAsync(string macAddress, CancellationToken ct = default)
    {
        var macBytes = ParseMac(macAddress);

        var packet = new byte[102];
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (var i = 1; i <= 16; i++)
            macBytes.CopyTo(packet, i * 6);

        using var client = new UdpClient { EnableBroadcast = true };
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, WakeOnLanPort), ct);

        logger.LogInformation("Wake-on-LAN packet sent for MAC {MacAddress}", macAddress);
    }

    private static byte[] ParseMac(string macAddress)
    {
        var hex = macAddress.Replace(":", "").Replace("-", "");
        if (hex.Length != 12)
            throw new FormatException($"'{macAddress}' is not a valid MAC address.");
        return Convert.FromHexString(hex);
    }
}
