using System.Threading.RateLimiting;
using DropShield.Api.Admission;
using DropShield.Api.Actions;
using DropShield.Api.Behaviour;
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
            var isProtectedStock = TrafficRouteClassifier.IsProtectedStockRequest(
                httpContext.Request,
                options.ProtectedProducts);
            var reason = route switch
            {
                TrafficRoute.Cart or TrafficRoute.Checkout or TrafficRoute.ActionProof
                    or TrafficRoute.StorefrontCartAdd or TrafficRoute.GraphQlCartAdd
                    or TrafficRoute.CommerceRestCart or TrafficRoute.CommerceRestCheckout =>
                    RateLimitReason.PerClient,
                TrafficRoute.Stock when isProtectedStock =>
                    RateLimitReason.ProtectedStockChained,
                _ => RateLimitReason.Unattributed,
            };
            httpContext.RequestServices.GetRequiredService<TrafficMetrics>()
                .RecordRateLimited(route, isProtectedStock, reason);
            await httpContext.RequestServices.GetRequiredService<BehaviourActivityRecorder>()
                .RecordAsync(
                    httpContext,
                    BehaviourEventType.RateLimited,
                    CancellationToken.None);

            var retryAfter = TimeSpan.FromSeconds(1);
            if (rejectionContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var leaseRetryAfter))
            {
                retryAfter = leaseRetryAfter;
            }

            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DropShield.TrafficPolicy");
            logger.LogDebug(
                "Rate limited {Method} {TrafficRoute} with {RateLimitReason}",
                httpContext.Request.Method,
                route,
                reason);

            await RateLimitResponseWriter.WriteAsync(
                httpContext,
                retryAfter,
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
        var policy = GetClientPolicy(route, context, options);
        if (policy is null || !policy.Enabled)
        {
            return RateLimitPartition.GetNoLimiter($"unlimited:{route}");
        }

        var clientIdentity = context.RequestServices
            .GetRequiredService<ClientIdentityProvider>()
            .GetPartitionKey(context);
        var partitionRoute = route == TrafficRoute.ActionProof &&
                             ActionProofPolicy.TryGetAction(context.Request, out var action)
            ? action == ActionKind.Cart ? TrafficRoute.Cart : TrafficRoute.Checkout
            : route;
        var partitionKey = $"{partitionRoute}:{clientIdentity}";

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
            AdmissionPolicy.AppliesTo(context.Request, options) ||
            !TrafficRouteClassifier.IsProtectedStockRequest(
                context.Request,
                options.ProtectedProducts))
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
        HttpContext context,
        DropShieldOptions options)
    {
        var request = context.Request;
        return route switch
        {
            TrafficRoute.Stock when TrafficRouteClassifier.IsProtectedStockRequest(
                request,
                options.ProtectedProducts) =>
                options.Policies.Stock,
            TrafficRoute.Cart => options.Policies.Cart,
            TrafficRoute.Checkout => options.Policies.Checkout,
            TrafficRoute.CommerceRestCart => options.Policies.Cart,
            TrafficRoute.CommerceRestCheckout => options.Policies.Checkout,
            TrafficRoute.StorefrontCartAdd => options.Policies.Cart,
            TrafficRoute.GraphQlCartAdd when context.Features.Get<TrafficRequestObservation>()
                    ?.IsProtectedGraphQlCartMutation ?? false =>
                options.Policies.Cart,
            TrafficRoute.ActionProof when ActionProofPolicy.TryGetAction(request, out var action) =>
                action == ActionKind.Cart ? options.Policies.Cart : options.Policies.Checkout,
            _ => null,
        };
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
