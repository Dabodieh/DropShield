using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Inventory;
using DropShield.Api.Models;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

public sealed class InventoryReservationTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string FirstSession = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task CartReservesAndCheckoutCommitsSyntheticInventory()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "stock", FirstSession);
        var cartProof = await GetActionProofAsync(client, "cart-proof", FirstSession, admission, "cart");

        var cart = await SendMutationAsync(client, "cart", FirstSession, admission, cartProof, "/api/cart");
        var reserved = await client.GetFromJsonAsync<InventorySnapshot>("/internal/inventory");

        var checkoutProof = await GetActionProofAsync(client, "checkout-proof", FirstSession, admission, "checkout");
        var checkout = await SendMutationAsync(client, "checkout", FirstSession, admission, checkoutProof, "/api/checkout");
        var committed = await client.GetFromJsonAsync<InventorySnapshot>("/internal/inventory");

        Assert.Equal(HttpStatusCode.Accepted, cart.StatusCode);
        Assert.Equal(new InventorySnapshot(1, 1, 0), reserved);
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        Assert.Equal(new InventorySnapshot(1, 0, 1), committed);
    }

    [Fact]
    public async Task DuplicateCartAndCheckoutWithoutReservation_AreRejectedBeforeOrigin()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "session-a", FirstSession);
        var firstCartProof = await GetActionProofAsync(client, "cart-a", FirstSession, admission, "cart");

        var firstCart = await SendMutationAsync(client, "cart-a", FirstSession, admission, firstCartProof, "/api/cart");
        var duplicateProof = await GetActionProofAsync(client, "cart-b", FirstSession, admission, "cart");
        var duplicateCart = await SendMutationAsync(client, "cart-b", FirstSession, admission, duplicateProof, "/api/cart");

        var secondSession = new string('b', 64);
        var secondAdmission = await GetAdmissionAsync(client, "session-b", secondSession);
        var checkoutProof = await GetActionProofAsync(client, "checkout-b", secondSession, secondAdmission, "checkout");
        var checkout = await SendMutationAsync(client, "checkout-b", secondSession, secondAdmission, checkoutProof, "/api/checkout");

        Assert.Equal(HttpStatusCode.Accepted, firstCart.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateCart.StatusCode);
        Assert.Equal("reservation_exists", (await duplicateCart.Content.ReadFromJsonAsync<GatewayErrorResponse>())!.Error);
        Assert.Equal(HttpStatusCode.Conflict, checkout.StatusCode);
        Assert.Equal("reservation_required", (await checkout.Content.ReadFromJsonAsync<GatewayErrorResponse>())!.Error);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
        Assert.Equal(0, factory.Origin.GetRequestCount("/api/checkout"));
    }

    [Fact]
    public async Task CartCompensatesOriginFailureAndOutOfStockDoesNotForward()
    {
        var settings = Settings();
        settings["DropShield:InventoryReservation:InitialStock"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);

        var firstAdmission = await GetAdmissionAsync(client, "stock-a", FirstSession);
        var firstProof = await GetActionProofAsync(client, "proof-a", FirstSession, firstAdmission, "cart");
        factory.Origin.ThrowOnSend = true;
        var failed = await SendMutationAsync(client, "cart-a", FirstSession, firstAdmission, firstProof, "/api/cart");
        factory.Origin.ThrowOnSend = false;

        var secondSession = new string('b', 64);
        var secondAdmission = await GetAdmissionAsync(client, "stock-b", secondSession);
        var secondProof = await GetActionProofAsync(client, "proof-b", secondSession, secondAdmission, "cart");
        var reserved = await SendMutationAsync(client, "cart-b", secondSession, secondAdmission, secondProof, "/api/cart");

        var thirdSession = new string('c', 64);
        var thirdAdmission = await GetAdmissionAsync(client, "stock-c", thirdSession);
        var thirdProof = await GetActionProofAsync(client, "proof-c", thirdSession, thirdAdmission, "cart");
        var outOfStock = await SendMutationAsync(client, "cart-c", thirdSession, thirdAdmission, thirdProof, "/api/cart");

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, reserved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, outOfStock.StatusCode);
        Assert.Equal("out_of_stock", (await outOfStock.Content.ReadFromJsonAsync<GatewayErrorResponse>())!.Error);
        Assert.Equal(1, factory.Origin.GetRequestCount("/api/cart"));
    }

    [Fact]
    public async Task FailedCheckoutRetainsReservationUntilSuccessfulCheckoutCommitsIt()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);
        var admission = await GetAdmissionAsync(client, "checkout", FirstSession);
        var cartProof = await GetActionProofAsync(client, "cart", FirstSession, admission, "cart");
        await SendMutationAsync(client, "cart", FirstSession, admission, cartProof, "/api/cart");

        factory.Origin.ThrowOnSend = true;
        var failedProof = await GetActionProofAsync(client, "checkout-failed", FirstSession, admission, "checkout");
        var failed = await SendMutationAsync(client, "checkout-failed", FirstSession, admission, failedProof, "/api/checkout");
        factory.Origin.ThrowOnSend = false;

        var retained = await client.GetFromJsonAsync<InventorySnapshot>("/internal/inventory");
        var successProof = await GetActionProofAsync(client, "checkout-success", FirstSession, admission, "checkout");
        var succeeded = await SendMutationAsync(client, "checkout-success", FirstSession, admission, successProof, "/api/checkout");

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(new InventorySnapshot(1, 1, 0), retained);
        Assert.Equal(HttpStatusCode.Accepted, succeeded.StatusCode);
        Assert.Equal(new InventorySnapshot(1, 0, 1), await client.GetFromJsonAsync<InventorySnapshot>("/internal/inventory"));
    }

    [Fact]
    public async Task InMemoryStateIsAtomicAndReturnsExpiredReservations()
    {
        var settings = Settings();
        settings["DropShield:InventoryReservation:InitialStock"] = "10";
        settings["DropShield:InventoryReservation:ReservationTtlSeconds"] = "1";
        var time = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(settings, timeProvider: time);
        _ = CreateClient(factory);
        var state = factory.Services.GetRequiredService<IInventoryReservationState>();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            state.TryReserveAsync("pokemon-etb", $"session-{index}", CancellationToken.None).AsTask()));

        Assert.Equal(10, results.Count(result => result.Status == ReservationStatus.Reserved));
        Assert.Equal(10, results.Count(result => result.Status == ReservationStatus.OutOfStock));
        Assert.Equal(new InventorySnapshot(0, 10, 0), await state.GetSnapshotAsync("pokemon-etb", CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new InventorySnapshot(10, 0, 0), await state.GetSnapshotAsync("pokemon-etb", CancellationToken.None));
    }

    [Fact]
    public async Task InvalidActionAndStateFailureDoNotForwardOrAllocateInventory()
    {
        using var invalidFactory = new DropShieldApiFactory(Settings());
        using var invalidClient = CreateClient(invalidFactory);
        var admission = await GetAdmissionAsync(invalidClient, "invalid", FirstSession);
        var invalidAction = await SendMutationAsync(invalidClient, "invalid", FirstSession, admission, "not-a-valid-action-proof", "/api/cart");

        Assert.Equal(HttpStatusCode.Forbidden, invalidAction.StatusCode);
        Assert.Equal(new InventorySnapshot(2, 0, 0), await invalidClient.GetFromJsonAsync<InventorySnapshot>("/internal/inventory"));
        Assert.Equal(0, invalidFactory.Origin.GetRequestCount("/api/cart"));

        using var unavailableFactory = new DropShieldApiFactory(Settings(), inventoryState: new UnavailableInventoryReservationState());
        using var unavailableClient = CreateClient(unavailableFactory);
        var unavailableAdmission = await GetAdmissionAsync(unavailableClient, "unavailable", FirstSession);
        var unavailableProof = await GetActionProofAsync(unavailableClient, "unavailable", FirstSession, unavailableAdmission, "cart");
        var unavailable = await SendMutationAsync(unavailableClient, "unavailable", FirstSession, unavailableAdmission, unavailableProof, "/api/cart");
        var metrics = await unavailableClient.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("state_unavailable", (await unavailable.Content.ReadFromJsonAsync<GatewayErrorResponse>())!.Error);
        Assert.Equal(0, unavailableFactory.Origin.GetRequestCount("/api/cart"));
        Assert.Equal(1, metrics!.InventoryReservations.ReservationStateFailures);
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
        ["DropShield:InventoryReservation:Enabled"] = "true",
        ["DropShield:InventoryReservation:InitialStock"] = "2",
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

    private static async Task<string> GetActionProofAsync(HttpClient client, string clientId, string session, string admission, string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        request.Headers.Add("X-DropShield-Test-Client", clientId);
        request.Headers.Add("Cookie", $"DropShield.Session={session}; DropShield.Admission={admission}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActionProofResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(HttpClient client, string clientId, string session, string admission, string actionProof, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-DropShield-Test-Client", clientId);
        request.Headers.Add("Cookie", $"DropShield.Session={session}; DropShield.Admission={admission}");
        request.Headers.Add("X-DropShield-Action", actionProof);
        return await client.SendAsync(request);
    }

    private sealed class UnavailableInventoryReservationState : IInventoryReservationState
    {
        public ValueTask<ReservationResult> TryReserveAsync(string drop, string sessionId, CancellationToken cancellationToken) => Fail();
        public ValueTask<ReservationResult> GetActiveAsync(string drop, string sessionId, CancellationToken cancellationToken) => Fail();
        public ValueTask<ReservationResult> ReleaseAsync(string drop, string sessionId, CancellationToken cancellationToken) => Fail();
        public ValueTask<ReservationResult> CommitAsync(string drop, string sessionId, CancellationToken cancellationToken) => Fail();
        public ValueTask<InventorySnapshot> GetSnapshotAsync(string drop, CancellationToken cancellationToken) => ValueTask.FromException<InventorySnapshot>(Exception());

        private static ValueTask<ReservationResult> Fail() => ValueTask.FromException<ReservationResult>(Exception());

        private static InventoryReservationStateUnavailableException Exception() => new(
            "Unavailable for test.",
            new InvalidOperationException());
    }
}
