using System.Net;
using System.Net.Http.Json;
using DropShield.Tests.Support;

namespace DropShield.Tests;

public sealed class DropShieldGatewayTests
{
    [Fact]
    public async Task ProductRequests_AreForwarded()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var listResponse = await client.GetAsync("/api/products");
        var detailResponse = await client.GetAsync("/api/products/pokemon-etb");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/products"));
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/products/pokemon-etb"));
    }

    [Fact]
    public async Task StockRequest_IsForwarded()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, StockPath, "stock-client");
        var body = await response.Content.ReadFromJsonAsync<StockResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(500, body.Available);
        Assert.Equal(1, factory.Origin.GetRequestCount(StockPath));
    }

    [Theory]
    [InlineData("/api/cart")]
    [InlineData("/api/checkout")]
    public async Task TransactionRequest_IsForwarded(string path)
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Post, path, $"client-{path}");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, factory.Origin.GetRequestCount(path));
    }

    [Fact]
    public async Task NormalSyntheticClient_RemainsUnderConfiguredLimits()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var responses = new[]
        {
            await SendAsync(client, HttpMethod.Get, "/api/products", "normal-1"),
            await SendAsync(client, HttpMethod.Get, "/api/products/pokemon-etb", "normal-1"),
            await SendAsync(client, HttpMethod.Get, StockPath, "normal-1"),
            await SendAsync(client, HttpMethod.Post, "/api/cart", "normal-1"),
            await SendAsync(client, HttpMethod.Post, "/api/checkout", "normal-1"),
        };

        Assert.All(responses, response => Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode));
        Assert.Equal(5, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task RepeatedAggressiveClient_EventuallyReceives429()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        var first = await SendAsync(client, HttpMethod.Get, StockPath, "aggressive-1");
        var second = await SendAsync(client, HttpMethod.Get, StockPath, "aggressive-1");
        var rejected = await SendAsync(client, HttpMethod.Get, StockPath, "aggressive-1");
        var body = await rejected.Content.ReadFromJsonAsync<RateLimitResponse>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("rate_limited", body.Error);
        Assert.True(rejected.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task OneSyntheticClient_DoesNotConsumeAnotherClientsAllowance()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        await SendAsync(client, HttpMethod.Get, StockPath, "client-a");
        await SendAsync(client, HttpMethod.Get, StockPath, "client-a");
        var rejectedA = await SendAsync(client, HttpMethod.Get, StockPath, "client-a");
        var allowedB = await SendAsync(client, HttpMethod.Get, StockPath, "client-b");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedB.StatusCode);
    }

    [Fact]
    public async Task AggregateStockLimit_RejectsIndependentClients()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:Policies:Stock:ClientPermitLimit"] = "10",
            ["DropShield:Policies:Stock:AggregatePermitLimit"] = "2",
        };
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        var first = await SendAsync(client, HttpMethod.Get, StockPath, "aggregate-a");
        var second = await SendAsync(client, HttpMethod.Get, StockPath, "aggregate-b");
        var rejected = await SendAsync(client, HttpMethod.Get, StockPath, "aggregate-c");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ExhaustedAggregateStockLimit_DoesNotLimitProductBrowsing()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:Policies:Stock:ClientPermitLimit"] = "10",
            ["DropShield:Policies:Stock:AggregatePermitLimit"] = "1",
        };
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        var stock = await SendAsync(client, HttpMethod.Get, StockPath, "browse-a");
        var rejectedStock = await SendAsync(client, HttpMethod.Get, StockPath, "browse-b");
        var product = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/products/pokemon-etb",
            "browse-c");

        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedStock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, product.StatusCode);
    }

    [Fact]
    public async Task StockForUnprotectedProduct_IsNotRateLimited()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:Policies:Stock:ClientPermitLimit"] = "1",
            ["DropShield:Policies:Stock:AggregatePermitLimit"] = "1",
        };
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await SendAsync(
                client,
                HttpMethod.Get,
                "/api/products/ordinary-product/stock",
                "ordinary-client");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(3, factory.Origin.GetRequestCount("/api/products/ordinary-product/stock"));
    }

    [Fact]
    public async Task RejectedRequest_IsNotForwardedAndIsCounted()
    {
        using var factory = new DropShieldApiFactory();
        using var client = factory.CreateClient();

        await SendAsync(client, HttpMethod.Get, StockPath, "counted-client");
        await SendAsync(client, HttpMethod.Get, StockPath, "counted-client");
        await SendAsync(client, HttpMethod.Get, StockPath, "counted-client");

        var metrics = await client.GetFromJsonAsync<MetricsResponse>("/internal/metrics");

        Assert.Equal(2, factory.Origin.GetRequestCount(StockPath));
        Assert.NotNull(metrics);
        Assert.Equal(3, metrics.Routes["stock"].Incoming);
        Assert.Equal(2, metrics.Routes["stock"].Forwarded);
        Assert.Equal(1, metrics.Routes["stock"].RateLimited);
    }

    [Fact]
    public async Task SyntheticIdentityHeader_IsIgnoredWhenNotExplicitlyEnabled()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:SyntheticClientIdentity:Enabled"] = "false",
            ["DropShield:Policies:Stock:ClientPermitLimit"] = "1",
        };
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        var first = await SendAsync(client, HttpMethod.Get, StockPath, "claimed-a");
        var second = await SendAsync(client, HttpMethod.Get, StockPath, "claimed-b");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task OriginFailure_Returns502RatherThan429()
    {
        using var factory = new DropShieldApiFactory();
        factory.Origin.ThrowOnSend = true;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products");
        var body = await response.Content.ReadFromJsonAsync<GatewayErrorResponse>();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("upstream_unavailable", body.Error);
    }

    [Fact]
    public async Task InternalMetrics_AreUnavailableInProduction()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:SyntheticClientIdentity:Enabled"] = "false",
            ["DropShield:InternalMetrics:Enabled"] = "false",
        };
        using var factory = new DropShieldApiFactory(settings, "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/metrics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string syntheticClientId)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-DropShield-Test-Client", syntheticClientId);
        return await client.SendAsync(request);
    }

    private const string StockPath = "/api/products/pokemon-etb/stock";

    private sealed record StockResponse(string ProductId, int Available);

    private sealed record RateLimitResponse(string Error, string Message);

    private sealed record GatewayErrorResponse(string Error, string Message);

    private sealed record MetricsResponse(
        TrafficCounts Total,
        Dictionary<string, TrafficCounts> Routes);

    private sealed record TrafficCounts(long Incoming, long Forwarded, long RateLimited);
}
