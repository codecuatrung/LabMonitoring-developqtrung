using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Identity;

/// <summary>Stable per-machine identity used at registration.</summary>
public sealed record MachineFacts(Guid MachineId, string Hostname, string? MacAddress, string OsVersion);

public sealed class MachineIdentityProvider(AgentPaths paths, ILogger<MachineIdentityProvider> logger)
{
    public MachineFacts Resolve()
    {
        var machineId = ResolveMachineId();
        var hostname = Environment.MachineName;
        var mac = ResolvePrimaryMac();
        var os = Environment.OSVersion.VersionString;

        logger.LogInformation("Machine identity: {MachineId} host={Host} mac={Mac}", machineId, hostname, mac);
        return new MachineFacts(machineId, hostname, mac, os);
    }

    private Guid ResolveMachineId()
    {
        // 1. Stable OS identifier where available.
        if (OperatingSystem.IsWindows() && TryReadWindowsMachineGuid(out var winGuid))
            return winGuid;

        if (!OperatingSystem.IsWindows() && File.Exists("/etc/machine-id")
            && Guid.TryParse(File.ReadAllText("/etc/machine-id").Trim().Insert(8, "-").Insert(13, "-").Insert(18, "-").Insert(23, "-"), out var linuxGuid))
            return linuxGuid;

        // 2. Fall back to a value we generate once and persist.
        if (File.Exists(paths.MachineIdFile) && Guid.TryParse(File.ReadAllText(paths.MachineIdFile).Trim(), out var persisted))
            return persisted;

        var generated = Guid.NewGuid();
        File.WriteAllText(paths.MachineIdFile, generated.ToString());
        return generated;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryReadWindowsMachineGuid(out Guid guid)
    {
        guid = Guid.Empty;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var value = key?.GetValue("MachineGuid") as string;
            return Guid.TryParse(value, out guid);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolvePrimaryMac()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(n => n.GetPhysicalAddress().GetAddressBytes())
            .Where(b => b.Length == 6)
            .Select(b => string.Join(':', b.Select(x => x.ToString("X2"))))
            .FirstOrDefault();
    }
}
