namespace DropShield.Api.Traffic;

public sealed class RequestBodyTooLargeException(int maximumBytes)
    : Exception($"Request body exceeds the configured {maximumBytes}-byte limit.");

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
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (request.ContentLength is null or 0 && request.Headers.TransferEncoding.Count == 0)
        {
            return [];
        }

        if (request.ContentLength > maximumBytes)
        {
            throw new RequestBodyTooLargeException(maximumBytes);
        }

        request.EnableBuffering();
        await using var buffer = new MemoryStream((int)Math.Min(request.ContentLength ?? 0, maximumBytes));
        var chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    request.Body.Position = 0;
                    return buffer.ToArray();
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new RequestBodyTooLargeException(maximumBytes);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }
        }
    }
}
