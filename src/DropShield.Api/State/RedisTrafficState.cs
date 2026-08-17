using DropShield.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.State;

public sealed class RedisTrafficState(
    RedisConnectionProvider connectionProvider,
    RedisTrafficKeyBuilder keyBuilder,
    IOptions<DropShieldOptions> options)
    : IDistributedTrafficState
{
    private const string FixedWindowScript = """
        local now = redis.call('TIME')
        local nowMilliseconds = (tonumber(now[1]) * 1000) + math.floor(tonumber(now[2]) / 1000)
        local windowMilliseconds = tonumber(ARGV[1])
        local permitLimit = tonumber(ARGV[2])
        local windowId = math.floor(nowMilliseconds / windowMilliseconds)
        local storedWindow = redis.call('HGET', KEYS[1], 'window')
        local count

        if (not storedWindow) or (tonumber(storedWindow) ~= windowId) then
            count = 1
            redis.call('HSET', KEYS[1], 'window', windowId, 'count', count)
        else
            count = redis.call('HINCRBY', KEYS[1], 'count', 1)
        end

        local retryAfterMilliseconds = windowMilliseconds - (nowMilliseconds % windowMilliseconds)
        redis.call('PEXPIRE', KEYS[1], retryAfterMilliseconds + 1000)

        local acquired = 0
        if count <= permitLimit then
            acquired = 1
        end

        return { acquired, retryAfterMilliseconds }
        """;

    private readonly RedisStateOptions _options = options.Value.Redis;

    public async ValueTask<DistributedTrafficLease> TryAcquireAsync(
        DistributedTrafficRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var database = connection.GetDatabase(_options.Database);
            var resultTask = database.ScriptEvaluateAsync(
                FixedWindowScript,
                [keyBuilder.Build(request)],
                [(long)request.Window.TotalMilliseconds, request.PermitLimit]);
            var result = await resultTask.WaitAsync(cancellationToken);
            var values = (RedisResult[])result!;

            return new DistributedTrafficLease(
                (long)values[0] == 1,
                TimeSpan.FromMilliseconds(Math.Max(1, (long)values[1])));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new DistributedTrafficStateUnavailableException(
                "Redis traffic state is unavailable.",
                exception);
        }
    }

    public async ValueTask<DistributedStateHealth> GetHealthAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var pingTask = connection.GetDatabase(_options.Database).PingAsync();
            await pingTask.WaitAsync(cancellationToken);
            return new DistributedStateHealth(true, "available");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return new DistributedStateHealth(false, "unavailable");
        }
    }
}
