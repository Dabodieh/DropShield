using System.Net;
using System.Net.Http.Json;
using System.Text;
using DropShield.Api.Models;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DropShield.Tests;

public sealed class BrowserCookieTransportTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string GraphQlCartAdd =
        "mutation { addProductsToCart(cartId: \"c1\", cartItems: [{ sku: \"pokemon-etb\", quantity: 1 }]) { cart { items { id } } } }";

    [Fact]
    public async Task BrowserCookieContainer_CarriesAdmissionProofAcrossProtectedRoutes()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var stock = await SendAsync(browser, HttpMethod.Get, "/api/products/pokemon-etb/stock");
        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        Assert.Contains(stock.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("DropShield.Admission=", StringComparison.Ordinal) &&
            value.Contains("path=/", StringComparison.OrdinalIgnoreCase));

        var cartProof = await GetActionProofAsync(browser, "cart");
        Assert.Equal(HttpStatusCode.Accepted, (await SendMutationAsync(browser, "/api/cart", cartProof)).StatusCode);

        var checkoutProof = await GetActionProofAsync(browser, "checkout");
        Assert.Equal(HttpStatusCode.Accepted, (await SendMutationAsync(browser, "/api/checkout", checkoutProof)).StatusCode);

        var graphqlProof = await GetActionProofAsync(browser, "cart");
        using var graphQlRequest = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent($"{{\"query\":{System.Text.Json.JsonSerializer.Serialize(GraphQlCartAdd)}}}", Encoding.UTF8, "application/json"),
        };
        AddIdentity(graphQlRequest);
        graphQlRequest.Headers.Add("X-DropShield-Action", graphqlProof);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(graphQlRequest)).StatusCode);

        var storefrontProof = await GetActionProofAsync(browser, "cart");
        Assert.Equal(HttpStatusCode.Accepted, (await SendMutationAsync(
            browser,
            "/checkout/cart/add",
            storefrontProof)).StatusCode);
        Assert.Equal(1, factory.Origin.GetRequestCount("/graphql"));
        Assert.Equal(1, factory.Origin.GetRequestCount("/checkout/cart/add"));
    }

    [Fact]
    public async Task ClientWithoutBrowserCookies_ReceivesAdmissionRequired()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await SendAsync(client, HttpMethod.Post, "/api/action-proofs/cart");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("admission_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Testing", "http://localhost", false)]
    [InlineData("Testing", "https://localhost", true)]
    [InlineData("Production", "http://localhost", true)]
    public async Task SessionAndAdmissionCookies_UseSecureOnlyWhereRequired(
        string environment,
        string baseAddress,
        bool expectedSecure)
    {
        var settings = Settings();
        if (environment == "Production")
        {
            settings["DropShield:SyntheticClientIdentity:Enabled"] = "false";
            settings["DropShield:InternalMetrics:Enabled"] = "false";
        }

        using var factory = new DropShieldApiFactory(settings, environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            HandleCookies = false,
        });
        var response = await SendAsync(client, HttpMethod.Get, "/api/products/pokemon-etb/stock");
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedSecure, CookieHasSecure(cookies, "DropShield.Session"));
        Assert.Equal(expectedSecure, CookieHasSecure(cookies, "DropShield.Admission"));
    }

    private static async Task<string> GetActionProofAsync(HttpClient client, string action)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"/api/action-proofs/{action}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActionProofResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        string path,
        string actionProof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddIdentity(request);
        request.Headers.Add("X-DropShield-Action", actionProof);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        AddIdentity(request);
        return await client.SendAsync(request);
    }

    private static void AddIdentity(HttpRequestMessage request) =>
        request.Headers.Add("X-DropShield-Test-Client", "browser-cookie-client");

    private static bool CookieHasSecure(IEnumerable<string> cookies, string cookieName) =>
        cookies.Single(cookie => cookie.StartsWith($"{cookieName}=", StringComparison.Ordinal))
            .Contains("secure", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string?> Settings() => new()
    {
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
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };
}
