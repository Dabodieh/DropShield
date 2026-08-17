using System.Globalization;
using System.Threading.RateLimiting;
using DropShield.Api.Models;
using DropShield.Api.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace DropShield.Api.Traffic;

public static class TrafficPolicy
{
    public static void Configure(
        RateLimiterOptions rateLimiterOptions,
        DropShieldOptions options)
    {
        var perClientLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            CreateClientPartition(context, options));

        var aggregateStockLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            CreateAggregateStockPartition(context, options));

        rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            perClientLimiter,
            aggregateStockLimiter);

        rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        rateLimiterOptions.OnRejected = async (rejectionContext, cancellationToken) =>
        {
            var httpContext = rejectionContext.HttpContext;
            var route = TrafficRouteClassifier.Classify(httpContext.Request);
            httpContext.RequestServices.GetRequiredService<TrafficMetrics>()
                .RecordRateLimited(route);

            var retryAfterSeconds = 1;
            if (rejectionContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            }

            httpContext.Response.Headers.RetryAfter =
                retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DropShield.TrafficPolicy");
            logger.LogDebug(
                "Rate limited {Method} {Path} as {TrafficRoute}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                route);

            await httpContext.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "rate_limited",
                    "Too many requests. Please try again shortly."),
                cancellationToken);
        };
    }

    private static RateLimitPartition<string> CreateClientPartition(
        HttpContext context,
        DropShieldOptions options)
    {
        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        var route = TrafficRouteClassifier.Classify(context.Request);
        var policy = GetClientPolicy(route, context.Request, options);
        if (policy is null || !policy.Enabled)
        {
            return RateLimitPartition.GetNoLimiter($"unlimited:{route}");
        }

        var clientIdentity = context.RequestServices
            .GetRequiredService<ClientIdentityProvider>()
            .GetPartitionKey(context);
        var partitionKey = $"{route}:{clientIdentity}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => CreateFixedWindowOptions(
                policy.ClientPermitLimit,
                policy.ClientWindowSeconds));
    }

    private static RateLimitPartition<string> CreateAggregateStockPartition(
        HttpContext context,
        DropShieldOptions options)
    {
        var stockPolicy = options.Policies.Stock;
        if (!options.Enabled ||
            !stockPolicy.Enabled ||
            !IsProtectedStockRequest(context.Request, options))
        {
            return RateLimitPartition.GetNoLimiter("aggregate-unlimited");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            "protected-stock:aggregate",
            _ => CreateFixedWindowOptions(
                stockPolicy.AggregatePermitLimit,
                stockPolicy.AggregateWindowSeconds));
    }

    private static ClientPolicyOptions? GetClientPolicy(
        TrafficRoute route,
        HttpRequest request,
        DropShieldOptions options) => route switch
        {
            TrafficRoute.Stock when IsProtectedStockRequest(request, options) =>
                options.Policies.Stock,
            TrafficRoute.Cart => options.Policies.Cart,
            TrafficRoute.Checkout => options.Policies.Checkout,
            _ => null,
        };

    private static bool IsProtectedStockRequest(
        HttpRequest request,
        DropShieldOptions options)
    {
        if (TrafficRouteClassifier.Classify(request) != TrafficRoute.Stock)
        {
            return false;
        }

        var productId = TrafficRouteClassifier.GetProductId(request);
        return productId is not null &&
               options.ProtectedProducts.Contains(productId, StringComparer.OrdinalIgnoreCase);
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(
        int permitLimit,
        int windowSeconds) => new()
        {
            AutoReplenishment = true,
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
}
