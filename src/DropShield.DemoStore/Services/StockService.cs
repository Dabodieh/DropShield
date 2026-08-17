using DropShield.DemoStore.Options;
using Microsoft.Extensions.Options;

namespace DropShield.DemoStore.Services;

public sealed class StockService(
    IOptions<DemoStoreOptions> options,
    ILogger<StockService> logger)
{
    private readonly DemoStoreOptions _options = options.Value;

    public async Task<int> GetAvailableAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Looking up stock for product {ProductId} with simulated delay {DelayMilliseconds} ms",
            productId,
            _options.StockLookupDelayMilliseconds);

        await Task.Delay(_options.StockLookupDelayMilliseconds, cancellationToken);
        return _options.InitialAvailableStock;
    }
}

