using System.Globalization;
using DropShield.Api.Models;

namespace DropShield.Api.Traffic;

public static class RateLimitResponseWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        TimeSpan retryAfter,
        CancellationToken cancellationToken)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "rate_limited",
                "Too many requests. Please try again shortly."),
            cancellationToken);
    }
}
