using DropShield.Api.Options;
using DropShield.Api.State;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Admission;

public sealed class RedisAdmissionState(
    RedisConnectionProvider connectionProvider,
    RedisAdmissionKeyBuilder keyBuilder,
    IOptions<DropShieldOptions> options)
    : IAdmissionState
{
    private const string AdmissionScript = """
        local now = redis.call('TIME')
        local nowMilliseconds = (tonumber(now[1]) * 1000) + math.floor(tonumber(now[2]) / 1000)
        local session = ARGV[1]
        local maximumActive = tonumber(ARGV[2])
        local batchSize = tonumber(ARGV[3])
        local maximumWaiting = tonumber(ARGV[4])
        local sessionTtl = tonumber(ARGV[5])
        local waitingTtl = tonumber(ARGV[6])
        local batchWindow = tonumber(ARGV[7])
        local cleanupAllowance = 1000

        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', nowMilliseconds)

        local expiredWaiters = redis.call('ZRANGEBYSCORE', KEYS[3], '-inf', nowMilliseconds)
        for _, expiredSession in ipairs(expiredWaiters) do
            redis.call('ZREM', KEYS[2], expiredSession)
            redis.call('ZREM', KEYS[3], expiredSession)
        end

        if redis.call('ZSCORE', KEYS[1], session) then
            redis.call('ZADD', KEYS[1], nowMilliseconds + sessionTtl, session)
            redis.call('PEXPIRE', KEYS[1], sessionTtl + cleanupAllowance)
            return { 1, 0 }
        end

        if not redis.call('ZSCORE', KEYS[2], session) then
            if redis.call('ZCARD', KEYS[2]) >= maximumWaiting then
                return { 3, batchWindow }
            end

            local sequence = redis.call('INCR', KEYS[4])
            redis.call('ZADD', KEYS[2], sequence, session)
        end

        redis.call('ZADD', KEYS[3], nowMilliseconds + waitingTtl, session)

        local windowId = math.floor(nowMilliseconds / batchWindow)
        local storedWindow = redis.call('HGET', KEYS[5], 'window')
        local admittedInWindow = 0
        if (not storedWindow) or (tonumber(storedWindow) ~= windowId) then
            redis.call('HSET', KEYS[5], 'window', windowId, 'count', 0)
        else
            admittedInWindow = tonumber(redis.call('HGET', KEYS[5], 'count') or '0')
        end

        local availableCapacity = maximumActive - redis.call('ZCARD', KEYS[1])
        local availableBatch = batchSize - admittedInWindow
        local eligibleCount = math.min(availableCapacity, availableBatch)
        local rank = tonumber(redis.call('ZRANK', KEYS[2], session))

        if eligibleCount > 0 and rank < eligibleCount then
            redis.call('ZREM', KEYS[2], session)
            redis.call('ZREM', KEYS[3], session)
            redis.call('ZADD', KEYS[1], nowMilliseconds + sessionTtl, session)
            redis.call('HINCRBY', KEYS[5], 'count', 1)
            redis.call('PEXPIRE', KEYS[1], sessionTtl + cleanupAllowance)
            redis.call('PEXPIRE', KEYS[5], batchWindow + cleanupAllowance)
            redis.call('PEXPIRE', KEYS[2], waitingTtl + cleanupAllowance)
            redis.call('PEXPIRE', KEYS[3], waitingTtl + cleanupAllowance)
            redis.call('PEXPIRE', KEYS[4], waitingTtl + cleanupAllowance)
            return { 1, 0 }
        end

        redis.call('PEXPIRE', KEYS[2], waitingTtl + cleanupAllowance)
        redis.call('PEXPIRE', KEYS[3], waitingTtl + cleanupAllowance)
        redis.call('PEXPIRE', KEYS[4], waitingTtl + cleanupAllowance)
        redis.call('PEXPIRE', KEYS[5], batchWindow + cleanupAllowance)
        return { 2, batchWindow }
        """;

    private readonly RedisStateOptions _options = options.Value.Redis;

    public async ValueTask<AdmissionDecision> EvaluateAsync(
        AdmissionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            var database = connection.GetDatabase(_options.Database);
            var keys = keyBuilder.Build(request.Drop);
            var resultTask = database.ScriptEvaluateAsync(
                AdmissionScript,
                keys.ToArray(),
                [
                    keyBuilder.HashSession(request.SessionId),
                    request.MaximumActiveSessions,
                    request.AdmissionBatchSize,
                    request.MaximumWaitingSessions,
                    (long)request.SessionTtl.TotalMilliseconds,
                    (long)request.WaitingTtl.TotalMilliseconds,
                    (long)request.RetryAfter.TotalMilliseconds,
                ]);
            var result = await resultTask.WaitAsync(cancellationToken);
            var values = (RedisResult[])result!;
            var status = (long)values[0] switch
            {
                1 => AdmissionStatus.Admitted,
                2 => AdmissionStatus.Waiting,
                3 => AdmissionStatus.Full,
                _ => throw new InvalidOperationException("Redis returned an invalid admission status."),
            };

            return new AdmissionDecision(
                status,
                TimeSpan.FromMilliseconds(Math.Max(0, (long)values[1])));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            throw new DistributedTrafficStateUnavailableException(
                "Redis admission state is unavailable.",
                exception);
        }
    }
}
