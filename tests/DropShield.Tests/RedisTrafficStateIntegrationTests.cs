using DropShield.Api.Options;
using DropShield.Api.Actions;
using DropShield.Api.State;
using DropShield.Tests.Support;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class RedisTrafficStateIntegrationTests
{
    [RedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task ReplayConsumption_IsAtomicAcrossInstancesAndKeyExpires()
    {
        var connectionString = RedisTestEnvironment.ConnectionString;

        var options = Options.Create(new DropShieldOptions
        {
            StateProvider = TrafficStateProvider.Redis,
            Redis = new RedisStateOptions
            {
                ConnectionString = connectionString,
                Database = 0,
                KeyPrefix = $"dropshield:test:{Guid.NewGuid():N}",
                IdentityHashKey = "redis-integration-identity-key-001",
                ConnectTimeoutMilliseconds = 1_000,
                OperationTimeoutMilliseconds = 1_000,
            },
        });
        await using var connectionA = new RedisConnectionProvider(options);
        await using var connectionB = new RedisConnectionProvider(options);
        var stateA = new RedisReplayState(connectionA, options);
        var stateB = new RedisReplayState(connectionB, options);
        const string replayKey = "derived-replay-key-for-test";
        var expiry = TimeSpan.FromSeconds(2);
        var redis = await connectionA.GetConnectionAsync(CancellationToken.None);
        var database = redis.GetDatabase(options.Value.Redis.Database);
        var key = $"{options.Value.Redis.KeyPrefix}:replay:{replayKey}";

        var attempts = Enumerable.Range(0, 20)
            .Select(index => (index % 2 == 0 ? stateA : stateB)
                .TryConsumeAsync(replayKey, expiry, CancellationToken.None)
                .AsTask());
        var results = await Task.WhenAll(attempts);
        var timeToLive = await database.KeyTimeToLiveAsync(key);

        Assert.Equal(1, results.Count(result => result.IsConsumed));
        Assert.Equal(19, results.Count(result => !result.IsConsumed));
        Assert.NotNull(timeToLive);
        Assert.InRange(timeToLive.Value, TimeSpan.Zero, TimeSpan.FromSeconds(2.1));

        await Task.Delay(timeToLive.Value + TimeSpan.FromMilliseconds(250));
        Assert.False(await database.KeyExistsAsync(key));
    }

    [RedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task FixedWindow_IsAtomicAcrossInstancesAndKeyExpires()
    {
        var connectionString = RedisTestEnvironment.ConnectionString;

        var options = Options.Create(new DropShieldOptions
        {
            StateProvider = TrafficStateProvider.Redis,
            Redis = new RedisStateOptions
            {
                ConnectionString = connectionString,
                Database = 0,
                KeyPrefix = $"dropshield:test:{Guid.NewGuid():N}",
                IdentityHashKey = "redis-integration-identity-key-001",
                ConnectTimeoutMilliseconds = 1_000,
                OperationTimeoutMilliseconds = 1_000,
            },
        });
        await using var connectionA = new RedisConnectionProvider(options);
        await using var connectionB = new RedisConnectionProvider(options);
        var keyBuilderA = new RedisTrafficKeyBuilder(options);
        var stateA = new RedisTrafficState(connectionA, keyBuilderA, options);
        var stateB = new RedisTrafficState(
            connectionB,
            new RedisTrafficKeyBuilder(options),
            options);
        var request = new DistributedTrafficRequest(
            TrafficPolicyKind.Stock,
            TrafficLimitScope.Aggregate,
            ClientPartition: null,
            PermitLimit: 10,
            Window: TimeSpan.FromSeconds(2));
        var redis = await connectionA.GetConnectionAsync(CancellationToken.None);
        var database = redis.GetDatabase(options.Value.Redis.Database);
        var key = keyBuilderA.Build(request);

        var probe = await stateA.TryAcquireAsync(request, CancellationToken.None);
        await database.KeyDeleteAsync(key);
        if (probe.RetryAfter < TimeSpan.FromMilliseconds(500))
        {
            await Task.Delay(probe.RetryAfter + TimeSpan.FromMilliseconds(100));
        }

        var attempts = Enumerable.Range(0, 20)
            .Select(index => (index % 2 == 0 ? stateA : stateB)
                .TryAcquireAsync(request, CancellationToken.None)
                .AsTask());
        var leases = await Task.WhenAll(attempts);
        var timeToLive = await database.KeyTimeToLiveAsync(key);

        Assert.Equal(10, leases.Count(lease => lease.IsAcquired));
        Assert.Equal(10, leases.Count(lease => !lease.IsAcquired));
        Assert.NotNull(timeToLive);
        Assert.InRange(timeToLive.Value, TimeSpan.Zero, TimeSpan.FromSeconds(3.1));
        Assert.True((await stateA.GetHealthAsync(CancellationToken.None)).IsAvailable);
        Assert.True((await stateB.GetHealthAsync(CancellationToken.None)).IsAvailable);

        await Task.Delay(timeToLive.Value + TimeSpan.FromMilliseconds(250));
        Assert.False(await database.KeyExistsAsync(key));
    }
}
