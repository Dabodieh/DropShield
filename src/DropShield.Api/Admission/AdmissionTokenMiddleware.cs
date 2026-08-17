using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Admission;

public sealed class AdmissionTokenMiddleware(RequestDelegate next)
{
    public const string ValidatedTokenItemKey = "DropShield.AdmissionToken.Validated";

    public async Task InvokeAsync(
        HttpContext context,
        AdmissionSessionProvider sessionProvider,
        IAdmissionTokenService tokenService,
        AdmissionTokenCookieManager cookies,
        TrafficMetrics metrics,
        IOptions<DropShieldOptions> options,
        ILogger<AdmissionTokenMiddleware> logger)
    {
        var configuredOptions = options.Value;
        if (!AdmissionPolicy.AppliesTo(context.Request, configuredOptions) ||
            !configuredOptions.AdmissionTokens.Enabled)
        {
            await next(context);
            return;
        }

        var sessionId = sessionProvider.GetOrCreate(context);
        if (!context.Request.Cookies.TryGetValue(
                configuredOptions.AdmissionTokens.CookieName,
                out var token) || string.IsNullOrWhiteSpace(token))
        {
            await next(context);
            return;
        }

        var validation = tokenService.Validate(
            token,
            configuredOptions.Admission.ProtectedProduct,
            sessionId);
        metrics.RecordAdmissionTokenValidation(validation);
        if (validation.IsValid)
        {
            context.Items[ValidatedTokenItemKey] = true;
            await next(context);
            return;
        }

        cookies.Delete(context);
        logger.LogDebug(
            "Admission token validation failed with fixed category {AdmissionTokenFailure}",
            validation.Failure);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "admission_required",
                "Admission is required for this protected drop."),
            context.RequestAborted);
    }
}
