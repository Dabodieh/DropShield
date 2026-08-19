using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DropShield.Api.Origin;
using Microsoft.AspNetCore.Http;

namespace DropShield.Tests.Support;

internal sealed class RecordingDemoStoreClient : IDemoStoreClient
{
    private readonly ConcurrentDictionary<string, int> _requestCounts = new();

    public bool ThrowOnSend { get; set; }

    public int TotalRequests => _requestCounts.Values.Sum();

    public (string HeaderName, string Value)? LastOriginAssertionHeader { get; private set; }

    public byte[]? LastForwardedBody { get; private set; }

    public string? LastForwardedPath { get; private set; }

    public IReadOnlyDictionary<string, string[]> NextResponseHeaders { get; set; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public int GetRequestCount(string path) =>
        _requestCounts.GetValueOrDefault(path);

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken,
        (string HeaderName, string Value)? originAssertionHeader = null,
        OriginForwardingProfile profile = OriginForwardingProfile.DemoStore)
    {
        LastOriginAssertionHeader = originAssertionHeader;
        LastForwardedPath = path;
        LastForwardedBody = await CaptureBodyAsync(sourceRequest, cancellationToken);
        if (ThrowOnSend)
        {
            throw new HttpRequestException("Synthetic origin failure.");
        }

        _requestCounts.AddOrUpdate(path, 1, (_, count) => count + 1);

        var response = (method.Method, path.Split('?', 2)[0]) switch
        {
            ("GET", "/api/products") => Json(
                HttpStatusCode.OK,
                """[{"id":"pokemon-etb","name":"Pokémon Elite Trainer Box","price":49.99,"currency":"GBP"}]"""),
            ("GET", "/api/products/pokemon-etb") => Json(
                HttpStatusCode.OK,
                """{"id":"pokemon-etb","name":"Pokémon Elite Trainer Box","price":49.99,"currency":"GBP"}"""),
            ("GET", "/api/products/pokemon-etb/stock") => Json(
                HttpStatusCode.OK,
                """{"productId":"pokemon-etb","available":500}"""),
            ("POST", "/api/cart") or ("POST", "/api/checkout") => Json(
                HttpStatusCode.Accepted,
                """{"status":"accepted"}"""),
            ("POST", "/graphql") => Json(
                HttpStatusCode.OK,
                """{"data":{"addProductsToCart":{"cart":{"items":[]}}}}"""),
            ("POST", "/checkout/cart/add") => Json(
                HttpStatusCode.Accepted,
                """{"status":"accepted"}"""),
            ("POST", var commercePath) when commercePath.StartsWith(
                "/rest/V1/guest-carts/", StringComparison.Ordinal) || commercePath.StartsWith(
                "/rest/default/V1/guest-carts/", StringComparison.Ordinal) => Json(
                HttpStatusCode.Accepted,
                """{"status":"accepted"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        foreach (var header in NextResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }

    private static async Task<byte[]?> CaptureBodyAsync(
        HttpRequest sourceRequest,
        CancellationToken cancellationToken)
    {
        if (sourceRequest.ContentLength is null or 0 && sourceRequest.Headers.TransferEncoding.Count == 0)
        {
            return null;
        }

        sourceRequest.EnableBuffering();
        using var buffer = new MemoryStream();
        await sourceRequest.Body.CopyToAsync(buffer, cancellationToken);
        sourceRequest.Body.Position = 0;
        return buffer.ToArray();
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
