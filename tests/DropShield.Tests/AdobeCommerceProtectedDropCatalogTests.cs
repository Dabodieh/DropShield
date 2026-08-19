using System.Net;
using System.Text;
using DropShield.Api.Catalog;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

/// <summary>
/// A successfully authenticated manifest that explicitly reports "no active drop" is a known
/// state, not an unknown one. Regression coverage for the diff-review finding that this was
/// previously conflated with "never loaded"/"stale", causing ordinary Commerce cart-add traffic
/// to fail closed (503) purely because no protected drop was currently enabled.
/// </summary>
public sealed class AdobeCommerceProtectedDropCatalogTests
{
    [Fact]
    public async Task RefreshAsync_ManifestWithNoActiveDrop_IsStillUsable()
    {
        using var catalog = CreateCatalog(new StaticHandler("""{"version":1,"active_drop":null}"""), out _);

        await catalog.RefreshAsync(CancellationToken.None);

        Assert.True(catalog.Status.HasLoaded);
        Assert.True(catalog.Status.IsUsable);
        Assert.Null(catalog.GetActiveDrop());
        Assert.Null(catalog.Status.ActiveDropId);
    }

    [Fact]
    public async Task RefreshAsync_NeverSucceeded_IsNotUsable()
    {
        using var catalog = CreateCatalog(new StaticHandler(null, HttpStatusCode.ServiceUnavailable), out _);

        await catalog.RefreshAsync(CancellationToken.None);

        Assert.False(catalog.Status.HasLoaded);
        Assert.False(catalog.Status.IsUsable);
    }

    [Fact]
    public async Task RefreshAsync_StaleAfterSuccessfulLoad_BecomesUnusable()
    {
        var time = new TestTimeProviderLocal(DateTimeOffset.UtcNow);
        using var catalog = CreateCatalog(
            new StaticHandler("""{"version":1,"active_drop":null}"""), out _, time, staleAfterSeconds: 5);

        await catalog.RefreshAsync(CancellationToken.None);
        Assert.True(catalog.Status.IsUsable);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(catalog.Status.IsUsable);
    }

    private static AdobeCommerceProtectedDropCatalog CreateCatalog(
        HttpMessageHandler handler,
        out HttpClient client,
        TestTimeProviderLocal? time = null,
        int staleAfterSeconds = 300)
    {
        client = new HttpClient(handler) { BaseAddress = new Uri("https://commerce.local") };
        var factory = new SingleClientFactory(client);
        var options = Microsoft.Extensions.Options.Options.Create(new DropShieldOptions
        {
            AdobeCommerce = new AdobeCommerceOptions
            {
                ProtectionManifest = new ProtectionManifestOptions
                {
                    AccessToken = "test-token",
                    StaleAfterSeconds = staleAfterSeconds,
                },
            },
        });
        return new AdobeCommerceProtectedDropCatalog(
            factory,
            options,
            time ?? new TestTimeProviderLocal(DateTimeOffset.UtcNow),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AdobeCommerceProtectedDropCatalog>.Instance);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticHandler(string? body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class TestTimeProviderLocal(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _current = initial;
        public override DateTimeOffset GetUtcNow() => _current;
        public void Advance(TimeSpan duration) => _current += duration;
    }
}
