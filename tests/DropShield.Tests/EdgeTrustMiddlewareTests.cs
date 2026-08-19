using System.Net;
using DropShield.Tests.Support;

namespace DropShield.Tests;

/// <summary>
/// EdgeTrustMiddleware is DropShield.Api's independent half of the Fastly reference adapter's
/// trust boundary (see integrations/fastly): the edge is expected to strip/overwrite a client's
/// own value and inject the real one, but this middleware must reject a missing or forged key
/// even if DropShield.Api is reached directly, bypassing the edge entirely.
/// </summary>
public sealed class EdgeTrustMiddlewareTests
{
    private const string EdgeKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public async Task Disabled_AllowsRequestsWithoutAnEdgeKey()
    {
        using var factory = new DropShieldApiFactory(EdgeTrustDisabled());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_RejectsRequestsMissingTheEdgeKey()
    {
        using var factory = new DropShieldApiFactory(EdgeTrustEnabled());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_RejectsAForgedEdgeKey()
    {
        using var factory = new DropShieldApiFactory(EdgeTrustEnabled());
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-DropShield-Edge-Key", "attacker-supplied-value");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_AllowsTheCorrectEdgeKey()
    {
        using var factory = new DropShieldApiFactory(EdgeTrustEnabled());
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-DropShield-Edge-Key", EdgeKey);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Dictionary<string, string?> EdgeTrustDisabled() => new()
    {
        ["DropShield:EdgeTrust:Enabled"] = "false",
    };

    private static Dictionary<string, string?> EdgeTrustEnabled() => new()
    {
        ["DropShield:EdgeTrust:Enabled"] = "true",
        ["DropShield:EdgeTrust:HeaderName"] = "X-DropShield-Edge-Key",
        ["DropShield:EdgeTrust:SharedKey"] = EdgeKey,
    };
}
