using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace LabMonitoring.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOrigins ?? [];

        services.AddCors(options => options.AddPolicy(CorsOptions.PolicyName, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials(); // SignalR needs credentialed CORS
            }
        }));

        return services;
    }

    public static IServiceCollection AddHealthChecksForLabMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();

        var sqlConnection = configuration.GetConnectionString("SqlServer");
        if (!string.IsNullOrWhiteSpace(sqlConnection))
        {
            builder.AddSqlServer(sqlConnection, name: "sql-server", tags: ["ready"]);
        }

        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            builder.AddRedis(redisConnection, name: "redis", tags: ["ready"]);
        }

        return services;
    }

    public static WebApplication MapLabMonitoringHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        return app;
    }
}
