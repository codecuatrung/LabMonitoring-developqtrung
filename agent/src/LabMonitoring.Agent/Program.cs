using LabMonitoring.Agent;
using LabMonitoring.Agent.Client;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Identity;
using LabMonitoring.Agent.Monitoring;
using LabMonitoring.Agent.Outbox;
using LabMonitoring.Agent.RemoteControl;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows Service when installed via sc.exe; falls back to console for local runs.
builder.Services.AddWindowsService(options => options.ServiceName = "LabMonitoringAgent");

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<AgentPaths>();
builder.Services.AddSingleton<CredentialStore>();
builder.Services.AddSingleton<MachineIdentityProvider>();
builder.Services.AddSingleton<SystemMetricsCollector>();
builder.Services.AddSingleton<SessionInfoProvider>();
builder.Services.AddSingleton<HeartbeatOutbox>();
builder.Services.AddSingleton<AgentConfigCache>();
builder.Services.AddSingleton<CommandReceiver>();
builder.Services.AddSingleton<AgentUpdateExecutor>();
builder.Services.AddSingleton<FileTransferExecutor>();
builder.Services.AddSingleton<ICommandExecutor>(sp => OperatingSystem.IsWindows()
    ? new WindowsCommandExecutor(
        sp.GetRequiredService<AgentConfigCache>(), sp.GetRequiredService<AgentUpdateExecutor>(), sp.GetRequiredService<FileTransferExecutor>(), sp.GetRequiredService<SessionInfoProvider>(),
        () => sp.GetRequiredService<PresentationCoordinator>(), sp.GetRequiredService<ILogger<WindowsCommandExecutor>>())
    : new UnsupportedPlatformCommandExecutor());
builder.Services.AddSingleton<AgentHubClient>();
builder.Services.AddSingleton<AgentRegistrationService>();
builder.Services.AddSingleton<RemoteControlCoordinator>();
builder.Services.AddSingleton<PresentationCoordinator>();

builder.Services.AddHttpClient<AgentApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl.TrimEnd('/') + "/");
    // 120s (not 30s) — DownloadUpdatePackageAsync (SRS H1 "auto-update") can transfer a sizeable zip;
    // every other call here is tiny and fails fast on its own regardless of this ceiling.
    client.Timeout = TimeSpan.FromSeconds(120);
});

// File-transfer bodies (500 MB downloads, zip uploads) get their own client with no overall timeout —
// the 120s above would abort a legitimate transfer over a slow link. The caller's token governs cancellation.
builder.Services.AddHttpClient<TransferApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl.TrimEnd('/') + "/");
    client.Timeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();

// Force the coordinator's construction now (nothing else resolves it) so its constructor's event
// subscriptions are wired onto AgentHubClient before the hub connects (ADR-0013).
_ = host.Services.GetRequiredService<RemoteControlCoordinator>();
_ = host.Services.GetRequiredService<PresentationCoordinator>();

host.Run();
