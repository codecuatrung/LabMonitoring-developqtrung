using LabMonitoring.Api.Auth;
using LabMonitoring.Application.Common.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace LabMonitoring.Api.Realtime;

public static class RealtimeExtensions
{
    public const string HubPath = "/hubs/lab";

    public static IServiceCollection AddLabMonitoringRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        // ClientTimeoutInterval raised from the 30s default to match AgentHubClient's ServerTimeout —
        // under dev's `dotnet watch` load (or a congested lab network in production), a few seconds'
        // scheduling delay on either side otherwise trips a false "connection dead" verdict and drops
        // the agent out of its computer group right when a command might be delivered. KeepAliveInterval
        // stays at the SignalR-recommended ~1/3 of that so pings still arrive well within the window.
        var signalR = services.AddSignalR(o =>
        {
            o.EnableDetailedErrors = true;
            o.KeepAliveInterval = TimeSpan.FromSeconds(15);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            // Presentation frames (JPEG of the admin's screen) far exceed the 32 KB default.
            o.MaximumReceiveMessageSize = 2 * 1024 * 1024;
        });

        // Redis backplane — wired from MVP so scale-out is a config change (NFR "Khả năng mở rộng").
        // AbortOnConnectFail=false so a down/restarting Redis degrades realtime push instead of
        // taking the whole API down with it (StackExchange.Redis otherwise throws synchronously
        // on the first failed connect, which every notification-triggering request would hit).
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            signalR.AddStackExchangeRedis(redis, o =>
            {
                o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("labmonitoring");
                o.Configuration.AbortOnConnectFail = false;
            });
        }

        services.AddSingleton<IRealtimeNotifier, LabHubNotifier>();
        services.AddHostedService<PresentationWatchdogService>();

        // Let SignalR read the bearer token from the query string on the hub path (WebSockets can't set headers).
        services.PostConfigure<JwtBearerOptions>(AuthSchemes.Jwt, options =>
        {
            var existing = options.Events?.OnMessageReceived;
            options.Events ??= new JwtBearerEvents();
            options.Events.OnMessageReceived = async context =>
            {
                if (existing is not null)
                    await existing(context);

                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments(HubPath))
                    context.Token = accessToken;
            };
        });

        return services;
    }
}
