using DropShield.Api.Models;
using DropShield.Api.Behaviour;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Actions;

public sealed class ActionProofMiddleware(RequestDelegate next)
{
    public const string AuthorizedSessionItemKey = "DropShield.ActionProof.Session";
    public async Task InvokeAsync(
        HttpContext context,
        AdmissionProofAuthorizer admissionAuthorizer,
        IActionTokenService tokenService,
        IReplayState replayState,
        BehaviourActivityRecorder behaviourRecorder,
        TrafficMetrics metrics,
        IOptions<DropShieldOptions> options,
        ILogger<ActionProofMiddleware> logger)
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.ActionProofs.Enabled ||
            !ActionProofPolicy.AppliesToMutation(context.Request))
        {
            await next(context);
            return;
        }

        var admission = await admissionAuthorizer.AuthorizeAsync(context, context.RequestAborted);
        if (admission.IsStateUnavailable)
        {
            await WriteStateUnavailableAsync(context);
            return;
        }

        if (!admission.IsAuthorized)
        {
            await WriteAdmissionRequiredAsync(context);
            return;
        }
        context.Items[AuthorizedSessionItemKey] = admission.SessionId!;

        var action = ActionProofPolicy.GetMutationAction(context.Request);
        if (!context.Request.Headers.TryGetValue(
                configuredOptions.ActionProofs.HeaderName,
                out var tokenValues) || tokenValues.Count != 1)
        {
            await behaviourRecorder.RecordAsync(
                context,
                BehaviourEventType.InvalidProof,
                CancellationToken.None);
            await WriteActionRequiredAsync(context);
            return;
        }

        var validation = tokenService.Validate(
            tokenValues[0]!,
            configuredOptions.Admission.ProtectedProduct,
            admission.SessionId!,
            action);
        metrics.RecordActionTokenValidation(validation);
        if (!validation.IsValid)
        {
            await behaviourRecorder.RecordAsync(
                context,
                BehaviourEventType.InvalidProof,
                CancellationToken.None);
            logger.LogDebug(
                "Action token validation failed with fixed category {ActionTokenFailure}",
                validation.Failure);
            await WriteActionRequiredAsync(context);
            return;
        }

        ReplayConsumeResult consumption;
        try
        {
            consumption = await replayState.TryConsumeAsync(
                validation.ReplayKey!,
                validation.RemainingLifetime + TimeSpan.FromSeconds(
                    configuredOptions.ActionProofs.ReplayTtlMarginSeconds),
                context.RequestAborted);
        }
        catch (ReplayStateUnavailableException exception)
        {
            metrics.RecordReplayStateUnavailable();
            metrics.RecordStateFailure(TrafficRouteClassifier.Classify(context.Request), false);
            logger.LogWarning(exception, "Replay state unavailable; protected action denied");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "state_unavailable",
                    "Replay state is temporarily unavailable."),
                context.RequestAborted);
            return;
        }

        if (!consumption.IsConsumed)
        {
            await behaviourRecorder.RecordAsync(
                context,
                BehaviourEventType.ReplayRejected,
                CancellationToken.None);
            metrics.RecordReplayRejected();
            logger.LogDebug("Action replay rejected for protected {ActionKind}", action);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(
                new GatewayErrorResponse(
                    "action_already_used",
                    "This action proof has already been used."),
                context.RequestAborted);
            return;
        }

        metrics.RecordActionConsumed(action);
        await next(context);
    }

    private static Task WriteAdmissionRequiredAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "admission_required",
                "Admission is required for this protected drop."),
            context.RequestAborted);
    }

    private static Task WriteStateUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "state_unavailable",
                "Admission state is temporarily unavailable."),
            context.RequestAborted);
    }

    private static Task WriteActionRequiredAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "action_authorization_required",
                "A valid action proof is required."),
            context.RequestAborted);
    }
}
