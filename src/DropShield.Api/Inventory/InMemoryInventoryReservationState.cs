using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Inventory;

public sealed class InMemoryInventoryReservationState(
    TimeProvider timeProvider,
    ReservationSessionHasher sessionHasher,
    IOptions<DropShieldOptions> options) : IInventoryReservationState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DropInventory> _drops = new(StringComparer.OrdinalIgnoreCase);
    private readonly InventoryReservationOptions _options = options.Value.InventoryReservation;

    public ValueTask<ReservationResult> TryReserveAsync(string drop, string sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(drop, sessionId, cancellationToken, (inventory, owner) =>
        {
            if (inventory.Reservations.ContainsKey(owner)) return ReservationStatus.Existing;
            if (inventory.Available == 0) return ReservationStatus.OutOfStock;
            if (inventory.Reservations.Count >= _options.MaximumInMemoryReservations)
                throw Unavailable();
            inventory.Available--;
            inventory.Reserved++;
            inventory.Reservations.Add(owner, timeProvider.GetUtcNow().AddSeconds(_options.ReservationTtlSeconds));
            return ReservationStatus.Reserved;
        });

    public ValueTask<ReservationResult> GetActiveAsync(string drop, string sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(drop, sessionId, cancellationToken, (inventory, owner) =>
            inventory.Reservations.ContainsKey(owner) ? ReservationStatus.Active : ReservationStatus.Missing);

    public ValueTask<ReservationResult> ReleaseAsync(string drop, string sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(drop, sessionId, cancellationToken, (inventory, owner) =>
        {
            if (!inventory.Reservations.Remove(owner)) return ReservationStatus.Missing;
            inventory.Reserved--;
            inventory.Available++;
            return ReservationStatus.Released;
        });

    public ValueTask<ReservationResult> CommitAsync(string drop, string sessionId, CancellationToken cancellationToken) =>
        ExecuteAsync(drop, sessionId, cancellationToken, (inventory, owner) =>
        {
            if (!inventory.Reservations.Remove(owner)) return ReservationStatus.Missing;
            inventory.Reserved--;
            inventory.Committed++;
            return ReservationStatus.Committed;
        });

    public ValueTask<InventorySnapshot> GetSnapshotAsync(string drop, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var inventory = GetOrCreate(drop);
            Prune(inventory);
            return ValueTask.FromResult(Snapshot(inventory));
        }
    }

    private ValueTask<ReservationResult> ExecuteAsync(
        string drop, string sessionId, CancellationToken cancellationToken,
        Func<DropInventory, string, ReservationStatus> operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var inventory = GetOrCreate(drop);
            var expired = Prune(inventory);
            var status = operation(inventory, sessionHasher.Hash(sessionId));
            return ValueTask.FromResult(new ReservationResult(status, Snapshot(inventory), expired));
        }
    }

    private DropInventory GetOrCreate(string drop)
    {
        if (_drops.TryGetValue(drop, out var inventory)) return inventory;
        inventory = new DropInventory(_options.InitialStock);
        _drops.Add(drop, inventory);
        return inventory;
    }

    private int Prune(DropInventory inventory)
    {
        var now = timeProvider.GetUtcNow();
        var expired = inventory.Reservations.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
        foreach (var owner in expired)
        {
            inventory.Reservations.Remove(owner);
            inventory.Reserved--;
            inventory.Available++;
        }
        return expired.Length;
    }

    private static InventorySnapshot Snapshot(DropInventory inventory) =>
        new(inventory.Available, inventory.Reserved, inventory.Committed);

    private static InventoryReservationStateUnavailableException Unavailable() => new(
        "In-memory inventory reservation capacity is exhausted.",
        new InvalidOperationException("Reservation capacity reached."));

    private sealed class DropInventory(int initialStock)
    {
        public int Available { get; set; } = initialStock;
        public int Reserved { get; set; }
        public int Committed { get; set; }
        public Dictionary<string, DateTimeOffset> Reservations { get; } = new(StringComparer.Ordinal);
    }
}
