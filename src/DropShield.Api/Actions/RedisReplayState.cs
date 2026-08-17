using DropShield.Api.Options;
using DropShield.Api.State;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Actions;

public sealed class RedisReplayState(
    RedisConnectionProvider connectionProvider,
    IOptions<DropShieldOptions> options)
    : IReplayState
{
    private readonly RedisStateOptions _options = options.Value.Redis;

    public async ValueTask<ReplayConsumeResult> TryConsumeAsync(
        string replayKey,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var database = connection.GetDatabase(_options.Database);
            var wasSet = await database.StringSetAsync(
                    $"{_options.KeyPrefix}:replay:{replayKey}",
                    "1",
                    timeToLive,
                    When.NotExists)
                .WaitAsync(cancellationToken);
            return wasSet ? ReplayConsumeResult.Consumed : ReplayConsumeResult.AlreadyConsumed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new ReplayStateUnavailableException("Redis replay state is unavailable.", exception);
        }
    }
}
