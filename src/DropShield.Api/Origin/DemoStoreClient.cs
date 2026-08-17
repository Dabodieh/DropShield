namespace DropShield.Api.Origin;

public sealed class DemoStoreClient(HttpClient httpClient) : IDemoStoreClient
{
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpRequest sourceRequest,
        CancellationToken cancellationToken,
        (string HeaderName, string Value)? originAssertionHeader = null)
    {
        using var request = new HttpRequestMessage(method, path);

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

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0;
}
