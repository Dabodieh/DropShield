using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Traffic;

public sealed class ClientIdentityProvider(
    IOptions<DropShieldOptions> options,
    IHostEnvironment environment)
{
    private readonly DropShieldOptions _options = options.Value;

    public string GetPartitionKey(HttpContext context)
    {
        var syntheticOptions = _options.SyntheticClientIdentity;
        var mayUseSyntheticIdentity = syntheticOptions.Enabled &&
                                      (environment.IsDevelopment() ||
                                       environment.IsEnvironment("Testing"));

        if (mayUseSyntheticIdentity &&
            context.Request.Headers.TryGetValue(syntheticOptions.HeaderName, out var values))
        {
            var value = values.ToString().Trim();
            if (value.Length is > 0 and <= 128)
            {
                return $"test:{value}";
            }
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
