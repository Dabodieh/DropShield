using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DropShield.Demo;

/// <summary>
/// Acts as a browser-capable DropShield client for one synthetic shopper identity. Cookies stay
/// in a CookieContainer, so normal path, HttpOnly, Secure, and expiry rules determine transport.
/// </summary>
public sealed class DropShieldClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;
    private readonly string _identity;

    public DropShieldClient(Uri baseUri, string identity)
    {
        _identity = identity;
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = _cookies,
            UseCookies = true,
        })
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(10),
        };
    }


    public async Task<HttpResponseMessage> GetStockAsync(string productId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}/stock");
        AttachIdentity(request);
        return await _http.SendAsync(request, cancellationToken);
    }

    public async Task<ActionProofOutcome> RequestActionProofAsync(string action, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        AttachIdentity(request);
        var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ActionProofOutcome(false, null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<ActionProofPayload>(Json, cancellationToken);
        return new ActionProofOutcome(true, payload?.Token, response.StatusCode);
    }

    public async Task<HttpResponseMessage> PostCartAsync(
        string productId,
        int quantity,
        string actionToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart")
        {
            Content = JsonContent.Create(new { productId, quantity }, options: Json),
        };
        AttachIdentity(request);
        request.Headers.Add("X-DropShield-Action", actionToken);
        return await _http.SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostCheckoutAsync(
        string productId,
        string actionToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { productId }, options: Json),
        };
        AttachIdentity(request);
        request.Headers.Add("X-DropShield-Action", actionToken);
        return await _http.SendAsync(request, cancellationToken);
    }

    public bool HasAdmissionProof => _cookies.GetCookies(_http.BaseAddress!)
        .Cast<Cookie>()
        .Any(cookie => string.Equals(cookie.Name, "DropShield.Admission", StringComparison.Ordinal));

    public void Dispose() => _http.Dispose();

    private void AttachIdentity(HttpRequestMessage request)
    {
        request.Headers.Add("X-DropShield-Test-Client", _identity);
    }

    private sealed record ActionProofPayload(string Action, string Token, int ExpiresInSeconds);
}

public sealed record ActionProofOutcome(bool IsSuccess, string? Token, System.Net.HttpStatusCode StatusCode);
