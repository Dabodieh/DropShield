namespace DropShield.Api.Origin;

public sealed class DemoStoreClient(HttpClient httpClient) : IDemoStoreClient
{
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken,
        (string HeaderName, string Value)? originAssertionHeader = null,
        OriginForwardingProfile profile = OriginForwardingProfile.DemoStore)
    {
        if (!Uri.TryCreate(path, UriKind.Relative, out var relativePath) ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Origin forwarding requires a rooted relative request target.");
        }

        using var request = new HttpRequestMessage(method, relativePath);

        if (method != HttpMethod.Get && HasBody(sourceRequest))
        {
            request.Content = new StreamContent(sourceRequest.Body);
            if (!string.IsNullOrWhiteSpace(sourceRequest.ContentType))
            {
                request.Content.Headers.TryAddWithoutValidation(
                    "Content-Type",
                    sourceRequest.ContentType);
            }
        }

        if (originAssertionHeader is { } header)
        {
            request.Headers.TryAddWithoutValidation(header.HeaderName, header.Value);
        }

        if (profile == OriginForwardingProfile.AdobeCommerce)
        {
            CopyCommerceHeaders(sourceRequest, request);
        }

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0;

    private static void CopyCommerceHeaders(HttpRequest source, HttpRequestMessage destination)
    {
        foreach (var name in new[] { "Accept", "Accept-Language", "Store", "X-Magento-Store", "X-Requested-With" })
        {
            if (source.Headers.TryGetValue(name, out var values))
            {
                destination.Headers.TryAddWithoutValidation(name, values.ToArray());
            }
        }

        var cookies = source.Cookies
            .Where(cookie => !cookie.Key.StartsWith("DropShield.", StringComparison.Ordinal))
            .Select(cookie => $"{cookie.Key}={cookie.Value}")
            .ToArray();
        if (cookies.Length > 0)
        {
            destination.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
        }
    }
}
