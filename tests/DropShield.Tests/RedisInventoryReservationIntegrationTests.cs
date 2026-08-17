using DropShield.Api.Inventory;
using DropShield.Api.Options;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace DropShield.Tests;

public sealed class RedisInventoryReservationIntegrationTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task ReservationsAreAtomicPrivateAndExpiryDrivenAcrossInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("DROPSHIELD_REDIS_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var keyPrefix = $"dropshield:test:{Guid.NewGuid():N}";
        var settings = Settings(connectionString, keyPrefix);
        using var factoryA = new DropShieldApiFactory(settings);
        using var factoryB = new DropShieldApiFactory(settings);
        using var clientA = factoryA.CreateClient(new WebApplicationFactoryClientOptions());
        using var clientB = factoryB.CreateClient(new WebApplicationFactoryClientOptions());
        var stateA = factoryA.Services.GetRequiredService<IInventoryReservationState>();
        var stateB = factoryB.Services.GetRequiredService<IInventoryReservationState>();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? stateA : stateB)
                .TryReserveAsync("pokemon-etb", $"raw-session-{index}", CancellationToken.None)
                .AsTask()));

        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var disposableConnection = connection;
        var server = connection.GetServer(connection.GetEndPoints().Single());
        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: $"*{keyPrefix}:inventory:pokemon-etb*"))
        {
            keys.Add(key);
        }

        Assert.Equal(10, results.Count(result => result.Status == ReservationStatus.Reserved));
        Assert.Equal(10, results.Count(result => result.Status == ReservationStatus.OutOfStock));
        Assert.Equal(new InventorySnapshot(0, 10, 0), await stateB.GetSnapshotAsync("pokemon-etb", CancellationToken.None));
        Assert.DoesNotContain(keys, key => key.ToString().Contains("raw-session", StringComparison.Ordinal));

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));

        Assert.Equal(new InventorySnapshot(10, 0, 0), await stateA.GetSnapshotAsync("pokemon-etb", CancellationToken.None));
    }

    private static Dictionary<string, string?> Settings(string connectionString, string keyPrefix) => new()
    {
        ["DropShield:StateProvider"] = "Redis",
        ["DropShield:Redis:ConnectionString"] = connectionString,
        ["DropShield:Redis:KeyPrefix"] = keyPrefix,
        ["DropShield:Redis:IdentityHashKey"] = "redis-inventory-identity-key-00001",
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:MaximumActiveSessions"] = "30",
        ["DropShield:Admission:AdmissionBatchSize"] = "30",
        ["DropShield:Admission:WaitingTtlSeconds"] = "300",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:AdmissionTokens:Enabled"] = "true",
        ["DropShield:AdmissionTokens:SigningKey"] = SigningKey,
        ["DropShield:ActionProofs:Enabled"] = "true",
        ["DropShield:ActionProofs:LifetimeSeconds"] = "30",
        ["DropShield:InventoryReservation:Enabled"] = "true",
        ["DropShield:InventoryReservation:InitialStock"] = "10",
        ["DropShield:InventoryReservation:ReservationTtlSeconds"] = "1",
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };
}
