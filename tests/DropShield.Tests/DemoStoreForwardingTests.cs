using System.Net;
using DropShield.Api.Origin;
using Microsoft.AspNetCore.Http;

namespace DropShield.Tests;

public sealed class DemoStoreForwardingTests
{
    [Fact]
    public async Task AdmissionAndSessionCookies_AreNotForwardedToDemoStore()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5058"),
        };
        var source = new DefaultHttpContext();
        source.Request.Headers.Cookie =
            "DropShield.Session=private-session; DropShield.Admission=private-token";
        source.Request.Headers["X-DropShield-Action"] = "private-action-token";
        var client = new DemoStoreClient(httpClient);

        using var response = await client.SendAsync(
            HttpMethod.Get,
            "/api/products/pokemon-etb/stock",
            source.Request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(handler.ForwardedCookie);
        Assert.Null(handler.ForwardedActionToken);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? ForwardedCookie { get; private set; }

        public string? ForwardedActionToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ForwardedCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join(";", values)
                : null;
            ForwardedActionToken = request.Headers.TryGetValues("X-DropShield-Action", out var actionValues)
                ? string.Join(";", actionValues)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
