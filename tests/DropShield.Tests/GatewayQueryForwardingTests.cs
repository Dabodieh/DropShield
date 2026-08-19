using System.Net;
using DropShield.Tests.Support;

namespace DropShield.Tests;

public sealed class GatewayQueryForwardingTests
{
    [Fact]
    public async Task RequestPathBasePathAndQuery_AreForwardedAsOneRelativeTarget()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products?x=1&y=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/products?x=1&y=2", factory.Origin.LastForwardedPath);
    }

    [Fact]
    public async Task RequestWithoutQuery_RemainsUnchanged()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/products", factory.Origin.LastForwardedPath);
    }
}
