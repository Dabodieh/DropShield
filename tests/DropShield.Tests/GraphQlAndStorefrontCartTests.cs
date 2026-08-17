using System.Net;
using System.Text;
using DropShield.Api.Origin;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

/// <summary>
/// Covers the DropShield.Api-side transport gap identified in the Adobe Commerce runtime
/// validation: DropShield previously had no route for POST /graphql or POST /checkout/cart/add,
/// so it could never issue an origin assertion shaped for the Magento GraphQL/storefront
/// cart-add paths the connector already validates. See docs/adobe-commerce.md.
/// </summary>
public sealed class GraphQlAndStorefrontCartTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string ProtectedAddToCartQuery =
        "mutation { addProductsToCart(cartId: \"c1\", cartItems: [{ sku: \"pokemon-etb\", quantity: 1 }]) { cart { items { id } } } }";
    private const string OrdinarySkuAddToCartQuery =
        "mutation { addProductsToCart(cartId: \"c1\", cartItems: [{ sku: \"regular-mug\", quantity: 1 }]) { cart { items { id } } } }";
    private const string CatalogueQuery = "query { products(search: \"mug\") { items { sku } } }";

    [Fact]
    public async Task ProtectedGraphQlMutation_ReceivesFreshAssertionBoundToGraphQlRoute()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        using var request = GraphQlRequest(ProtectedAddToCartQuery, "buyer");
        request.Headers.Add("X-DropShield-Origin-Assertion", "v1.client-forged.forged");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(factory.Origin.LastOriginAssertionHeader);
        Assert.Equal("X-DropShield-Origin-Assertion", factory.Origin.LastOriginAssertionHeader!.Value.HeaderName);
        Assert.NotEqual("v1.client-forged.forged", factory.Origin.LastOriginAssertionHeader.Value.Value);
    }

    [Fact]
    public async Task OrdinaryGraphQlQuery_IsForwardedWithoutAssertionOrMutationPipeline()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        var response = await client.SendAsync(GraphQlRequest(CatalogueQuery, "browser"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Origin.LastOriginAssertionHeader);
    }

    [Fact]
    public async Task OrdinarySkuGraphQlAddToCart_IsForwardedWithoutAssertion()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        var response = await client.SendAsync(GraphQlRequest(OrdinarySkuAddToCartQuery, "browser"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Origin.LastOriginAssertionHeader);
    }

    [Fact]
    public async Task GraphQlAssertion_BindsToRealGraphQlRouteAndMethod()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<IOriginAssertionService>();

        await client.SendAsync(GraphQlRequest(ProtectedAddToCartQuery, "buyer"));

        var assertion = factory.Origin.LastOriginAssertionHeader!.Value.Value;
        var body = factory.Origin.LastForwardedBody!;
        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /graphql", body);
        Assert.True(result.IsValid);

        var wrongRoute = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/cart", body);
        Assert.False(wrongRoute.IsValid);
    }

    [Fact]
    public async Task GraphQlBodyHash_CoversExactForwardedBytes()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<IOriginAssertionService>();

        await client.SendAsync(GraphQlRequest(ProtectedAddToCartQuery, "buyer"));

        var assertion = factory.Origin.LastOriginAssertionHeader!.Value.Value;
        var forwardedBody = factory.Origin.LastForwardedBody!;

        // Whitespace-only change to the query text changes the raw bytes DropShield hashed;
        // validation must fail even though the mutation is semantically identical.
        var mutatedBody = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(forwardedBody).Replace("{ ", "{  "));

        Assert.True(service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /graphql", forwardedBody).IsValid);
        Assert.False(service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /graphql", mutatedBody).IsValid);
    }

    [Fact]
    public async Task RejectedGraphQlMutation_ReceivesNoOriginAssertion()
    {
        var settings = Settings();
        settings["DropShield:Policies:Cart:ClientPermitLimit"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        await client.SendAsync(GraphQlRequest(ProtectedAddToCartQuery, "buyer"));
        var limited = await client.SendAsync(GraphQlRequest(ProtectedAddToCartQuery, "buyer"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task StorefrontCartAdd_ReceivesFreshAssertionBoundToStorefrontRoute()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<IOriginAssertionService>();

        using var request = StorefrontRequest("product=2&form_key=abc123&qty=1", "buyer");
        request.Headers.Add("X-DropShield-Origin-Assertion", "v1.client-forged.forged");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(factory.Origin.LastOriginAssertionHeader);
        Assert.NotEqual("v1.client-forged.forged", factory.Origin.LastOriginAssertionHeader!.Value.Value);

        var assertion = factory.Origin.LastOriginAssertionHeader.Value.Value;
        var result = service.Validate(
            assertion, "pokemon-etb", "cart", "POST", "POST /checkout/cart/add", factory.Origin.LastForwardedBody!);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task StorefrontRawFormBytes_AreUsedForBodyHashing()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        const string formBody = "product=2&form_key=abc123&qty=1";
        await client.SendAsync(StorefrontRequest(formBody, "buyer"));

        Assert.Equal(formBody, Encoding.UTF8.GetString(factory.Origin.LastForwardedBody!));
    }

    [Fact]
    public async Task RejectedStorefrontCartAdd_ReceivesNoOriginAssertion()
    {
        var settings = Settings();
        settings["DropShield:Policies:Cart:ClientPermitLimit"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = factory.CreateClient();

        await client.SendAsync(StorefrontRequest("product=2&form_key=abc123&qty=1", "buyer"));
        var limited = await client.SendAsync(StorefrontRequest("product=2&form_key=abc123&qty=1", "buyer"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task ClientSuppliedOriginAssertion_IsStrippedOnGraphQlAndStorefrontRoutes()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        using var graphQlRequest = GraphQlRequest(CatalogueQuery, "probe");
        graphQlRequest.Headers.Add("X-DropShield-Origin-Assertion", "v1.fake.fake");
        await client.SendAsync(graphQlRequest);
        Assert.Null(factory.Origin.LastOriginAssertionHeader);

        using var storefrontRequest = StorefrontRequest("product=1&form_key=abc123&qty=1", "probe");
        storefrontRequest.Headers.Add("X-DropShield-Origin-Assertion", "v1.fake.fake");
        await client.SendAsync(storefrontRequest);
        Assert.NotNull(factory.Origin.LastOriginAssertionHeader);
        Assert.NotEqual("v1.fake.fake", factory.Origin.LastOriginAssertionHeader!.Value.Value);
    }

    [Fact]
    public async Task ExistingRestCartAndCheckout_RemainUnaffected()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient();

        using var cartRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart");
        cartRequest.Headers.Add("X-DropShield-Test-Client", "buyer");
        cartRequest.Content = new StringContent("""{"productId":"pokemon-etb"}""", Encoding.UTF8, "application/json");
        var cartResponse = await client.SendAsync(cartRequest);

        using var checkoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/checkout");
        checkoutRequest.Headers.Add("X-DropShield-Test-Client", "buyer");
        checkoutRequest.Content = new StringContent("""{"productId":"pokemon-etb"}""", Encoding.UTF8, "application/json");
        var checkoutResponse = await client.SendAsync(checkoutRequest);

        Assert.Equal(HttpStatusCode.Accepted, cartResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, checkoutResponse.StatusCode);
        Assert.NotNull(factory.Origin.LastOriginAssertionHeader);
    }

    [Fact]
    public void ContractRouteLiterals_MatchClassifierTemplates()
    {
        Assert.Equal(
            "POST /graphql",
            TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.GraphQlCartAdd));
        Assert.Equal(
            "POST /checkout/cart/add",
            TrafficRouteClassifier.GetRouteTemplate(TrafficRoute.StorefrontCartAdd));
    }

    private static HttpRequestMessage GraphQlRequest(string query, string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Content = new StringContent(
            $$"""{"query":"{{query.Replace("\"", "\\\"")}}"}""",
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private static HttpRequestMessage StorefrontRequest(string formBody, string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/checkout/cart/add");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded");
        return request;
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["DropShield:Admission:Enabled"] = "false",
        ["DropShield:AdmissionTokens:Enabled"] = "false",
        ["DropShield:ActionProofs:Enabled"] = "false",
        ["DropShield:InventoryReservation:Enabled"] = "false",
        ["DropShield:BehaviourScoring:Enabled"] = "false",
        ["DropShield:OriginAssertions:Enabled"] = "true",
        ["DropShield:OriginAssertions:LifetimeSeconds"] = "20",
        ["DropShield:OriginAssertions:SigningKey"] = SigningKey,
        ["DropShield:ProtectedProducts:0"] = "pokemon-etb",
        ["DropShield:Admission:ProtectedProduct"] = "pokemon-etb",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };
}
