namespace DropShield.Api.Inventory;

public interface IInventoryReservationState
{
    ValueTask<ReservationResult> TryReserveAsync(string drop, string sessionId, CancellationToken cancellationToken);

    ValueTask<ReservationResult> GetActiveAsync(string drop, string sessionId, CancellationToken cancellationToken);

    ValueTask<ReservationResult> ReleaseAsync(string drop, string sessionId, CancellationToken cancellationToken);

    ValueTask<ReservationResult> CommitAsync(string drop, string sessionId, CancellationToken cancellationToken);

    ValueTask<InventorySnapshot> GetSnapshotAsync(string drop, CancellationToken cancellationToken);
}

public sealed record ReservationResult(ReservationStatus Status, InventorySnapshot Inventory, int ExpiredReservations = 0);

public sealed record InventorySnapshot(int Available, int Reserved, int Committed)
{
    public static InventorySnapshot Empty { get; } = new(0, 0, 0);
}

public enum ReservationStatus
{
    Reserved,
    Existing,
    Active,
    Released,
    Committed,
    OutOfStock,
    Missing,
}

public sealed class InventoryReservationStateUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
