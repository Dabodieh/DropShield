using DropShield.Api.Admission;
using DropShield.Api.Options;
using DropShield.Tests.Support;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class AdmissionStateTests
{
    [Fact]
    public async Task CapacityAndBatch_AreAtomicUnderConcurrency()
    {
        var state = new InMemoryAdmissionState(TimeProvider.System);
        var attempts = Enumerable.Range(0, 20)
            .Select(index => state.EvaluateAsync(
                    Request($"session-{index}", maximumActive: 10, batchSize: 10),
                    CancellationToken.None)
                .AsTask());

        var decisions = await Task.WhenAll(attempts);

        Assert.Equal(10, decisions.Count(result => result.Status == AdmissionStatus.Admitted));
        Assert.Equal(10, decisions.Count(result => result.Status == AdmissionStatus.Waiting));
    }

    [Fact]
    public async Task ExpiredActiveSession_ReleasesCapacityForWaitingSession()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var state = new InMemoryAdmissionState(clock);
        var first = Request("first", sessionTtl: TimeSpan.FromSeconds(1));
        var waiting = Request("waiting", sessionTtl: TimeSpan.FromSeconds(1));

        Assert.Equal(
            AdmissionStatus.Admitted,
            (await state.EvaluateAsync(first, CancellationToken.None)).Status);
        Assert.Equal(
            AdmissionStatus.Waiting,
            (await state.EvaluateAsync(waiting, CancellationToken.None)).Status);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(
            AdmissionStatus.Admitted,
            (await state.EvaluateAsync(waiting, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task WaitingRoom_IsBoundedAndExpiredWaitersAreRemoved()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var state = new InMemoryAdmissionState(clock);
        var active = Request("active", maximumWaiting: 1);
        var waiting = Request(
            "waiting",
            maximumWaiting: 1,
            waitingTtl: TimeSpan.FromSeconds(1));
        var overflow = Request("overflow", maximumWaiting: 1);

        await state.EvaluateAsync(active, CancellationToken.None);
        Assert.Equal(
            AdmissionStatus.Waiting,
            (await state.EvaluateAsync(waiting, CancellationToken.None)).Status);
        Assert.Equal(
            AdmissionStatus.Full,
            (await state.EvaluateAsync(overflow, CancellationToken.None)).Status);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.NotEqual(
            AdmissionStatus.Full,
            (await state.EvaluateAsync(overflow, CancellationToken.None)).Status);
    }

    [Fact]
    public void RedisKeys_AreNamespacedAndDoNotExposeSessionIdentity()
    {
        const string rawSession = "private-session-value";
        var options = Options.Create(new DropShieldOptions
        {
            Redis = new RedisStateOptions
            {
                KeyPrefix = "dropshield:test",
                IdentityHashKey = "test-only-admission-hash-key-0001",
            },
        });
        var builder = new RedisAdmissionKeyBuilder(options);

        var keys = builder.Build("pokemon-etb");
        var hashedSession = builder.HashSession(rawSession);

        Assert.StartsWith(
            "dropshield:test:admission:{pokemon-etb}:",
            keys.Active.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(rawSession, hashedSession, StringComparison.Ordinal);
        Assert.Equal(64, hashedSession.Length);
        Assert.Equal(hashedSession, builder.HashSession(rawSession));
    }

    private static AdmissionRequest Request(
        string session,
        int maximumActive = 1,
        int batchSize = 1,
        int maximumWaiting = 10,
        TimeSpan? sessionTtl = null,
        TimeSpan? waitingTtl = null) => new(
            "pokemon-etb",
            session,
            maximumActive,
            batchSize,
            maximumWaiting,
            sessionTtl ?? TimeSpan.FromMinutes(5),
            waitingTtl ?? TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(1));
}
