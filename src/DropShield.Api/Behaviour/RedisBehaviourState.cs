using System.Security.Cryptography;
using DropShield.Api.Options;
using DropShield.Api.State;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Behaviour;

public sealed class RedisBehaviourState(
    RedisConnectionProvider connectionProvider,
    IOptions<DropShieldOptions> options) : IBehaviourState
{
    private const string Script = """
        local redisTime = redis.call('TIME')
        local now = redisTime[1] * 1000 + math.floor(redisTime[2] / 1000)
        local cutoff = now - tonumber(ARGV[1])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', cutoff)
        if ARGV[3] ~= '' then
          redis.call('ZADD', KEYS[1], now, ARGV[3])
          local maximumEvents = tonumber(ARGV[4])
          local eventCount = redis.call('ZCARD', KEYS[1])
          if eventCount > maximumEvents then
            redis.call('ZREMRANGEBYRANK', KEYS[1], 0, eventCount - maximumEvents - 1)
          end
          redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[2]))
        end
        local events = redis.call('ZRANGEBYSCORE', KEYS[1], cutoff, '+inf')
        local requestCount = 0
        local stockCount = 0
        local rateLimitedCount = 0
        local replayCount = 0
        local invalidProofCount = 0
        local transactionCount = 0
        for _, value in ipairs(events) do
          local kind = string.sub(value, 1, 1)
          if kind == 'R' then requestCount = requestCount + 1
          elseif kind == 'S' then stockCount = stockCount + 1
          elseif kind == 'L' then rateLimitedCount = rateLimitedCount + 1
          elseif kind == 'Y' then replayCount = replayCount + 1
          elseif kind == 'I' then invalidProofCount = invalidProofCount + 1
          elseif kind == 'T' then transactionCount = transactionCount + 1 end
        end
        return { requestCount, stockCount, rateLimitedCount, replayCount, invalidProofCount, transactionCount }
        """;

    private readonly DropShieldOptions _options = options.Value;

    public ValueTask<BehaviourEvidence> RecordAsync(
        string actor,
        BehaviourEventType eventType,
        CancellationToken cancellationToken) =>
        ExecuteAsync(actor, $"{ToCode(eventType)}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}", cancellationToken);

    public ValueTask<BehaviourEvidence> GetAsync(string actor, CancellationToken cancellationToken) =>
        ExecuteAsync(actor, string.Empty, cancellationToken);

    private async ValueTask<BehaviourEvidence> ExecuteAsync(
        string actor,
        string eventMember,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var database = connection.GetDatabase(_options.Redis.Database);
            var key = $"{{{_options.Redis.KeyPrefix}:behaviour:v1:{actor}}}:events";
            var result = await database.ScriptEvaluateAsync(
                Script,
                [key],
                [
                    (long)TimeSpan.FromSeconds(_options.BehaviourScoring.ObservationWindowSeconds).TotalMilliseconds,
                    (long)TimeSpan.FromSeconds(_options.BehaviourScoring.StateTtlSeconds).TotalMilliseconds,
                    eventMember,
                    _options.BehaviourScoring.MaximumEventsPerActor,
                ]).WaitAsync(cancellationToken);
            var values = (RedisResult[])result!;
            return new BehaviourEvidence(
                (int)(long)values[0],
                (int)(long)values[1],
                (int)(long)values[2],
                (int)(long)values[3],
                (int)(long)values[4],
                (int)(long)values[5]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new BehaviourStateUnavailableException(
                "Redis behavioural state is unavailable.",
                exception);
        }
    }

    private static char ToCode(BehaviourEventType eventType) => eventType switch
    {
        BehaviourEventType.Request => 'R',
        BehaviourEventType.StockRequest => 'S',
        BehaviourEventType.RateLimited => 'L',
        BehaviourEventType.ReplayRejected => 'Y',
        BehaviourEventType.InvalidProof => 'I',
        BehaviourEventType.Transaction => 'T',
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
    };
}
