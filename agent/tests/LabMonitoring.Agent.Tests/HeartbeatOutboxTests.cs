using FluentAssertions;
using LabMonitoring.Agent;
using LabMonitoring.Agent.Configuration;
using LabMonitoring.Agent.Outbox;
using LabMonitoring.Agent.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LabMonitoring.Agent.Tests;

public sealed class HeartbeatOutboxTests
{
    private static HeartbeatOutbox NewOutbox(int capacity)
    {
        // Isolate the on-disk file per test run so cases don't bleed into each other.
        var dir = Path.Combine(Path.GetTempPath(), "labagent-tests", Guid.NewGuid().ToString("N"));
        var paths = new AgentPaths(dir);

        var options = Options.Create(new AgentOptions { ServerBaseUrl = "http://x", OutboxCapacity = capacity });
        return new HeartbeatOutbox(options, paths, NullLogger<HeartbeatOutbox>.Instance);
    }

    [Fact]
    public void Drops_oldest_when_capacity_exceeded()
    {
        var outbox = NewOutbox(capacity: 3);

        for (var i = 0; i < 10; i++)
            outbox.Enqueue(new AgentHeartbeatRequest { CurrentWindowsUser = $"u{i}" });

        outbox.Count.Should().Be(3);
    }

    [Fact]
    public async Task Drain_stops_at_first_failure_and_keeps_the_rest()
    {
        var outbox = NewOutbox(capacity: 100);
        for (var i = 0; i < 5; i++)
            outbox.Enqueue(new AgentHeartbeatRequest { CurrentWindowsUser = $"u{i}" });

        var sent = 0;
        var delivered = await outbox.DrainAsync(_ =>
        {
            sent++;
            return Task.FromResult(sent < 3); // third send "fails"
        }, CancellationToken.None);

        delivered.Should().Be(2);
        outbox.Count.Should().Be(3);
    }
}
