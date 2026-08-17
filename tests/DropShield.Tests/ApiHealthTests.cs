using System.Net;
using System.Net.Http.Json;
using DropShield.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DropShield.Tests;

public sealed class ApiHealthTests : IClassFixture<WebApplicationFactory<ApiAssemblyMarker>>
{
    private readonly HttpClient _client;

    public ApiHealthTests(WebApplicationFactory<ApiAssemblyMarker> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthyResponse()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("healthy", body.Status);
        Assert.Equal("DropShield.Api", body.Service);
        Assert.Equal("InMemory", body.StateProvider);
        Assert.Equal("available", body.State);
    }

    private sealed record HealthResponse(
        string Status,
        string Service,
        string StateProvider,
        string State);
}
