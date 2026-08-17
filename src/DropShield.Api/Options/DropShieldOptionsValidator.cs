using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DropShield.Api.Options;

public sealed partial class DropShieldOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<DropShieldOptions>
{
    private static readonly HashSet<string> AllowedOriginHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "::1",
        "host.docker.internal",
    };

    public ValidateOptionsResult Validate(string? name, DropShieldOptions options)
    {
        var failures = new List<string>();

        ValidateOrigin(options.OriginBaseUrl, failures);

        if (options.OriginTimeoutSeconds <= 0)
        {
            failures.Add("DropShield:OriginTimeoutSeconds must be greater than zero.");
        }

        if (options.ProtectedProducts.Count == 0 ||
            options.ProtectedProducts.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("DropShield:ProtectedProducts must contain at least one non-empty product ID.");
        }

        if (options.ProtectedProducts.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            options.ProtectedProducts.Count)
        {
            failures.Add("DropShield:ProtectedProducts must not contain duplicate product IDs.");
        }

        ValidateClientPolicy("Stock", options.Policies.Stock, failures);
        ValidateClientPolicy("Cart", options.Policies.Cart, failures);
        ValidateClientPolicy("Checkout", options.Policies.Checkout, failures);

        if (options.Policies.Stock.Enabled &&
            (options.Policies.Stock.AggregatePermitLimit <= 0 ||
             options.Policies.Stock.AggregateWindowSeconds <= 0))
        {
            failures.Add(
                "Enabled stock policy aggregate permit limit and window must be greater than zero.");
        }

        if (!HeaderNamePattern().IsMatch(options.SyntheticClientIdentity.HeaderName))
        {
            failures.Add("DropShield synthetic client identity header name is invalid.");
        }

        var isControlledEnvironment = environment.IsDevelopment() ||
                                      environment.IsEnvironment("Testing");

        if (options.SyntheticClientIdentity.Enabled && !isControlledEnvironment)
        {
            failures.Add(
                "Synthetic client identity can be enabled only in Development or Testing.");
        }

        if (options.InternalMetrics.Enabled && !isControlledEnvironment)
        {
            failures.Add("Internal metrics can be enabled only in Development or Testing.");
        }

        if (!Enum.IsDefined(options.StateProvider))
        {
            failures.Add("DropShield:StateProvider must be InMemory or Redis.");
        }
        else if (options.StateProvider == TrafficStateProvider.Redis)
        {
            ValidateRedis(options.Redis, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRedis(
        RedisStateOptions redisOptions,
        ICollection<string> failures)
    {
        ConfigurationOptions configuration;
        try
        {
            configuration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            failures.Add("DropShield:Redis:ConnectionString is invalid.");
            return;
        }

        if (configuration.EndPoints.Count == 0 ||
            configuration.EndPoints.Any(endpoint => !IsApprovedLocalEndpoint(endpoint)))
        {
            failures.Add(
                "DropShield Redis endpoints must use localhost, loopback, or host.docker.internal.");
        }

        if (redisOptions.Database < 0)
        {
            failures.Add("DropShield:Redis:Database must be zero or greater.");
        }

        if (!RedisKeyPrefixPattern().IsMatch(redisOptions.KeyPrefix))
        {
            failures.Add(
                "DropShield:Redis:KeyPrefix must be a bounded lowercase namespace.");
        }

        if (redisOptions.IdentityHashKey.Length < 32)
        {
            failures.Add(
                "DropShield:Redis:IdentityHashKey must contain at least 32 characters in Redis mode.");
        }

        if (redisOptions.ConnectTimeoutMilliseconds is < 100 or > 10_000 ||
            redisOptions.OperationTimeoutMilliseconds is < 100 or > 10_000)
        {
            failures.Add("DropShield Redis timeouts must be between 100 and 10000 milliseconds.");
        }
    }

    private static bool IsApprovedLocalEndpoint(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => AllowedOriginHosts.Contains(dns.Host),
        IPEndPoint ip => IPAddress.IsLoopback(ip.Address),
        _ => false,
    };

    private static void ValidateOrigin(string originBaseUrl, ICollection<string> failures)
    {
        if (!Uri.TryCreate(originBaseUrl, UriKind.Absolute, out var origin))
        {
            failures.Add("DropShield:OriginBaseUrl must be an absolute URL.");
            return;
        }

        if ((!origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !AllowedOriginHosts.Contains(origin.Host) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            failures.Add(
                "DropShield:OriginBaseUrl must contain only HTTP(S), an approved local host, and an optional port.");
        }
    }

    private static void ValidateClientPolicy(
        string policyName,
        ClientPolicyOptions policy,
        ICollection<string> failures)
    {
        if (policy.Enabled && (policy.ClientPermitLimit <= 0 || policy.ClientWindowSeconds <= 0))
        {
            failures.Add(
                $"Enabled {policyName} client permit limit and window must be greater than zero.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderNamePattern();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9:-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex RedisKeyPrefixPattern();
}
