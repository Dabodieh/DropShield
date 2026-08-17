using DropShield.Api.Models;
using DropShield.Api.Traffic;

namespace DropShield.Api.Origin;

public sealed class DemoStoreForwarder(
    IDemoStoreClient client,
    TrafficMetrics metrics,
    ILogger<DemoStoreForwarder> logger)
{
    public async Task ForwardAsync(
        HttpContext context,
        TrafficRoute route,
        CancellationToken cancellationToken)
    {
        metrics.RecordForwarded(route);

        try
        {
            using var originResponse = await client.SendAsync(
                new HttpMethod(context.Request.Method),
                context.Request.Path,
                context.Request,
                cancellationToken);

            context.Response.StatusCode = (int)originResponse.StatusCode;
            if (originResponse.Content.Headers.ContentType is not null)
            {
                context.Response.ContentType = originResponse.Content.Headers.ContentType.ToString();
            }

            await originResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "DemoStore origin request failed for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
            await WriteBadGatewayAsync(context, cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "DemoStore origin request timed out for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
            await WriteBadGatewayAsync(context, cancellationToken);
        }
    }

    private static async Task WriteBadGatewayAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "upstream_unavailable",
                "The ecommerce origin is temporarily unavailable."),
            cancellationToken);
    }
}
