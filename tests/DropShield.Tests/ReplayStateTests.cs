using DropShield.Api.Actions;
using DropShield.Api.Options;
using DropShield.Tests.Support;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class ReplayStateTests
{
    [Fact]
    public async Task InMemoryReplayState_IsAtomicBoundedAndExpiryDriven()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var options = Options.Create(new DropShieldOptions
        {
            ActionProofs = new ActionProofOptions { MaximumInMemoryMarkers = 1 },
        });
        var state = new InMemoryReplayState(clock, options);

        var first = await state.TryConsumeAsync("one", TimeSpan.FromSeconds(2), CancellationToken.None);
        var replay = await state.TryConsumeAsync("one", TimeSpan.FromSeconds(2), CancellationToken.None);
        var capacity = await Assert.ThrowsAsync<ReplayStateUnavailableException>(async () =>
            await state.TryConsumeAsync("two", TimeSpan.FromSeconds(2), CancellationToken.None));

        clock.Advance(TimeSpan.FromSeconds(3));
        var afterExpiry = await state.TryConsumeAsync("two", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(first.IsConsumed);
        Assert.False(replay.IsConsumed);
        Assert.NotNull(capacity);
        Assert.True(afterExpiry.IsConsumed);
    }
}
