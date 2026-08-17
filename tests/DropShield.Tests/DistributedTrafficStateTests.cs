using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Options;
using DropShield.Api.State;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class DistributedTrafficStateTests
{
    [Fact]
    public async Task AggregateStockLimit_IsSharedAcrossGatewayInstances()
    {
        var sharedState = new FakeDistributedTrafficState();
        var settings = RedisSettings();
        settings["DropShield:Policies:Stock:ClientPermitLimit"] = "10";
        settings["DropShield:Policies:Stock:AggregatePermitLimit"] = "2";
        using var factoryA = new DropShieldApiFactory(settings, distributedState: sharedState);
        using var factoryB = new DropShieldApiFactory(settings, distributedState: sharedState);
        using var clientA = factoryA.CreateClient();
        using var clientB = factoryB.CreateClient();

        var first = await SendStockAsync(clientA, "aggregate-a");
        var second = await SendStockAsync(clientB, "aggregate-b");
        var rejected = await SendStockAsync(clientA, "aggregate-c");
        var body = await rejected.Content.ReadFromJsonAsync<GatewayError>();
        var metrics = await clientA.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("rate_limited", body.Error);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(2, factoryA.Origin.TotalRequests + factoryB.Origin.TotalRequests);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.RateLimitReasons.Aggregate);
    }

    [Fact]
    public async Task PerClientStockLimit_IsSharedAndClientsRemainIsolated()
    {
        var sharedState = new FakeDistributedTrafficState();
        var settings = RedisSettings();
        settings["DropShield:Policies:Stock:ClientPermitLimit"] = "2";
        settings["DropShield:Policies:Stock:AggregatePermitLimit"] = "100";
        using var factoryA = new DropShieldApiFactory(settings, distributedState: sharedState);
        using var factoryB = new DropShieldApiFactory(settings, distributedState: sharedState);
        using var clientA = factoryA.CreateClient();
        using var clientB = factoryB.CreateClient();

        var first = await SendStockAsync(clientA, "shared-client");
        var second = await SendStockAsync(clientB, "shared-client");
        var rejected = await SendStockAsync(clientA, "shared-client");
        var independent = await SendStockAsync(clientB, "independent-client");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        Assert.Equal(3, factoryA.Origin.TotalRequests + factoryB.Origin.TotalRequests);
    }

    [Fact]
    public async Task RedisUnavailable_FailsClosedAndIsObservable()
    {
        var sharedState = new FakeDistributedTrafficState { IsAvailable = false };
        var settings = RedisSettings();
        using var factory = new DropShieldApiFactory(settings, distributedState: sharedState);
        using var client = factory.CreateClient();

        var response = await SendStockAsync(client, "state-failure-client");
        var error = await response.Content.ReadFromJsonAsync<GatewayError>();
        var health = await client.GetAsync("/health");
        var healthBody = await health.Content.ReadFromJsonAsync<HealthResponse>();
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("state_unavailable", error.Error);
        Assert.Equal(0, factory.Origin.TotalRequests);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.NotNull(healthBody);
        Assert.Equal("unhealthy", healthBody.Status);
        Assert.Equal("Redis", healthBody.StateProvider);
        Assert.Equal("unavailable", healthBody.State);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.Traffic.StateFailures);
        Assert.Equal(0, metrics.Traffic.RateLimited);
        Assert.Equal(1, metrics.StatusCodes.ServerError5xx);
    }

    [Fact]
    public async Task RedisHealthy_IsReportedWithoutExpandingHealthPayload()
    {
        var settings = RedisSettings();
        using var factory = new DropShieldApiFactory(
            settings,
            distributedState: new FakeDistributedTrafficState());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("healthy", body.Status);
        Assert.Equal("Redis", body.StateProvider);
        Assert.Equal("available", body.State);
    }

    [Fact]
    public void RedisKeys_DoNotExposeRawClientIdentity()
    {
        const string rawIdentity = "test:private-client-127.0.0.1";
        var options = Options.Create(new DropShieldOptions
        {
            Redis = new RedisStateOptions
            {
                KeyPrefix = "dropshield:test",
                IdentityHashKey = new string('h', 32),
            },
        });
        var builder = new RedisTrafficKeyBuilder(options);
        var request = new DistributedTrafficRequest(
            TrafficPolicyKind.Stock,
            TrafficLimitScope.PerClient,
            rawIdentity,
            5,
            TimeSpan.FromSeconds(1));

        var key = builder.Build(request);
        var anotherKey = builder.Build(request with { ClientPartition = "test:another-client" });

        Assert.StartsWith("dropshield:test:rate:stock:client:", key, StringComparison.Ordinal);
        Assert.DoesNotContain(rawIdentity, key, StringComparison.Ordinal);
        Assert.NotEqual(key, anotherKey);
        Assert.Equal(key, builder.Build(request));
    }

    private static Dictionary<string, string?> RedisSettings() => new()
    {
        ["DropShield:StateProvider"] = "Redis",
        ["DropShield:Redis:ConnectionString"] = "127.0.0.1:6379",
        ["DropShield:Redis:KeyPrefix"] = "dropshield:test",
        ["DropShield:Redis:IdentityHashKey"] = "test-only-identity-hash-key-0001",
    };

    private static async Task<HttpResponseMessage> SendStockAsync(
        HttpClient client,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/products/pokemon-etb/stock");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        return await client.SendAsync(request);
    }

    private sealed record GatewayError(string Error, string Message);

    private sealed record HealthResponse(
        string Status,
        string Service,
        string StateProvider,
        string State);
}
