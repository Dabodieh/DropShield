using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Origin;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

public sealed class CommerceTransportTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string CartPath = "/rest/V1/guest-carts/cart_123/items?fields=cart_item_id";
    private const string CheckoutPath = "/rest/default/V1/guest-carts/cart_123/payment-information";

    [Theory]
    [InlineData("/rest/V1/guest-carts/cart_123/items", TrafficRoute.CommerceRestCart)]
    [InlineData("/rest/default/V1/guest-carts/cart_123/payment-information", TrafficRoute.CommerceRestCheckout)]
    public void Matcher_RecognisesOnlySupportedGuestCartTemplates(string path, TrafficRoute expectedRoute)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;

        Assert.True(CommerceRouteMatcher.TryMatch(context.Request, out var match));
        Assert.Equal(expectedRoute, match.TrafficRoute);
    }

    [Fact]
    public async Task CommerceRestCartAndCheckout_ForwardConcreteRouteWithValidAssertion()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        await AdmitAsync(browser);

        var cartProof = await ActionProofAsync(browser, "cart");
        var cart = await CommerceRequestAsync(browser, CartPath, cartProof, CartBody());
        Assert.Equal(HttpStatusCode.Accepted, cart.StatusCode);
        Assert.Equal(CartPath, factory.Origin.LastForwardedPath);
        AssertValidAssertion(
            factory,
            "cart",
            "POST /rest/V1/guest-carts/cart_123/items",
            Encoding.UTF8.GetBytes(CartBody()));

        var checkoutProof = await ActionProofAsync(browser, "checkout");
        const string checkoutBody = "{\"paymentMethod\":{\"method\":\"checkmo\"},\"billingAddress\":{}}";
        var checkout = await CommerceRequestAsync(browser, CheckoutPath, checkoutProof, checkoutBody);
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        Assert.Equal(CheckoutPath, factory.Origin.LastForwardedPath);
        AssertValidAssertion(
            factory,
            "checkout",
            "POST /rest/default/V1/guest-carts/cart_123/payment-information",
            Encoding.UTF8.GetBytes(checkoutBody));
    }

    [Fact]
    public async Task ProtectedCommerceCartWithoutActionProof_IsNotForwarded()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        await AdmitAsync(browser);

        var response = await CommerceRequestAsync(browser, CartPath, null, CartBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Origin.TotalRequests); // Commerce admission stock is local.
    }

    [Fact]
    public async Task UnsupportedCommerceRestPath_IsNotAnOriginPassthrough()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);

        var response = await browser.PostAsync(
            "/rest/V1/guest-carts/cart_123/items/extra",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task AddSimpleProductsToCart_IsRecognisedAndBoundToGraphQl()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        await AdmitAsync(browser);
        var proof = await ActionProofAsync(browser, "cart");
        var body = JsonSerializer.Serialize(new
        {
            query = "mutation AddSimple($input: AddSimpleProductsToCartInput!) { addSimpleProductsToCart(input: $input) { cart { id } } }",
            operationName = "AddSimple",
            variables = new { input = new { cart_id = "cart_123", cart_items = new[] { new { data = new { sku = "pokemon-etb", quantity = 1 } } } } },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);
        request.Headers.Add("X-DropShield-Action", proof);

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertValidAssertion(factory, "cart", "POST /graphql", Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task AddProductsToCart_IsRecognisedAndBoundToGraphQl()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        await AdmitAsync(browser);
        var proof = await ActionProofAsync(browser, "cart");
        var body = JsonSerializer.Serialize(new
        {
            query = "mutation AddProducts($cartId: String!, $cartItems: [CartItemInput!]!) { addProductsToCart(cartId: $cartId, cartItems: $cartItems) { cart { id } } }",
            operationName = "AddProducts",
            variables = new { cartId = "cart_123", cartItems = new[] { new { sku = "pokemon-etb", quantity = 1 } } },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);
        request.Headers.Add("X-DropShield-Action", proof);

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertValidAssertion(factory, "cart", "POST /graphql", Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task AddVirtualProductsToCart_WithAliasAndOperationName_UsesTheProtectedGraphQlPath()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        await AdmitAsync(browser);
        var proof = await ActionProofAsync(browser, "cart");
        var body = JsonSerializer.Serialize(new
        {
            query = "mutation AddVirtual($input: AddVirtualProductsToCartInput!) { virtualCart: addVirtualProductsToCart(input: $input) { cart { id } } }",
            operationName = "AddVirtual",
            variables = new { input = new { cart_id = "cart_123", cart_items = new[] { new { data = new { sku = "pokemon-etb", quantity = 1 } } } } },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);
        request.Headers.Add("X-DropShield-Action", proof);
        request.Headers.Add("X-DropShield-Origin-Assertion", "v1.client-forged.forged");

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, Encoding.UTF8.GetString(factory.Origin.LastForwardedBody!));
        Assert.NotEqual("v1.client-forged.forged", factory.Origin.LastOriginAssertionHeader!.Value.Value);
        AssertValidAssertion(factory, "cart", "POST /graphql", Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task OrdinaryVirtualGraphQlCartAdd_RemainsOutsideTheProtectedPipeline()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = Browser(factory);
        var body = JsonSerializer.Serialize(new
        {
            query = "mutation { addVirtualProductsToCart(input: { cart_id: \"cart_123\", cart_items: [{ data: { sku: \"regular-mug\", quantity: 1 } }] }) { cart { id } } }",
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Origin.LastOriginAssertionHeader);
        Assert.Equal(body, Encoding.UTF8.GetString(factory.Origin.LastForwardedBody!));
    }

    [Fact]
    public async Task CommerceResponse_PreservesMagentoSessionCookies()
    {
        using var factory = new DropShieldApiFactory(Settings());
        factory.Origin.NextResponseHeaders = new Dictionary<string, string[]>
        {
            ["Set-Cookie"] = ["PHPSESSID=commerce-session; Path=/; HttpOnly"],
            ["X-Magento-Vary"] = ["store=default"],
        };
        using var browser = Browser(factory);
        await AdmitAsync(browser);
        var proof = await ActionProofAsync(browser, "cart");

        var response = await CommerceRequestAsync(browser, CartPath, proof, CartBody());

        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("PHPSESSID=commerce-session", StringComparison.Ordinal));
        Assert.Equal("store=default", response.Headers.GetValues("X-Magento-Vary").Single());
    }

    [Fact]
    public async Task OverLimitProtectedCommerceBody_Returns413BeforeForwarding()
    {
        var settings = Settings();
        settings["DropShield:AdobeCommerce:MaximumProtectedRequestBodyBytes"] = "4096";
        using var factory = new DropShieldApiFactory(settings);
        using var client = Browser(factory);
        var body = "{" + new string('x', 4_096) + "}";
        using var request = new HttpRequestMessage(HttpMethod.Post, CartPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        AddIdentity(request);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Origin.TotalRequests);
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
        HttpClient client,
        string path,
        string? actionProof,
        string body)
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

    private static void AssertValidAssertion(
        DropShieldApiFactory factory,
        string action,
        string route,
        byte[] body)
    {
        var assertion = factory.Origin.LastOriginAssertionHeader;
        Assert.NotNull(assertion);
        var service = factory.Services.GetRequiredService<IOriginAssertionService>();
        Assert.True(service.Validate(
            assertion!.Value.Value,
            "pokemon-etb",
            action,
            "POST",
            route,
            body).IsValid);
    }

    private static void AddIdentity(HttpRequestMessage request) =>
        request.Headers.Add("X-DropShield-Test-Client", "commerce-browser");

    private static string CartBody() =>
        "{\"cartItem\":{\"sku\":\"pokemon-etb\",\"qty\":1,\"quote_id\":\"cart_123\"}}";

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
}
