using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabMonitoring.IntegrationTests;

public sealed class MetaEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Meta_endpoint_returns_service_info()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/meta");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MetaResponse>();
        body!.Service.Should().Be("LabMonitoring.Api");
    }

    private sealed record MetaResponse(string Service, string? Version, string EnvironmentName);
}
