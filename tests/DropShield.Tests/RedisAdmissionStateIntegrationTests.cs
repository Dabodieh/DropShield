using DropShield.Api.Admission;
using DropShield.Api.Options;
using DropShield.Api.State;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class RedisAdmissionStateIntegrationTests
{
    [Fact]
    [Trait("Category", "RedisIntegration")]
    public async Task Admission_IsAtomicPrivateAndExpiryDrivenAcrossInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "DROPSHIELD_REDIS_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = Options.Create(new DropShieldOptions
        {
            StateProvider = TrafficStateProvider.Redis,
            Redis = new RedisStateOptions
            {
                ConnectionString = connectionString,
                Database = 0,
                KeyPrefix = $"dropshield:test:{Guid.NewGuid():N}",
                IdentityHashKey = "redis-admission-identity-key-00001",
                ConnectTimeoutMilliseconds = 1_000,
                OperationTimeoutMilliseconds = 1_000,
            },
        });
        await using var connectionA = new RedisConnectionProvider(options);
        await using var connectionB = new RedisConnectionProvider(options);
        var keyBuilder = new RedisAdmissionKeyBuilder(options);
        var stateA = new RedisAdmissionState(connectionA, keyBuilder, options);
        var stateB = new RedisAdmissionState(
            connectionB,
            new RedisAdmissionKeyBuilder(options),
            options);
        var attempts = Enumerable.Range(0, 20)
            .Select(index => new
            {
                Session = $"raw-session-{index}",
                Task = (index % 2 == 0 ? stateA : stateB)
                    .EvaluateAsync(Request($"raw-session-{index}"), CancellationToken.None)
                    .AsTask(),
            })
            .ToArray();

        var results = await Task.WhenAll(attempts.Select(async attempt => new
        {
            attempt.Session,
            Decision = await attempt.Task,
        }));

        var admitted = results.Where(attempt =>
            attempt.Decision.Status == AdmissionStatus.Admitted).ToArray();
        var waiting = results.Where(attempt =>
            attempt.Decision.Status == AdmissionStatus.Waiting).ToArray();
        var connection = await connectionA.GetConnectionAsync(CancellationToken.None);
        var database = connection.GetDatabase(options.Value.Redis.Database);
        var keys = keyBuilder.Build("pokemon-etb");
        var activeMembers = await database.SortedSetRangeByRankAsync(keys.Active);
        var waitingMembers = await database.SortedSetRangeByRankAsync(keys.WaitingOrder);
        var activeTtl = await database.KeyTimeToLiveAsync(keys.Active);
        var waitingTtl = await database.KeyTimeToLiveAsync(keys.WaitingOrder);

        Assert.Equal(10, admitted.Length);
        Assert.Equal(10, waiting.Length);
        Assert.Equal(10, activeMembers.Length);
        Assert.Equal(10, waitingMembers.Length);
        Assert.DoesNotContain(
            activeMembers.Concat(waitingMembers),
            member => member.ToString().Contains("raw-session", StringComparison.Ordinal));
        Assert.NotNull(activeTtl);
        Assert.NotNull(waitingTtl);
        Assert.InRange(activeTtl.Value, TimeSpan.Zero, TimeSpan.FromSeconds(2.1));
        Assert.InRange(waitingTtl.Value, TimeSpan.Zero, TimeSpan.FromSeconds(3.1));

        await Task.Delay(TimeSpan.FromMilliseconds(1_250));

        var promoted = await stateB.EvaluateAsync(
            Request(waiting[0].Session),
            CancellationToken.None);
        Assert.Equal(AdmissionStatus.Admitted, promoted.Status);
    }

    private static AdmissionRequest Request(string session) => new(
        "pokemon-etb",
        session,
        MaximumActiveSessions: 10,
        AdmissionBatchSize: 10,
        MaximumWaitingSessions: 20,
        SessionTtl: TimeSpan.FromSeconds(1),
        WaitingTtl: TimeSpan.FromSeconds(2),
        RetryAfter: TimeSpan.FromSeconds(1));
}
