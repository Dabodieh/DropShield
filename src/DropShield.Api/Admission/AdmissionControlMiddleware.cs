using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.State;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionControlMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        AdmissionEvaluator evaluator,
        TrafficMetrics metrics,
        IOptions<DropShieldOptions> options,
        ILogger<AdmissionControlMiddleware> logger)
    {
        if (!AdmissionPolicy.AppliesTo(context.Request, options.Value))
        {
            await next(context);
            return;
        }

        AdmissionDecision decision;
        try
        {
            decision = await evaluator.EvaluateAsync(context, context.RequestAborted);
        }
        catch (DistributedTrafficStateUnavailableException exception)
        {
            metrics.RecordStateFailure(TrafficRoute.Stock, isProtectedStock: true);
            logger.LogWarning(
                exception,
                "Admission state unavailable for protected drop; request denied");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "state_unavailable",
                    "Admission state is temporarily unavailable."),
                context.RequestAborted);
            return;
        }

        metrics.RecordAdmission(decision.Status);
        switch (decision.Status)
        {
            case AdmissionStatus.Admitted:
                await next(context);
                return;
            case AdmissionStatus.Waiting:
                await WriteWaitingAsync(context, options.Value.Admission, decision.RetryAfter);
                return;
            case AdmissionStatus.Full:
                logger.LogDebug("Bounded waiting room is full for configured protected drop");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(
                    new GatewayErrorResponse(
                        "waiting_room_full",
                        "The waiting room is temporarily full."),
                    context.RequestAborted);
                return;
            default:
                throw new InvalidOperationException("Unknown admission decision.");
        }
    }

    private static async Task WriteWaitingAsync(
        HttpContext context,
        AdmissionOptions options,
        TimeSpan retryAfter)
    {
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(
            new WaitingRoomResponse(
                "waiting",
                options.ProtectedProduct,
                retryAfterSeconds),
            context.RequestAborted);
    }
}
