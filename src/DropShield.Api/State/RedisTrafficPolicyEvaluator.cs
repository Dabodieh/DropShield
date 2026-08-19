using DropShield.Api.Admission;
using DropShield.Api.Actions;
using DropShield.Api.Options;
using DropShield.Api.Catalog;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.State;

public sealed class RedisTrafficPolicyEvaluator(
    IDistributedTrafficState state,
    ClientIdentityProvider identityProvider,
    IProtectedDropCatalog catalog,
    IOptions<DropShieldOptions> options)
{
    private readonly DropShieldOptions _options = options.Value;

    public async ValueTask<RedisTrafficPolicyDecision> EvaluateAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return RedisTrafficPolicyDecision.Allowed;
        }

        var route = TrafficRouteClassifier.Classify(context.Request);
        return route switch
        {
            TrafficRoute.Stock when IsProtectedStock(context) =>
                await EvaluateProtectedStockAsync(context, cancellationToken),
            TrafficRoute.Cart => await EvaluateClientPolicyAsync(
                context,
                TrafficPolicyKind.Cart,
                _options.Policies.Cart,
                cancellationToken),
            TrafficRoute.Checkout => await EvaluateClientPolicyAsync(
                context,
                TrafficPolicyKind.Checkout,
                _options.Policies.Checkout,
                cancellationToken),
            TrafficRoute.StorefrontCartAdd => await EvaluateClientPolicyAsync(
                context,
                TrafficPolicyKind.Cart,
                _options.Policies.Cart,
                cancellationToken),
            TrafficRoute.GraphQlCartAdd when context.Features.Get<TrafficRequestObservation>()
                    ?.IsProtectedGraphQlCartMutation ?? false =>
                await EvaluateClientPolicyAsync(
                    context,
                    TrafficPolicyKind.Cart,
                    _options.Policies.Cart,
                    cancellationToken),
            TrafficRoute.ActionProof when ActionProofPolicy.TryGetAction(
                context.Request,
                out var action) => await EvaluateClientPolicyAsync(
                context,
                action == ActionKind.Cart ? TrafficPolicyKind.Cart : TrafficPolicyKind.Checkout,
                action == ActionKind.Cart ? _options.Policies.Cart : _options.Policies.Checkout,
                cancellationToken),
            _ => RedisTrafficPolicyDecision.Allowed,
        };
    }

    private async ValueTask<RedisTrafficPolicyDecision> EvaluateProtectedStockAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var policy = _options.Policies.Stock;
        if (!policy.Enabled)
        {
            return RedisTrafficPolicyDecision.Allowed;
        }

        var clientDecision = await EvaluateClientPolicyAsync(
            context,
            TrafficPolicyKind.Stock,
            policy,
            cancellationToken);
        if (!clientDecision.IsAllowed)
        {
            return clientDecision;
        }

        if (AdmissionPolicy.AppliesTo(context.Request, _options, catalog))
        {
            return RedisTrafficPolicyDecision.Allowed;
        }

        var aggregateLease = await state.TryAcquireAsync(
            new DistributedTrafficRequest(
                TrafficPolicyKind.Stock,
                TrafficLimitScope.Aggregate,
                ClientPartition: null,
                policy.AggregatePermitLimit,
                TimeSpan.FromSeconds(policy.AggregateWindowSeconds)),
            cancellationToken);

        return aggregateLease.IsAcquired
            ? RedisTrafficPolicyDecision.Allowed
            : new RedisTrafficPolicyDecision(
                false,
                RateLimitReason.Aggregate,
                aggregateLease.RetryAfter);
    }

    private bool IsProtectedStock(HttpContext context) =>
        TrafficRouteClassifier.GetProductId(context.Request) is { } productId &&
        catalog.TryResolveSku(productId, out _);

    private async ValueTask<RedisTrafficPolicyDecision> EvaluateClientPolicyAsync(
        HttpContext context,
        TrafficPolicyKind policyKind,
        ClientPolicyOptions policy,
        CancellationToken cancellationToken)
    {
        if (!policy.Enabled)
        {
            return RedisTrafficPolicyDecision.Allowed;
        }

        var lease = await state.TryAcquireAsync(
            new DistributedTrafficRequest(
                policyKind,
                TrafficLimitScope.PerClient,
                identityProvider.GetPartitionKey(context),
                policy.ClientPermitLimit,
                TimeSpan.FromSeconds(policy.ClientWindowSeconds)),
            cancellationToken);

        return lease.IsAcquired
            ? RedisTrafficPolicyDecision.Allowed
            : new RedisTrafficPolicyDecision(
                false,
                RateLimitReason.PerClient,
                lease.RetryAfter);
    }
}

public sealed record RedisTrafficPolicyDecision(
    bool IsAllowed,
    RateLimitReason Reason,
    TimeSpan RetryAfter)
{
    public static RedisTrafficPolicyDecision Allowed { get; } = new(
        true,
        RateLimitReason.Unattributed,
        TimeSpan.Zero);
}
