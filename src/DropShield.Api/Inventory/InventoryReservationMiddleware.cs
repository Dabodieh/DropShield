using DropShield.Api.Actions;
using DropShield.Api.Models;
using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Inventory;

public sealed class InventoryReservationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IInventoryReservationState state,
        IOptions<DropShieldOptions> options,
        TrafficMetrics metrics,
        ILogger<InventoryReservationMiddleware> logger)
    {
        var configured = options.Value;
        if (!configured.InventoryReservation.Enabled ||
            !ActionProofPolicy.AppliesToMutation(context))
        {
            await next(context);
            return;
        }

        if (!context.Items.TryGetValue(ActionProofMiddleware.AuthorizedSessionItemKey, out var value) ||
            value is not string sessionId)
        {
            await next(context);
            return;
        }

        var isCart = ActionProofPolicy.GetMutationAction(context) == ActionKind.Cart;
        ReservationResult reservation;
        try
        {
            reservation = isCart
                ? await state.TryReserveAsync(configured.Admission.ProtectedProduct, sessionId, context.RequestAborted)
                : await state.GetActiveAsync(configured.Admission.ProtectedProduct, sessionId, context.RequestAborted);
            metrics.RecordReservation(reservation.Status, reservation.ExpiredReservations);
        }
        catch (InventoryReservationStateUnavailableException exception)
        {
            metrics.RecordReservationStateFailure();
            logger.LogWarning(exception, "Inventory reservation state unavailable");
            await StateUnavailable(context);
            return;
        }

        if (isCart && reservation.Status == ReservationStatus.OutOfStock)
        {
            await Error(context, "out_of_stock", "Synthetic inventory is unavailable.");
            return;
        }

        if (isCart && reservation.Status == ReservationStatus.Existing)
        {
            await Error(context, "reservation_exists", "This session already has an active reservation.");
            return;
        }

        if (!isCart && reservation.Status != ReservationStatus.Active)
        {
            logger.LogDebug("Checkout attempted without active reservation");
            await Error(context, "reservation_required", "An active reservation is required.");
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            if (isCart && reservation.Status == ReservationStatus.Reserved && context.Response.StatusCode >= 400)
            {
                var released = await state.ReleaseAsync(
                    configured.Admission.ProtectedProduct,
                    sessionId,
                    context.RequestAborted);
                metrics.RecordReservation(released.Status, released.ExpiredReservations);
            }

            if (!isCart && context.Response.StatusCode is >= 200 and < 300)
            {
                var committed = await state.CommitAsync(
                    configured.Admission.ProtectedProduct,
                    sessionId,
                    context.RequestAborted);
                metrics.RecordReservation(committed.Status, committed.ExpiredReservations);
                if (committed.Status != ReservationStatus.Committed)
                {
                    metrics.RecordReservationStateFailure();
                    await WriteBufferedStateUnavailable(context, buffer);
                }
            }

            buffer.Position = 0;
            context.Response.Body = original;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        catch (InventoryReservationStateUnavailableException exception)
        {
            metrics.RecordReservationStateFailure();
            logger.LogWarning(exception, "Inventory reservation transition failed");
            buffer.SetLength(0);
            context.Response.Body = original;
            await StateUnavailable(context);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private static Task Error(HttpContext context, string error, string message)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(error, message),
            context.RequestAborted);
    }

    private static Task StateUnavailable(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "state_unavailable",
                "Inventory reservation state is temporarily unavailable."),
            context.RequestAborted);
    }

    private static async Task WriteBufferedStateUnavailable(
        HttpContext context,
        MemoryStream buffer)
    {
        buffer.SetLength(0);
        context.Response.Headers.ContentLength = null;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(
            new GatewayErrorResponse(
                "state_unavailable",
                "Inventory reservation state is temporarily unavailable."),
            context.RequestAborted);
    }
}
