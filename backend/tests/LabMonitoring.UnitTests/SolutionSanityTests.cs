using FluentAssertions;

namespace LabMonitoring.UnitTests;

/// <summary>Placeholder that keeps the unit-test project wired until domain tests land in F1.</summary>
public sealed class SolutionSanityTests
{
    [Fact]
    public void Application_assembly_is_referenced()
    {
        typeof(LabMonitoring.Application.DependencyInjection).Assembly
            .GetName().Name.Should().Be("LabMonitoring.Application");
    }
}
