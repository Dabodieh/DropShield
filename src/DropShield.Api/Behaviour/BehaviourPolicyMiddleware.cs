using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Behaviour;

public sealed class BehaviourPolicyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        BehaviourIdentityProvider identityProvider,
        IBehaviourState state,
        TrafficMetrics metrics,
        IOptions<DropShieldOptions> options,
        ILogger<BehaviourPolicyMiddleware> logger)
    {
        var configured = options.Value;
        if (!configured.BehaviourScoring.Enabled || !AppliesToTransaction(context.Request))
        {
            await next(context);
            return;
        }

        try
        {
            var evidence = await state.GetAsync(
                identityProvider.GetActor(context),
                context.RequestAborted);
            var score = BehaviourScoreEvaluator.Evaluate(evidence);
            metrics.RecordBehaviourScore(score.Level);
            if (score.Level != BehaviourRiskLevel.High)
            {
                await next(context);
                return;
            }

            metrics.RecordBehaviourRestriction();
            logger.LogDebug("High behavioural risk temporarily restricted for protected transaction");
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = configured.BehaviourScoring.RestrictionRetryAfterSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "behaviour_restricted",
                    "This transaction is temporarily restricted."),
                context.RequestAborted);
        }
        catch (BehaviourStateUnavailableException exception)
        {
            metrics.RecordBehaviourStateFailure();
            logger.LogWarning(exception, "Behaviour state unavailable; conservative policy allows transaction");
            await next(context);
        }
    }

    private static bool AppliesToTransaction(HttpRequest request) =>
        ActionProofPolicy.AppliesToMutation(request) ||
        (request.Method == HttpMethods.Post &&
         request.Path.StartsWithSegments("/api/action-proofs", StringComparison.Ordinal));
}
