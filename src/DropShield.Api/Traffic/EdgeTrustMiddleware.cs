using System.Security.Cryptography;
using System.Text;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Traffic;

/// <summary>
/// Optional edge-authentication gate for a fronting reverse proxy/CDN (see
/// integrations/fastly). Independently rejects a missing or incorrect shared-key header rather
/// than trusting that the fronting edge stripped a client-forged value — the header check here
/// holds even if DropShield.Api is reached directly. A client-supplied value is always removed
/// before this check runs, so a request cannot supply its own trust header.
/// </summary>
public sealed class EdgeTrustMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<DropShieldOptions> options)
    {
        var edge = options.Value.EdgeTrust;
        if (!edge.Enabled)
        {
            await next(context);
            return;
        }

        var presented = context.Request.Headers.TryGetValue(edge.HeaderName, out var value)
            ? value.ToString()
            : null;
        context.Request.Headers.Remove(edge.HeaderName);

        if (presented is null || !IsValidKey(presented, edge.SharedKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "edge_trust_required",
                message = "This request did not present a valid edge trust credential.",
            });
            return;
        }

        await next(context);
    }

    private static bool IsValidKey(string presented, string expected)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}
