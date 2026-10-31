using System.Text.Json.Serialization;
using LabMonitoring.Api.Auth;
using LabMonitoring.Api.Infrastructure;
using LabMonitoring.Api.Realtime;
using LabMonitoring.Application;
using LabMonitoring.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog: read from configuration, enrich, write to console + Seq ---
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Options ---
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

// --- Application / Infrastructure layers ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Web ---
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddCorsPolicy(builder.Configuration);
builder.Services.AddHealthChecksForLabMonitoring(builder.Configuration);

builder.Services.AddLabMonitoringAuth(builder.Configuration);
builder.Services.AddLabMonitoringRealtime(builder.Configuration);

var app = builder.Build();

// `dotnet run -- seed [--dev-data]`: apply migrations (and optionally sample data), then exit.
if (args.Contains("seed", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<LabMonitoring.Infrastructure.Persistence.DatabaseSeeder>();
    await seeder.MigrateAsync();
    if (args.Contains("--dev-data", StringComparer.OrdinalIgnoreCase))
        await seeder.SeedDevDataAsync();
    return;
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Lab Monitoring API"));
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsOptions.PolicyName);

app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapLabMonitoringHealthChecks();
app.MapHub<LabHub>(RealtimeExtensions.HubPath);

app.Run();

/// <summary>Exposed so the integration test host (WebApplicationFactory) can reference the entry point.</summary>
public partial class Program;
