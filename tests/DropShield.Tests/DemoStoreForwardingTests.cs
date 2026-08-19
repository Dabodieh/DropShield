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

    [Fact]
    public async Task RelativeQuery_IsPreservedForTheOrigin()
    {
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5058") };
        var source = new DefaultHttpContext();
        var client = new DemoStoreClient(httpClient);

        using var response = await client.SendAsync(
            HttpMethod.Get,
            "/api/products?x=1&y=2",
            source.Request,
            CancellationToken.None);

        Assert.Equal("/api/products?x=1&y=2", handler.ForwardedTarget);
    }

    [Fact]
    public async Task RemoteRedirect_IsReturnedWithoutSendingARemoteRequest()
    {
        var handler = new RedirectingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5058") };
        var source = new DefaultHttpContext();
        var client = new DemoStoreClient(httpClient);

        using var response = await client.SendAsync(
            HttpMethod.Get,
            "/api/products",
            source.Request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(new Uri("https://remote.example/"), response.Headers.Location);
        Assert.Equal(1, handler.SendCount);
        Assert.False(handler.RemoteDestinationRequested);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? ForwardedCookie { get; private set; }

        public string? ForwardedActionToken { get; private set; }

        public string? ForwardedTarget { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ForwardedTarget = request.RequestUri?.PathAndQuery;
            ForwardedCookie = request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join(";", values)
                : null;
            ForwardedActionToken = request.Headers.TryGetValues("X-DropShield-Action", out var actionValues)
                ? string.Join(";", actionValues)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RedirectingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        public bool RemoteDestinationRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            RemoteDestinationRequested |= request.RequestUri?.Host == "remote.example";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://remote.example/") },
            });
        }
    }
}
