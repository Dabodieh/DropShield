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

    public int GetRequestCount(string path) =>
        _requestCounts.GetValueOrDefault(path);

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken)
    {
        if (ThrowOnSend)
        {
            throw new HttpRequestException("Synthetic origin failure.");
        }

        _requestCounts.AddOrUpdate(path, 1, (_, count) => count + 1);

        var response = (method.Method, path) switch
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
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        return Task.FromResult(response);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
