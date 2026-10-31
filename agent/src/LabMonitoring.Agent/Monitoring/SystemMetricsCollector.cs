using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace LabMonitoring.Agent.Monitoring;

public sealed record SystemMetrics(
    decimal? CpuUsagePercent,
    decimal? RamUsagePercent,
    decimal? DiskUsagePercent,
    decimal? TemperatureC);

public sealed record HardwareInventory(
    string? CpuModel,
    int? CpuCores,
    int? RamTotalMB,
    int? DiskTotalGB,
    string OsVersion);

/// <summary>
/// Best-effort resource sampling (SRS FR-1.2/1.3). CPU% is Windows-only for now (LibreHardwareMonitor
/// and a Linux /proc reader come with the monitoring module); temperature is not read yet.
/// </summary>
public sealed partial class SystemMetricsCollector(ILogger<SystemMetricsCollector> logger) : IDisposable
{
    private readonly object _cpuLock = new();
    private (DateTime at, TimeSpan cpu)? _lastCpuSample;

    public SystemMetrics Collect()
    {
        return new SystemMetrics(
            CpuUsagePercent: SampleCpu(),
            RamUsagePercent: SampleRam(),
            DiskUsagePercent: SampleSystemDisk(),
            TemperatureC: null);
    }

    public HardwareInventory Inventory()
    {
        var drive = SystemDrive();
        return new HardwareInventory(
            CpuModel: OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") : null,
            CpuCores: Environment.ProcessorCount,
            RamTotalMB: TotalRamBytes() is { } total ? (int)(total / 1024 / 1024) : null,
            DiskTotalGB: drive is { } d ? (int)(d.TotalSize / 1024 / 1024 / 1024) : null,
            OsVersion: RuntimeInformation.OSDescription);
    }

    private decimal? SampleCpu()
    {
        // Delta of total processor time across all cores between two calls.
        try
        {
            var cpuNow = TimeSpan.Zero;
            foreach (var p in Process.GetProcesses())
            {
                try { cpuNow += p.TotalProcessorTime; } catch { /* access denied / exited */ }
                finally { p.Dispose(); }
            }

            var now = DateTime.UtcNow;
            lock (_cpuLock)
            {
                if (_lastCpuSample is not { } prev)
                {
                    _lastCpuSample = (now, cpuNow);
                    return null; // need two samples
                }

                var wall = (now - prev.at).TotalMilliseconds * Environment.ProcessorCount;
                var used = (cpuNow - prev.cpu).TotalMilliseconds;
                _lastCpuSample = (now, cpuNow);

                if (wall <= 0) return null;
                return Math.Clamp((decimal)(used / wall * 100), 0, 100);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "CPU sample failed");
            return null;
        }
    }

    private decimal? SampleRam()
    {
        var total = TotalRamBytes();
        var available = AvailableRamBytes();
        if (total is not { } t || available is not { } a || t == 0) return null;
        return Math.Clamp((decimal)((t - a) / (double)t * 100), 0, 100);
    }

    private static decimal? SampleSystemDisk()
    {
        var d = SystemDrive();
        if (d is null || d.TotalSize == 0) return null;
        return Math.Clamp((decimal)((d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize * 100), 0, 100);
    }

    private static DriveInfo? SystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "/";
        try
        {
            var d = new DriveInfo(root);
            return d.IsReady ? d : null;
        }
        catch { return null; }
    }

    private ulong? TotalRamBytes()
    {
        if (OperatingSystem.IsWindows() && TryWindowsMemory(out var status))
            return status.ullTotalPhys;
        if (!OperatingSystem.IsWindows())
            return ReadProcMeminfo("MemTotal");
        return null;
    }

    private ulong? AvailableRamBytes()
    {
        if (OperatingSystem.IsWindows() && TryWindowsMemory(out var status))
            return status.ullAvailPhys;
        if (!OperatingSystem.IsWindows())
            return ReadProcMeminfo("MemAvailable");
        return null;
    }

    private static ulong? ReadProcMeminfo(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                var kb = ulong.Parse(new string(line.Where(char.IsDigit).ToArray()));
                return kb * 1024;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private bool TryWindowsMemory(out MEMORYSTATUSEX status)
    {
        status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        try { return GlobalMemoryStatusEx(ref status); }
        catch (Exception ex) { logger.LogDebug(ex, "GlobalMemoryStatusEx failed"); return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public void Dispose() { }
}
