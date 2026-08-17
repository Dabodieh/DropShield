namespace DropShield.Api.Traffic;

/// <summary>
/// Reads and buffers a request body so it can be inspected more than once (route
/// classification, origin-assertion body hashing, forwarding) without consuming the stream.
/// Every caller sees the exact same bytes — nothing here parses, reserializes, or otherwise
/// transforms the body.
/// </summary>
public static class RequestBodyReader
{
    public static async Task<byte[]> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is null or 0 && request.Headers.TransferEncoding.Count == 0)
        {
            return [];
        }

        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;
        return buffer.ToArray();
    }
}
