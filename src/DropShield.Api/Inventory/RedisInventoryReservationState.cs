using DropShield.Api.Options;
using DropShield.Api.State;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Inventory;

public sealed class RedisInventoryReservationState(
    RedisConnectionProvider connectionProvider,
    ReservationSessionHasher sessionHasher,
    IOptions<DropShieldOptions> options) : IInventoryReservationState
{
    private const string Script = """
        local redisTime = redis.call('TIME')
        local now = redisTime[1] * 1000 + math.floor(redisTime[2] / 1000)
        local initial = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])
        local owner = ARGV[3]
        local operation = ARGV[4]
        if redis.call('EXISTS', KEYS[1]) == 0 then redis.call('HSET', KEYS[1], 'available', initial, 'reserved', 0, 'committed', 0) end
        local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', now)
        for _, value in ipairs(expired) do redis.call('ZREM', KEYS[2], value); redis.call('HINCRBY', KEYS[1], 'available', 1); redis.call('HINCRBY', KEYS[1], 'reserved', -1) end
        local status = 0
        local active = redis.call('ZSCORE', KEYS[2], owner)
        if operation == 'reserve' then
          if active then status = 1
          elseif tonumber(redis.call('HGET', KEYS[1], 'available')) <= 0 then status = 5
          else redis.call('HINCRBY', KEYS[1], 'available', -1); redis.call('HINCRBY', KEYS[1], 'reserved', 1); redis.call('ZADD', KEYS[2], now + ttl, owner); status = 0 end
        elseif operation == 'get' then status = active and 2 or 6
        elseif operation == 'release' then
          if active then redis.call('ZREM', KEYS[2], owner); redis.call('HINCRBY', KEYS[1], 'available', 1); redis.call('HINCRBY', KEYS[1], 'reserved', -1); status = 3 else status = 6 end
        elseif operation == 'commit' then
          if active then redis.call('ZREM', KEYS[2], owner); redis.call('HINCRBY', KEYS[1], 'reserved', -1); redis.call('HINCRBY', KEYS[1], 'committed', 1); status = 4 else status = 6 end
        end
        local available = tonumber(redis.call('HGET', KEYS[1], 'available')); local reserved = tonumber(redis.call('HGET', KEYS[1], 'reserved')); local committed = tonumber(redis.call('HGET', KEYS[1], 'committed'))
        return { status, available, reserved, committed, #expired }
        """;
    private readonly DropShieldOptions _options = options.Value;

    public ValueTask<ReservationResult> TryReserveAsync(string drop, string sessionId, CancellationToken cancellationToken) => Execute(drop, sessionId, "reserve", cancellationToken);
    public ValueTask<ReservationResult> GetActiveAsync(string drop, string sessionId, CancellationToken cancellationToken) => Execute(drop, sessionId, "get", cancellationToken);
    public ValueTask<ReservationResult> ReleaseAsync(string drop, string sessionId, CancellationToken cancellationToken) => Execute(drop, sessionId, "release", cancellationToken);
    public ValueTask<ReservationResult> CommitAsync(string drop, string sessionId, CancellationToken cancellationToken) => Execute(drop, sessionId, "commit", cancellationToken);

    public async ValueTask<InventorySnapshot> GetSnapshotAsync(string drop, CancellationToken cancellationToken)
    {
        var result = await Execute(drop, string.Empty, "get", cancellationToken);
        return result.Inventory;
    }

    private async ValueTask<ReservationResult> Execute(string drop, string sessionId, string operation, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var database = connection.GetDatabase(_options.Redis.Database);
            var prefix = $"{{{_options.Redis.KeyPrefix}:inventory:{drop}}}";
            var result = await database.ScriptEvaluateAsync(Script,
                [$"{prefix}:state", $"{prefix}:expires"],
                [_options.InventoryReservation.InitialStock,
                 (long)TimeSpan.FromSeconds(_options.InventoryReservation.ReservationTtlSeconds).TotalMilliseconds,
                 sessionHasher.Hash(sessionId), operation]).WaitAsync(cancellationToken);
            var values = (RedisResult[])result!;
            return new ReservationResult((ReservationStatus)(long)values[0], new((int)(long)values[1], (int)(long)values[2], (int)(long)values[3]), (int)(long)values[4]);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        { throw new InventoryReservationStateUnavailableException("Redis inventory reservation state is unavailable.", exception); }
    }
}
