using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

public sealed class ActionProofTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string Session = "abababababababababababababababababababababababababababababababab";

    [Fact]
    public async Task AdmittedClient_CanObtainSeparateCartAndCheckoutProofs()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);

        var cart = await RequestActionProofAsync(client, "cart-client", Session, admissionToken, "cart");
        var checkout = await RequestActionProofAsync(
            client,
            "checkout-client",
            Session,
            admissionToken,
            "checkout");

        Assert.Equal(HttpStatusCode.OK, cart.StatusCode);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.Equal("cart", (await cart.Content.ReadFromJsonAsync<ActionProofResponse>())!.Action);
        Assert.Equal("checkout", (await checkout.Content.ReadFromJsonAsync<ActionProofResponse>())!.Action);
    }

    [Fact]
    public async Task MissingOrWaitingAdmission_CannotObtainActionProof()
    {
        var settings = Settings();
        settings["DropShield:Admission:MaximumActiveSessions"] = "1";
        settings["DropShield:Admission:AdmissionBatchSize"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);

        var missing = await RequestActionProofAsync(client, "missing", new string('c', 64), null, "cart");
        await GetAdmissionTokenAsync(client, "owner", new string('a', 64));
        var waiting = await SendStockAsync(client, "waiting", new string('b', 64));
        var denied = await RequestActionProofAsync(client, "waiting", new string('b', 64), null, "cart");

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("cart", "/api/cart", HttpStatusCode.Accepted)]
    [InlineData("checkout", "/api/checkout", HttpStatusCode.Accepted)]
    public async Task ValidActionProof_ForwardsExactlyOnce(
        string action,
        string mutationPath,
        HttpStatusCode expectedStatus)
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);
        var actionProof = await GetActionProofAsync(client, "proof", Session, admissionToken, action);

        var first = await SendMutationAsync(client, "mutation", Session, admissionToken, actionProof, mutationPath);
        var replay = await SendMutationAsync(client, "mutation", Session, admissionToken, actionProof, mutationPath);

        Assert.Equal(expectedStatus, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(1, factory.Origin.GetRequestCount(mutationPath));
    }

    [Theory]
    [InlineData("cart", "/api/checkout")]
    [InlineData("checkout", "/api/cart")]
    public async Task ActionProof_CannotAuthorizeDifferentMutation(string action, string mutationPath)
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);
        var actionProof = await GetActionProofAsync(client, "proof", Session, admissionToken, action);

        var response = await SendMutationAsync(client, "mutation", Session, admissionToken, actionProof, mutationPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public void ActionProof_RejectsWrongDropAndSession()
    {
        using var factory = new DropShieldApiFactory(Settings());
        var service = factory.Services.GetRequiredService<IActionTokenService>();
        var proof = service.Issue("pokemon-etb", Session, ActionKind.Cart);

        Assert.Equal(
            ActionTokenValidationFailure.WrongDrop,
            service.Validate(proof, "another-drop", Session, ActionKind.Cart).Failure);
        Assert.Equal(
            ActionTokenValidationFailure.WrongSession,
            service.Validate(proof, "pokemon-etb", new string('f', 64), ActionKind.Cart).Failure);
    }

    [Fact]
    public async Task InvalidOrExpiredActionProof_IsNotForwarded()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(Settings(), timeProvider: clock);
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);
        var actionProof = await GetActionProofAsync(client, "proof", Session, admissionToken, "cart");
        var modified = actionProof[..^1] + (actionProof[^1] == 'A' ? 'B' : 'A');

        var invalid = await SendMutationAsync(client, "invalid", Session, admissionToken, modified, "/api/cart");
        clock.Advance(TimeSpan.FromSeconds(31));
        var expired = await SendMutationAsync(client, "expired", Session, admissionToken, actionProof, "/api/cart");

        Assert.Equal(HttpStatusCode.Forbidden, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task ConcurrentReplay_ConsumesExactlyOnce()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var clientA = CreateClient(factory);
        using var clientB = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(clientA, "admitted", Session);
        var actionProof = await GetActionProofAsync(clientA, "proof", Session, admissionToken, "cart");

        var responses = await Task.WhenAll(
            SendMutationAsync(clientA, "mutation-a", Session, admissionToken, actionProof, "/api/cart"),
            SendMutationAsync(clientB, "mutation-b", Session, admissionToken, actionProof, "/api/cart"));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
    }

    [Fact]
    public async Task RevokedAdmissionLease_CannotObtainActionProofEvenWithUnexpiredToken()
    {
        var admissionState = new RevocableAdmissionState();
        using var factory = new DropShieldApiFactory(Settings(), admissionState: admissionState);
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);

        admissionState.Revoke();
        var revoked = await RequestActionProofAsync(client, "proof-client", Session, admissionToken, "cart");

        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task RevokedAdmissionLease_BlocksMutationEvenWithValidActionProof()
    {
        var admissionState = new RevocableAdmissionState();
        using var factory = new DropShieldApiFactory(Settings(), admissionState: admissionState);
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);
        var actionProof = await GetActionProofAsync(client, "proof", Session, admissionToken, "cart");

        admissionState.Revoke();
        var afterRevocation = await SendMutationAsync(
            client,
            "mutation",
            Session,
            admissionToken,
            actionProof,
            "/api/cart");

        Assert.Equal(HttpStatusCode.Forbidden, afterRevocation.StatusCode);
        Assert.Equal(0, factory.Origin.GetRequestCount("/api/cart"));
    }

    [Fact]
    public async Task ActionProofIssuance_RemainsSubjectToCartRateLimit()
    {
        var settings = Settings();
        settings["DropShield:Policies:Cart:ClientPermitLimit"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);

        var first = await RequestActionProofAsync(client, "proof-client", Session, admissionToken, "cart");
        var limited = await RequestActionProofAsync(client, "proof-client", Session, admissionToken, "cart");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task ReplayStateFailure_FailsClosedAndMetricsRemainPrivate()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(
            Settings(),
            timeProvider: clock,
            replayState: new UnavailableReplayState());
        using var client = CreateClient(factory);
        var admissionToken = await GetAdmissionTokenAsync(client, "admitted", Session);
        var actionProof = await GetActionProofAsync(client, "proof", Session, admissionToken, "cart");

        var unavailable = await SendMutationAsync(
            client,
            "mutation",
            Session,
            admissionToken,
            actionProof,
            "/api/cart");
        var metricsJson = await client.GetStringAsync("/internal/metrics");
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ActionProofs.ReplayStateUnavailable);
        Assert.DoesNotContain(Session, metricsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(actionProof, metricsJson, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:ProtectedProduct"] = "pokemon-etb",
        ["DropShield:Admission:MaximumActiveSessions"] = "10",
        ["DropShield:Admission:AdmissionBatchSize"] = "10",
        ["DropShield:Admission:MaximumWaitingSessions"] = "10",
        ["DropShield:Admission:SessionTtlSeconds"] = "300",
        ["DropShield:Admission:WaitingTtlSeconds"] = "300",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:AdmissionTokens:Enabled"] = "true",
        ["DropShield:AdmissionTokens:LifetimeSeconds"] = "60",
        ["DropShield:AdmissionTokens:SigningKey"] = SigningKey,
        ["DropShield:ActionProofs:Enabled"] = "true",
        ["DropShield:ActionProofs:LifetimeSeconds"] = "30",
        ["DropShield:ActionProofs:ReplayTtlMarginSeconds"] = "10",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };

    private static HttpClient CreateClient(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> GetAdmissionTokenAsync(
        HttpClient client,
        string identity,
        string session)
    {
        var response = await SendStockAsync(client, identity, session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return GetCookie(response, "DropShield.Admission")!;
    }

    private static async Task<string> GetActionProofAsync(
        HttpClient client,
        string identity,
        string session,
        string admissionToken,
        string action)
    {
        var response = await RequestActionProofAsync(client, identity, session, admissionToken, action);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActionProofResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> RequestActionProofAsync(
        HttpClient client,
        string identity,
        string session,
        string? admissionToken,
        string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Headers.Add(
            "Cookie",
            admissionToken is null
                ? $"DropShield.Session={session}"
                : $"DropShield.Session={session}; DropShield.Admission={admissionToken}");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendStockAsync(
        HttpClient client,
        string identity,
        string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/pokemon-etb/stock");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Headers.Add("Cookie", $"DropShield.Session={session}");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        string identity,
        string session,
        string admissionToken,
        string actionToken,
        string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Headers.Add("Cookie", $"DropShield.Session={session}; DropShield.Admission={admissionToken}");
        request.Headers.Add("X-DropShield-Action", actionToken);
        return await client.SendAsync(request);
    }

    private static string? GetCookie(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .SingleOrDefault(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return header?.Split(';', 2)[0].Split('=', 2)[1];
    }

    private sealed class UnavailableReplayState : IReplayState
    {
        public ValueTask<ReplayConsumeResult> TryConsumeAsync(
            string replayKey,
            TimeSpan timeToLive,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<ReplayConsumeResult>(
                new ReplayStateUnavailableException("Unavailable for test.", new TimeoutException()));
    }
}
