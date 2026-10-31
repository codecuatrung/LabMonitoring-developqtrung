using System.Reflection;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Monitoring;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Agent.Client;

/// <summary>Ensures the agent has valid credentials, performing first-time self-registration (SRS FR-5.3).</summary>
public sealed class AgentRegistrationService(
    IOptions<AgentOptions> options,
    CredentialStore credentials,
    MachineIdentityProvider machineIdentity,
    SystemMetricsCollector metrics,
    AgentApiClient api,
    ILogger<AgentRegistrationService> logger)
{
    private readonly AgentOptions _options = options.Value;

    public static string AgentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<AgentCredentials> EnsureRegisteredAsync(CancellationToken ct)
    {
        if (credentials.Load() is { } existing)
        {
            logger.LogInformation("Already registered as computer {ComputerId}.", existing.ComputerId);
            return existing;
        }

        if (string.IsNullOrWhiteSpace(_options.RoomCode))
            throw new InvalidOperationException("Agent is not registered and no RoomCode is configured (set Agent:RoomCode / Agent__RoomCode).");

        var facts = machineIdentity.Resolve();
        var hw = metrics.Inventory();

        var request = new AgentRegisterRequest
        {
            MachineId = facts.MachineId,
            RoomCode = _options.RoomCode!,
            Hostname = facts.Hostname,
            MacAddress = facts.MacAddress,
            AgentVersion = AgentVersion,
            CpuModel = hw.CpuModel,
            CpuCores = hw.CpuCores,
            RamTotalMB = hw.RamTotalMB,
            DiskTotalGB = hw.DiskTotalGB,
            OsVersion = hw.OsVersion,
        };

        try
        {
            var response = await api.RegisterAsync(request, ct);
            var creds = new AgentCredentials(response.ComputerId, response.ComputerName, response.AgentToken, response.AgentSecret);
            credentials.Save(creds);
            logger.LogInformation("Registration saved. Awaiting technician approval: {Pending}", !response.Approved);
            return creds;
        }
        catch (AgentRegistrationConflictException)
        {
            // The machine was registered before but we lost the local credentials — needs an admin
            // to reset the machine's token (M1). Surface a clear message and keep retrying later.
            logger.LogError("Machine {MachineId} is already registered but local credentials are missing. " +
                            "Ask an administrator to reset this machine's agent token.", facts.MachineId);
            throw;
        }
    }
}
