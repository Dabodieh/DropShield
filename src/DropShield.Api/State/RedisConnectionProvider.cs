using DropShield.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.State;

public sealed class RedisConnectionProvider(IOptions<DropShieldOptions> options)
    : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly RedisStateOptions _options = options.Value.Redis;
    private Task<ConnectionMultiplexer>? _connectionTask;

    public async ValueTask<IConnectionMultiplexer> GetConnectionAsync(
        CancellationToken cancellationToken)
    {
        Task<ConnectionMultiplexer> connectionTask;
        lock (_sync)
        {
            _connectionTask ??= ConnectAsync();
            connectionTask = _connectionTask;
        }

        try
        {
            return await connectionTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (connectionTask.IsFaulted)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_connectionTask, connectionTask))
                    {
                        _connectionTask = null;
                    }
                }
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<ConnectionMultiplexer>? connectionTask;
        lock (_sync)
        {
            connectionTask = _connectionTask;
            _connectionTask = null;
        }

        if (connectionTask is null)
        {
            return;
        }

        try
        {
            var connection = await connectionTask;
            await connection.CloseAsync();
            connection.Dispose();
        }
        catch (RedisException)
        {
            // Nothing remains to close after a failed connection attempt.
        }
    }

    private Task<ConnectionMultiplexer> ConnectAsync()
    {
        var configuration = ConfigurationOptions.Parse(_options.ConnectionString);
        configuration.AbortOnConnectFail = false;
        configuration.AllowAdmin = false;
        configuration.ClientName = "DropShield";
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = _options.ConnectTimeoutMilliseconds;
        configuration.AsyncTimeout = _options.OperationTimeoutMilliseconds;
        configuration.SyncTimeout = _options.OperationTimeoutMilliseconds;
        return ConnectionMultiplexer.ConnectAsync(configuration);
    }
}
