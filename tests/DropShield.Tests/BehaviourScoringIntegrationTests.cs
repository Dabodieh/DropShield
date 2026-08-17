using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Behaviour;
using DropShield.Api.Models;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DropShield.Tests;

public sealed class BehaviourScoringIntegrationTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string Session = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task NormalAdmittedCustomerRemainsAllowed()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "normal", Session);
        var actionProof = await GetActionProofAsync(client, "normal", Session, admission, "cart");

        var cart = await SendMutationAsync(client, "normal", Session, admission, actionProof);

        Assert.Equal(HttpStatusCode.Accepted, cart.StatusCode);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
    }

    [Fact]
    public async Task HighRecentEvidenceTemporarilyRestrictsTransactionWithoutForwarding()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "suspicious", Session);

        for (var index = 0; index < 8; index++)
        {
            using var stockRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/products/pokemon-etb/stock");
            stockRequest.Headers.Add("X-DropShield-Test-Client", "suspicious");
            stockRequest.Headers.Add(
                "Cookie",
                $"DropShield.Session={Session}; DropShield.Admission={admission}");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(stockRequest)).StatusCode);
        }

        for (var index = 0; index < 3; index++)
        {
            var invalid = await SendMutationAsync(
                client,
                "suspicious",
                Session,
                admission,
                $"invalid-proof-{index}");
            Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        }

        var cartProof = await GetActionProofAsync(client, "suspicious", Session, admission, "cart");
        var firstCart = await SendMutationAsync(client, "suspicious", Session, admission, cartProof);
        var firstReplay = await SendMutationAsync(client, "suspicious", Session, admission, cartProof);
        var secondReplay = await SendMutationAsync(client, "suspicious", Session, admission, cartProof);
        var restricted = await SendMutationAsync(client, "suspicious", Session, admission, cartProof);
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.Accepted, firstCart.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, firstReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondReplay.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, restricted.StatusCode);
        Assert.Equal(
            "behaviour_restricted",
            (await restricted.Content.ReadFromJsonAsync<GatewayErrorResponse>())!.Error);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
        Assert.True(metrics!.Behaviour.HighScores > 0);
        Assert.Equal(1, metrics.Behaviour.Restrictions);
    }

    [Fact]
    public async Task BehaviourStateFailureAllowsTransactionAndRecordsOnlyAggregateFailure()
    {
        using var factory = new DropShieldApiFactory(
            Settings(),
            behaviourState: new UnavailableBehaviourState());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "unavailable", Session);
        var proof = await GetActionProofAsync(client, "unavailable", Session, admission, "cart");

        var cart = await SendMutationAsync(client, "unavailable", Session, admission, proof);
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.Accepted, cart.StatusCode);
        Assert.True(metrics!.Behaviour.StateFailures > 0);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:MaximumActiveSessions"] = "30",
        ["DropShield:Admission:AdmissionBatchSize"] = "30",
        ["DropShield:Admission:WaitingTtlSeconds"] = "300",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:AdmissionTokens:Enabled"] = "true",
        ["DropShield:AdmissionTokens:SigningKey"] = SigningKey,
        ["DropShield:ActionProofs:Enabled"] = "true",
        ["DropShield:ActionProofs:LifetimeSeconds"] = "30",
        ["DropShield:BehaviourScoring:Enabled"] = "true",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };

    private static HttpClient CreateClient(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> GetAdmissionAsync(HttpClient client, string clientId, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/pokemon-etb/stock");
        request.Headers.Add("X-DropShield-Test-Client", clientId);
        request.Headers.Add("Cookie", $"DropShield.Session={session}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("DropShield.Admission=", StringComparison.Ordinal))
            .Split(';')[0]
            .Split('=')[1];
    }

    private static async Task<string> GetActionProofAsync(
        HttpClient client,
        string clientId,
        string session,
        string admission,
        string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        request.Headers.Add("X-DropShield-Test-Client", clientId);
        request.Headers.Add(
            "Cookie",
            $"DropShield.Session={session}; DropShield.Admission={admission}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActionProofResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        string clientId,
        string session,
        string admission,
        string actionProof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart");
        request.Headers.Add("X-DropShield-Test-Client", clientId);
        request.Headers.Add(
            "Cookie",
            $"DropShield.Session={session}; DropShield.Admission={admission}");
        request.Headers.Add("X-DropShield-Action", actionProof);
        return await client.SendAsync(request);
    }

    private sealed class UnavailableBehaviourState : IBehaviourState
    {
        public ValueTask<BehaviourEvidence> RecordAsync(
            string actor,
            BehaviourEventType eventType,
            CancellationToken cancellationToken) => Fail();

        public ValueTask<BehaviourEvidence> GetAsync(
            string actor,
            CancellationToken cancellationToken) => Fail();

        private static ValueTask<BehaviourEvidence> Fail() =>
            ValueTask.FromException<BehaviourEvidence>(new BehaviourStateUnavailableException(
                "Unavailable for test.",
                new InvalidOperationException()));
    }
}
