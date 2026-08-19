using DropShield.Api.Behaviour;
using DropShield.Api.Options;
using DropShield.Api.State;
using DropShield.Tests.Support;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Tests;

public sealed class RedisBehaviourStateIntegrationTests
{
    [RedisFact]
    [Trait("Category", "RedisIntegration")]
    public async Task BehaviourEvidenceIsSharedPrivateAndExpiryDrivenAcrossInstances()
    {
        var connectionString = RedisTestEnvironment.ConnectionString;

        var keyPrefix = $"dropshield:test:{Guid.NewGuid():N}";
        var options = Options.Create(new DropShieldOptions
        {
            StateProvider = TrafficStateProvider.Redis,
            Redis = new RedisStateOptions
            {
                ConnectionString = connectionString,
                Database = 0,
                KeyPrefix = keyPrefix,
                IdentityHashKey = "redis-behaviour-identity-key-00001",
                ConnectTimeoutMilliseconds = 1_000,
                OperationTimeoutMilliseconds = 1_000,
            },
            BehaviourScoring = new BehaviourScoringOptions
            {
                Enabled = true,
                ObservationWindowSeconds = 1,
                StateTtlSeconds = 2,
                MaximumEventsPerActor = 16,
            },
        });
        await using var connectionA = new RedisConnectionProvider(options);
        await using var connectionB = new RedisConnectionProvider(options);
        var stateA = new RedisBehaviourState(connectionA, options);
        var stateB = new RedisBehaviourState(connectionB, options);
        const string actor = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? stateA : stateB)
                .RecordAsync(actor, BehaviourEventType.RateLimited, CancellationToken.None)
                .AsTask()));

        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var disposableConnection = connection;
        var server = connection.GetServer(connection.GetEndPoints().Single());
        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: $"*{keyPrefix}:behaviour:v1*"))
        {
            keys.Add(key);
        }

        var evidence = await stateB.GetAsync(actor, CancellationToken.None);
        Assert.Equal(16, evidence.RateLimited);
        Assert.Equal(16, await connection.GetDatabase().SortedSetLengthAsync(keys.Single()));
        Assert.Contains(
            BehaviourReasonCode.RateLimitHistory,
            BehaviourScoreEvaluator.Evaluate(evidence).Reasons);
        Assert.DoesNotContain(keys, key => key.ToString().Contains("raw-session", StringComparison.Ordinal));

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));

        Assert.Equal(BehaviourEvidence.Empty, await stateA.GetAsync(actor, CancellationToken.None));
    }
}
