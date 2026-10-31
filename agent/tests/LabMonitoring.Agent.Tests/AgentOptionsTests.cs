using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using LabMonitoring.Agent.Configuration;

namespace LabMonitoring.Agent.Tests;

public sealed class AgentOptionsTests
{
    [Fact]
    public void Heartbeat_out_of_range_fails_validation()
    {
        var options = new AgentOptions { ServerBaseUrl = "https://x", HeartbeatSeconds = 1 };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true)
            .Should().BeFalse();
    }
}
