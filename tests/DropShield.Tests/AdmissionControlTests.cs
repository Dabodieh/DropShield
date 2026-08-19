using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Admission;
using DropShield.Api.Models;
using DropShield.Api.State;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DropShield.Tests;

public sealed class AdmissionControlTests
{
    [Fact]
    public async Task ExcessEligibleSession_WaitsWithoutOriginForwardOrQueuePosition()
    {
        using var factory = new DropShieldApiFactory(AdmissionSettings());
        using var client = CreateClientWithoutCookies(factory);

        var admitted = await SendStockAsync(client, "client-a", Session('a'));
        var waiting = await SendStockAsync(client, "client-b", Session('b'));
        var waitingJson = await waiting.Content.ReadAsStringAsync();
        var body = await waiting.Content.ReadFromJsonAsync<WaitingRoomResponse>();
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, waiting.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("waiting", body.Status);
        Assert.Equal("pokemon-etb", body.Drop);
        Assert.Equal(1, body.RetryAfterSeconds);
        Assert.True(waiting.Headers.TryGetValues("Retry-After", out var retryValues));
        Assert.Equal("1", Assert.Single(retryValues));
        Assert.DoesNotContain("position", waitingJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.Origin.TotalRequests);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.Admission.Admitted);
        Assert.Equal(1, metrics.Admission.Waiting);
        Assert.Equal(50, metrics.Traffic.OriginTrafficReductionPercentage);
    }

    [Fact]
    public async Task SharedState_ProgressivelyAdmitsAcrossGatewayInstances()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var sharedState = new InMemoryAdmissionState(clock);
        var settings = AdmissionSettings();
        settings["DropShield:Admission:SessionTtlSeconds"] = "1";
        using var factoryA = new DropShieldApiFactory(settings, admissionState: sharedState);
        using var factoryB = new DropShieldApiFactory(settings, admissionState: sharedState);
        using var clientA = CreateClientWithoutCookies(factoryA);
        using var clientB = CreateClientWithoutCookies(factoryB);

        var first = await SendStockAsync(clientA, "client-a", Session('a'));
        var waiting = await SendStockAsync(clientB, "client-b", Session('b'));

        clock.Advance(TimeSpan.FromSeconds(2));

        var promoted = await SendStockAsync(clientA, "client-b", Session('b'));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(2, factoryA.Origin.TotalRequests + factoryB.Origin.TotalRequests);
    }

    [Fact]
    public async Task AggressiveWaitingPolls_AreStillRateLimitedBeforeAdmission()
    {
        var settings = AdmissionSettings();
        settings["DropShield:Policies:Stock:ClientPermitLimit"] = "2";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClientWithoutCookies(factory);

        await SendStockAsync(client, "capacity-owner", Session('a'));
        var firstPoll = await SendStockAsync(client, "poller", Session('b'));
        var secondPoll = await SendStockAsync(client, "poller", Session('b'));
        var rejected = await SendStockAsync(client, "poller", Session('b'));

        Assert.Equal(HttpStatusCode.Accepted, firstPoll.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondPoll.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task AdmissionStateFailure_FailsClosedWithoutForwarding()
    {
        var settings = AdmissionSettings();
        settings["DropShield:StateProvider"] = "Redis";
        settings["DropShield:Redis:IdentityHashKey"] = "test-only-identity-hash-key-0001";
        using var factory = new DropShieldApiFactory(
            settings,
            distributedState: new FakeDistributedTrafficState(),
            admissionState: new UnavailableAdmissionState());
        using var client = CreateClientWithoutCookies(factory);

        var response = await SendStockAsync(client, "client-a", Session('a'));
        var error = await response.Content.ReadFromJsonAsync<GatewayErrorResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("state_unavailable", error.Error);
        Assert.Equal(0, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task BoundedWaitingRoom_RejectsOverflowWithoutForwarding()
    {
        var settings = AdmissionSettings();
        settings["DropShield:Admission:MaximumWaitingSessions"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClientWithoutCookies(factory);

        await SendStockAsync(client, "client-a", Session('a'));
        var waiting = await SendStockAsync(client, "client-b", Session('b'));
        var overflow = await SendStockAsync(client, "client-c", Session('c'));
        var error = await overflow.Content.ReadFromJsonAsync<GatewayErrorResponse>();

        Assert.Equal(HttpStatusCode.Accepted, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("waiting_room_full", error.Error);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    private static Dictionary<string, string?> AdmissionSettings() => new()
    {
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:DropId"] = "pokemon-etb",
        ["DropShield:Admission:MaximumActiveSessions"] = "1",
        ["DropShield:Admission:AdmissionBatchSize"] = "1",
        ["DropShield:Admission:MaximumWaitingSessions"] = "10",
        ["DropShield:Admission:SessionTtlSeconds"] = "30",
        ["DropShield:Admission:WaitingTtlSeconds"] = "60",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Stock:AggregatePermitLimit"] = "1",
    };

    private static string Session(char value) => new(value, 64);

    private static HttpClient CreateClientWithoutCookies(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> SendStockAsync(
        HttpClient client,
        string clientIdentity,
        string session)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/products/pokemon-etb/stock");
        request.Headers.Add("X-DropShield-Test-Client", clientIdentity);
        request.Headers.Add("Cookie", $"{AdmissionSessionProvider.CookieName}={session}");
        return await client.SendAsync(request);
    }

    private sealed class UnavailableAdmissionState : IAdmissionState
    {
        public ValueTask<AdmissionDecision> EvaluateAsync(
            AdmissionRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AdmissionDecision>(
                new DistributedTrafficStateUnavailableException(
                    "Unavailable for test.",
                    new TimeoutException()));
    }
}
