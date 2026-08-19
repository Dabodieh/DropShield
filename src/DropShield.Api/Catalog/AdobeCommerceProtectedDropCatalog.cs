using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Catalog;

/// <summary>
/// Periodically fetches the Commerce-owned manifest. Readers observe either a complete old
/// snapshot or a complete new snapshot; refresh failures never erase last-known-good data.
/// </summary>
public sealed partial class AdobeCommerceProtectedDropCatalog(
    IHttpClientFactory clientFactory,
    IOptions<DropShieldOptions> options,
    TimeProvider timeProvider,
    ILogger<AdobeCommerceProtectedDropCatalog> logger) : BackgroundService, IProtectedDropCatalog
{
    private readonly HttpClient _client = clientFactory.CreateClient("CommerceProtectionManifest");
    private readonly ProtectionManifestOptions _settings = options.Value.AdobeCommerce.ProtectionManifest;
    private readonly object _gate = new();
    private ProtectedDropSnapshot? _snapshot;
    private long _lastSuccessUtcTicks = -1;
    private int _failures;

    // A successfully authenticated manifest that explicitly reports "no active drop" is a known
    // state, not an unknown one: _snapshot is null in that case (nothing to resolve SKUs
    // against), but IsUsable must still be true so ordinary Commerce traffic is not fail-closed
    // purely because no drop happens to be enabled right now. Only "never loaded" and "stale"
    // are unknown-state and must fail closed.
    public ProtectedDropCatalogStatus Status
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            var ticks = Interlocked.Read(ref _lastSuccessUtcTicks);
            DateTimeOffset? lastSuccess = ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            var usable = lastSuccess is not null &&
                         timeProvider.GetUtcNow() - lastSuccess <= TimeSpan.FromSeconds(_settings.StaleAfterSeconds);
            return new ProtectedDropCatalogStatus(
                lastSuccess is not null,
                usable,
                lastSuccess,
                Volatile.Read(ref _failures),
                snapshot?.DropId,
                snapshot?.Products.Count ?? 0);
        }
    }

    public bool TryResolveSku(string sku, out ProtectedDropProduct product)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        product = snapshot?.Products.FirstOrDefault(item =>
            string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase))!;
        return product is not null;
    }

    public bool TryResolveProductId(long productId, out ProtectedDropProduct product)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        product = snapshot?.Products.FirstOrDefault(item => item.ProductId == productId)!;
        return product is not null;
    }

    public ProtectedDropSnapshot? GetActiveDrop() => Volatile.Read(ref _snapshot);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _settings.EndpointPath);
            request.Headers.Authorization = new("Bearer", _settings.AccessToken);
            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException($"Commerce manifest returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadBoundedAsync(stream, _settings.MaximumResponseBytes, cancellationToken);
            var snapshot = Parse(bytes, timeProvider.GetUtcNow(), _settings.MaximumProducts);
            lock (_gate)
            {
                Volatile.Write(ref _snapshot, snapshot);
                Interlocked.Exchange(ref _lastSuccessUtcTicks, timeProvider.GetUtcNow().UtcTicks);
                Volatile.Write(ref _failures, 0);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failures);
            // Deliberately do not include request headers, endpoint query, or token in logs.
            logger.LogWarning(exception, "Commerce protection manifest refresh failed; retaining last known good snapshot");
        }
    }

    internal static ProtectedDropSnapshot? Parse(byte[] bytes, DateTimeOffset loadedAt, int maximumProducts)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported protection manifest.");
        }

        // Magento's webapi serializer omits a null-valued property entirely rather than
        // emitting it as JSON null (confirmed by runtime testing against Mage-OS), so a missing
        // active_drop key means the same thing as an explicit null: no active protected drop.
        if (!root.TryGetProperty("active_drop", out var activeDrop) ||
            activeDrop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (activeDrop.ValueKind != JsonValueKind.Object ||
            !activeDrop.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !DropIdPattern().IsMatch(id.GetString() ?? string.Empty) ||
            !activeDrop.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array ||
            products.GetArrayLength() > maximumProducts)
        {
            throw new InvalidDataException("Invalid protection manifest active drop.");
        }

        var dropId = id.GetString()!;
        var values = new List<ProtectedDropProduct>();
        var productIds = new HashSet<long>();
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in products.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("product_id", out var productId) || !productId.TryGetInt64(out var parsedId) || parsedId <= 0 ||
                !item.TryGetProperty("sku", out var sku) || sku.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Invalid protected product in manifest.");
            }

            var parsedSku = sku.GetString() ?? string.Empty;
            if (parsedSku.Length is 0 or > 255 || !productIds.Add(parsedId) || !skus.Add(parsedSku))
            {
                throw new InvalidDataException("Duplicate or invalid protected product in manifest.");
            }

            values.Add(new ProtectedDropProduct(dropId, parsedId, parsedSku));
        }

        return new ProtectedDropSnapshot(dropId, values, loadedAt);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Commerce manifest response exceeds configured limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex DropIdPattern();
}
