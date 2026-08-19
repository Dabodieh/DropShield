using System.Net;
using System.Net.Http.Json;
using System.Text;
using DropShield.Api.Actions;
using DropShield.Api.Inventory;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DropShield.Tests;

/// <summary>
/// Focused regression coverage for the dynamic drop-ID resolution
/// <see cref="InventoryReservationMiddleware"/> now performs in AdobeCommerce mode (manifest-
/// resolved drop instead of the old static <c>Admission:ProtectedProduct</c> configuration).
/// </summary>
public sealed class InventoryReservationDropIdTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string CartPath = "/rest/V1/guest-carts/cart_123/items?fields=cart_item_id";
    private const string CheckoutPath = "/rest/default/V1/guest-carts/cart_123/payment-information";

    [Fact]
    public async Task ProtectedCheckout_UsesTheManifestResolvedDropIdForReservationCommit()
    {
        var catalog = new ControllableProtectedDropCatalog();
        catalog.SetActiveDrop("drop-a", (2, "pokemon-etb"));
        using var factory = new DropShieldApiFactory(Settings(), protectedDropCatalog: catalog);
        using var browser = Browser(factory);
        await AdmitAsync(browser);

        var cartProof = await ActionProofAsync(browser, "cart");
        var cart = await CommerceRequestAsync(browser, CartPath, cartProof, CartBody());
        Assert.Equal(HttpStatusCode.Accepted, cart.StatusCode);

        var checkoutProof = await ActionProofAsync(browser, "checkout");
        var checkout = await CommerceRequestAsync(
            browser, CheckoutPath, checkoutProof, "{\"paymentMethod\":{\"method\":\"checkmo\"},\"billingAddress\":{}}");
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);

        var state = factory.Services.GetRequiredService<IInventoryReservationState>();
        Assert.Equal(new InventorySnapshot(499, 0, 1), await state.GetSnapshotAsync("drop-a", CancellationToken.None));
    }

    [Fact]
    public async Task MiddlewareFallback_UsesCatalogActiveDropWhenObservationHasNoDropId()
    {
        var catalog = new ControllableProtectedDropCatalog();
        catalog.SetActiveDrop("fallback-drop", (2, "pokemon-etb"));
        var state = new InMemoryStateFake();
        var middleware = new InventoryReservationMiddleware(_ => Task.CompletedTask);
        var context = CheckoutContextWithNoResolvedDropId();

        await middleware.InvokeAsync(
            context,
            state,
            Options(),
            TestMetrics(),
            catalog,
            NullLogger());

        Assert.Equal("fallback-drop", state.LastDrop);
    }

    [Fact]
    public async Task NoActiveDrop_IsOrdinaryCheckout_NoReservationCreated()
    {
        var catalog = new ControllableProtectedDropCatalog();
        catalog.SetNoActiveDrop();
        using var factory = new DropShieldApiFactory(Settings(), protectedDropCatalog: catalog);
        using var browser = Browser(factory);

        var checkout = await CommerceRequestAsync(
            browser, CheckoutPath, null, "{\"paymentMethod\":{\"method\":\"checkmo\"},\"billingAddress\":{}}");

        // No active drop: the connector's own resolver independently treats this as ordinary
        // Commerce traffic. Origin forwarding is exercised elsewhere; this test only proves
        // DropShield does not invent a drop identifier or attempt a reservation for one.
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        var state = factory.Services.GetRequiredService<IInventoryReservationState>();
        var snapshot = await state.GetSnapshotAsync(string.Empty, CancellationToken.None);
        Assert.Equal(new InventorySnapshot(500, 0, 0), snapshot);
    }

    [Fact]
    public async Task UnusableCatalog_FailsClosedBeforeReservationMiddlewareRuns()
    {
        var catalog = new ControllableProtectedDropCatalog();
        catalog.SetActiveDrop("drop-a", (2, "pokemon-etb"));
        catalog.SetUsable(false);
        using var factory = new DropShieldApiFactory(Settings(), protectedDropCatalog: catalog);
        using var browser = Browser(factory);

        var checkout = await CommerceRequestAsync(
            browser, CheckoutPath, null, "{\"paymentMethod\":{\"method\":\"checkmo\"},\"billingAddress\":{}}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, checkout.StatusCode);
        var body = await checkout.Content.ReadFromJsonAsync<GatewayErrorResponse>();
        Assert.Equal("protection_catalog_unavailable", body!.Error);
        var state = factory.Services.GetRequiredService<IInventoryReservationState>();
        Assert.Equal(new InventorySnapshot(500, 0, 0), await state.GetSnapshotAsync("drop-a", CancellationToken.None));
    }

    [Fact]
    public async Task ReservationUnderOneDrop_IsNotVisibleOrCommittableUnderAnotherDrop()
    {
        using var factory = new DropShieldApiFactory(Settings());
        var state = factory.Services.GetRequiredService<IInventoryReservationState>();

        var reserved = await state.TryReserveAsync("drop-a", "session-1", CancellationToken.None);
        Assert.Equal(ReservationStatus.Reserved, reserved.Status);

        // The same session has no reservation recorded under a different drop identifier.
        var activeUnderWrongDrop = await state.GetActiveAsync("drop-b", "session-1", CancellationToken.None);
        Assert.Equal(ReservationStatus.Missing, activeUnderWrongDrop.Status);

        var committedUnderWrongDrop = await state.CommitAsync("drop-b", "session-1", CancellationToken.None);
        Assert.Equal(ReservationStatus.Missing, committedUnderWrongDrop.Status);

        // The original reservation under the correct drop is untouched by the wrong-drop attempt.
        var activeUnderCorrectDrop = await state.GetActiveAsync("drop-a", "session-1", CancellationToken.None);
        Assert.Equal(ReservationStatus.Active, activeUnderCorrectDrop.Status);
    }

    /// <summary>
    /// Simulates a checkout mutation whose <see cref="TrafficRequestObservation"/> was created
    /// (so <see cref="ActionProofPolicy.AppliesToMutation"/> lets the request through) but whose
    /// <see cref="TrafficRequestObservation.ProtectedDropId"/> was never populated — the only
    /// circumstance in which <see cref="InventoryReservationMiddleware"/>'s
    /// <c>?? catalog.GetActiveDrop()?.DropId</c> fallback is actually reached.
    /// </summary>
    private static DefaultHttpContext CheckoutContextWithNoResolvedDropId()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = CheckoutPath;
        context.Items[ActionProofMiddleware.AuthorizedSessionItemKey] = "session-x";
        var observation = new TrafficRequestObservation(isProtectedStock: false)
        {
            IsCommerceCheckoutMutation = true,
        };
        context.Features.Set(observation);
        return context;
    }

    private static HttpClient Browser(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task AdmitAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/pokemon-etb/stock");
        AddIdentity(request);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static async Task<string> ActionProofAsync(HttpClient client, string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        AddIdentity(request);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActionProofResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> CommerceRequestAsync(
        HttpClient client, string path, string? actionProof, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);
        if (actionProof is not null)
        {
            request.Headers.Add("X-DropShield-Action", actionProof);
        }

        return await client.SendAsync(request);
    }

    private static void AddIdentity(HttpRequestMessage request) =>
        request.Headers.Add("X-DropShield-Test-Client", "commerce-browser");

    private static string CartBody() =>
        "{\"cartItem\":{\"sku\":\"pokemon-etb\",\"qty\":1,\"quote_id\":\"cart_123\"}}";

    private static Microsoft.Extensions.Options.IOptions<DropShieldOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new DropShieldOptions
        {
            InventoryReservation = new InventoryReservationOptions { Enabled = true },
        });

    private static TrafficMetrics TestMetrics() => new(TimeProvider.System);

    private static ILogger<InventoryReservationMiddleware> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryReservationMiddleware>.Instance;

    private static Dictionary<string, string?> Settings() => new()
    {
        ["DropShield:OriginMode"] = "AdobeCommerce",
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:MaximumActiveSessions"] = "10",
        ["DropShield:Admission:AdmissionBatchSize"] = "10",
        ["DropShield:Admission:MaximumWaitingSessions"] = "10",
        ["DropShield:Admission:SessionTtlSeconds"] = "300",
        ["DropShield:Admission:WaitingTtlSeconds"] = "300",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:AdmissionTokens:Enabled"] = "true",
        ["DropShield:AdmissionTokens:SigningKey"] = SigningKey,
        ["DropShield:ActionProofs:Enabled"] = "true",
        ["DropShield:ActionProofs:SigningKey"] = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=",
        ["DropShield:OriginAssertions:Enabled"] = "true",
        ["DropShield:OriginAssertions:SigningKey"] = "QEFCQ0RFRkdISUpLTE1OT1BRUlNUVVZXWFlaW1xdXl8=",
        ["DropShield:InventoryReservation:Enabled"] = "true",
        ["DropShield:InternalHashing:SigningKey"] = "YGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn8=",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };

    private sealed class InMemoryStateFake : IInventoryReservationState
    {
        public string? LastDrop { get; private set; }

        public ValueTask<ReservationResult> TryReserveAsync(string drop, string sessionId, CancellationToken cancellationToken)
        {
            LastDrop = drop;
            return ValueTask.FromResult(new ReservationResult(ReservationStatus.Reserved, new InventorySnapshot(0, 1, 0), 0));
        }

        public ValueTask<ReservationResult> GetActiveAsync(string drop, string sessionId, CancellationToken cancellationToken)
        {
            LastDrop = drop;
            return ValueTask.FromResult(new ReservationResult(ReservationStatus.Active, new InventorySnapshot(0, 1, 0), 0));
        }

        public ValueTask<ReservationResult> ReleaseAsync(string drop, string sessionId, CancellationToken cancellationToken)
        {
            LastDrop = drop;
            return ValueTask.FromResult(new ReservationResult(ReservationStatus.Released, new InventorySnapshot(1, 0, 0), 0));
        }

        public ValueTask<ReservationResult> CommitAsync(string drop, string sessionId, CancellationToken cancellationToken)
        {
            LastDrop = drop;
            return ValueTask.FromResult(new ReservationResult(ReservationStatus.Committed, new InventorySnapshot(0, 0, 1), 0));
        }

        public ValueTask<InventorySnapshot> GetSnapshotAsync(string drop, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new InventorySnapshot(0, 0, 0));
    }
}
