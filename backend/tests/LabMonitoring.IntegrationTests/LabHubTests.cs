using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabMonitoring.IntegrationTests;

public sealed class LabHubTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Hub_negotiate_requires_authentication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/hubs/lab/negotiate?negotiateVersion=1", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
