using System.Net.Http.Json;
using System.Text.Json;

namespace DropShield.Demo;

/// <summary>
/// Acts as a real DropShield client for one synthetic shopper identity: tracks the session and
/// admission-proof cookies by hand (mirroring tests/DropShield.Tests/ActionProofTests.cs)
/// instead of relying on CookieContainer, because the admission-proof cookie is deliberately
/// scoped to the stock path and would not otherwise be sent on cart/checkout/action-proof
/// requests. Never logs a cookie or token value; callers only see whether calls succeeded.
/// </summary>
public sealed class DropShieldClient(HttpClient http, string identity)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? _sessionCookie;
    private string? _admissionCookie;

    public async Task<HttpResponseMessage> GetStockAsync(string productId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}/stock");
        AttachIdentity(request, includeAdmission: false);
        var response = await http.SendAsync(request, cancellationToken);
        CaptureCookies(response);
        return response;
    }

    public async Task<ActionProofOutcome> RequestActionProofAsync(string action, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/action-proofs/{action}");
        AttachIdentity(request, includeAdmission: true);
        var response = await http.SendAsync(request, cancellationToken);
        CaptureCookies(response);

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
        AttachIdentity(request, includeAdmission: true);
        request.Headers.Add("X-DropShield-Action", actionToken);
        var response = await http.SendAsync(request, cancellationToken);
        CaptureCookies(response);
        return response;
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
        AttachIdentity(request, includeAdmission: true);
        request.Headers.Add("X-DropShield-Action", actionToken);
        var response = await http.SendAsync(request, cancellationToken);
        CaptureCookies(response);
        return response;
    }

    public bool HasAdmissionProof => _admissionCookie is not null;

    private void AttachIdentity(HttpRequestMessage request, bool includeAdmission)
    {
        request.Headers.Add("X-DropShield-Test-Client", identity);
        if (_sessionCookie is null)
        {
            return;
        }

        var cookie = $"DropShield.Session={_sessionCookie}";
        if (includeAdmission && _admissionCookie is not null)
        {
            cookie += $"; DropShield.Admission={_admissionCookie}";
        }

        request.Headers.Add("Cookie", cookie);
    }

    private void CaptureCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            if (value.StartsWith("DropShield.Session=", StringComparison.Ordinal))
            {
                _sessionCookie = ExtractCookieValue(value);
            }
            else if (value.StartsWith("DropShield.Admission=", StringComparison.Ordinal))
            {
                _admissionCookie = ExtractCookieValue(value);
            }
        }
    }

    private static string ExtractCookieValue(string setCookieHeader)
    {
        var firstSegment = setCookieHeader.Split(';', 2)[0];
        var separatorIndex = firstSegment.IndexOf('=');
        return firstSegment[(separatorIndex + 1)..];
    }

    private sealed record ActionProofPayload(string Action, string Token, int ExpiresInSeconds);
}

public sealed record ActionProofOutcome(bool IsSuccess, string? Token, System.Net.HttpStatusCode StatusCode);
