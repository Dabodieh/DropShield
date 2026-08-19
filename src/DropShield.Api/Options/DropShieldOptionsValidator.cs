using System.Net;
using System.Security.Cryptography;
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

        if (!Enum.IsDefined(options.OriginMode))
        {
            failures.Add("DropShield:OriginMode must be DemoStore or AdobeCommerce.");
        }

        if (options.AdobeCommerce.MaximumProtectedRequestBodyBytes is < 4_096 or > 1_048_576)
        {
            failures.Add(
                "DropShield:AdobeCommerce:MaximumProtectedRequestBodyBytes must be between 4096 and 1048576.");
        }

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

        ValidateAdmission(options, failures);
        ValidateAdmissionTokens(options, isControlledEnvironment, failures);
        ValidateActionProofs(options, isControlledEnvironment, failures);
        ValidateInventoryReservation(options, failures);
        ValidateBehaviourScoring(options, failures);
        ValidateOriginAssertions(options, isControlledEnvironment, failures);
        ValidateInternalHashing(options, isControlledEnvironment, failures);
        ValidateEdgeTrust(options, isControlledEnvironment, failures);
        ValidateKeySeparation(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAdmission(
        DropShieldOptions options,
        ICollection<string> failures)
    {
        var admission = options.Admission;
        if (!admission.Enabled)
        {
            return;
        }

        if (!AdmissionProductPattern().IsMatch(admission.ProtectedProduct) ||
            !options.ProtectedProducts.Contains(
                admission.ProtectedProduct,
                StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                "DropShield:Admission:ProtectedProduct must be a bounded configured protected product ID.");
        }

        if (admission.MaximumActiveSessions is < 1 or > 100_000)
        {
            failures.Add(
                "DropShield:Admission:MaximumActiveSessions must be between 1 and 100000.");
        }

        if (admission.AdmissionBatchSize < 1 ||
            admission.AdmissionBatchSize > admission.MaximumActiveSessions)
        {
            failures.Add(
                "DropShield:Admission:AdmissionBatchSize must be between 1 and MaximumActiveSessions.");
        }

        if (admission.MaximumWaitingSessions is < 1 or > 1_000_000)
        {
            failures.Add(
                "DropShield:Admission:MaximumWaitingSessions must be between 1 and 1000000.");
        }

        if (admission.SessionTtlSeconds is < 1 or > 86_400 ||
            admission.WaitingTtlSeconds is < 1 or > 86_400 ||
            admission.RetryAfterSeconds is < 1 or > 300)
        {
            failures.Add(
                "DropShield admission TTLs must be between 1 and 86400 seconds and retry interval between 1 and 300 seconds.");
        }

        if (admission.SessionTtlSeconds < admission.RetryAfterSeconds ||
            admission.WaitingTtlSeconds < admission.RetryAfterSeconds)
        {
            failures.Add(
                "DropShield admission session and waiting TTLs must not be shorter than the retry interval.");
        }
    }

    private static void ValidateAdmissionTokens(
        DropShieldOptions options,
        bool isControlledEnvironment,
        ICollection<string> failures)
    {
        var tokens = options.AdmissionTokens;
        if (!tokens.Enabled)
        {
            return;
        }

        if (!options.Admission.Enabled)
        {
            failures.Add("DropShield:AdmissionTokens requires enabled admission control.");
        }

        if (!CookieNamePattern().IsMatch(tokens.CookieName))
        {
            failures.Add("DropShield:AdmissionTokens:CookieName is invalid.");
        }

        if (tokens.LifetimeSeconds is < 1 or > 3_600 ||
            (options.Admission.Enabled && tokens.LifetimeSeconds > options.Admission.SessionTtlSeconds))
        {
            failures.Add(
                "DropShield:AdmissionTokens:LifetimeSeconds must be between 1 and 3600 and no longer than the admission session TTL.");
        }

        if (!KeyIdPattern().IsMatch(tokens.KeyId))
        {
            failures.Add("DropShield:AdmissionTokens:KeyId is invalid.");
        }

        var requiresExplicitKey = options.StateProvider == TrafficStateProvider.Redis ||
                                  !isControlledEnvironment;
        if (string.IsNullOrWhiteSpace(tokens.SigningKey))
        {
            if (requiresExplicitKey)
            {
                failures.Add(
                    "DropShield:AdmissionTokens:SigningKey is required in Redis, Production, or other non-controlled environments.");
            }

            return;
        }

        try
        {
            var key = Convert.FromBase64String(tokens.SigningKey);
            if (key.Length < 32)
            {
                failures.Add(
                    "DropShield:AdmissionTokens:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
            }

            CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException)
        {
            failures.Add(
                "DropShield:AdmissionTokens:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
        }
    }

    private static void ValidateActionProofs(
        DropShieldOptions options,
        bool isControlledEnvironment,
        ICollection<string> failures)
    {
        var proofs = options.ActionProofs;
        if (!proofs.Enabled)
        {
            return;
        }

        if (!options.Admission.Enabled || !options.AdmissionTokens.Enabled)
        {
            failures.Add("DropShield:ActionProofs requires enabled admission and admission tokens.");
        }

        if (!HeaderNamePattern().IsMatch(proofs.HeaderName))
        {
            failures.Add("DropShield:ActionProofs:HeaderName is invalid.");
        }

        if (proofs.LifetimeSeconds is < 1 or > 300 ||
            (options.AdmissionTokens.Enabled &&
             proofs.LifetimeSeconds > options.AdmissionTokens.LifetimeSeconds))
        {
            failures.Add(
                "DropShield:ActionProofs:LifetimeSeconds must be between 1 and 300 and no longer than the admission token lifetime.");
        }

        if (proofs.ReplayTtlMarginSeconds is < 0 or > 300)
        {
            failures.Add(
                "DropShield:ActionProofs:ReplayTtlMarginSeconds must be between 0 and 300.");
        }

        if (proofs.MaximumInMemoryMarkers is < 1 or > 1_000_000)
        {
            failures.Add(
                "DropShield:ActionProofs:MaximumInMemoryMarkers must be between 1 and 1000000.");
        }

        if (!KeyIdPattern().IsMatch(proofs.KeyId))
        {
            failures.Add("DropShield:ActionProofs:KeyId is invalid.");
        }

        if (string.IsNullOrWhiteSpace(proofs.SigningKey))
        {
            if (!isControlledEnvironment)
            {
                failures.Add(
                    "DropShield:ActionProofs:SigningKey is required in Production or other non-controlled environments.");
            }

            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(proofs.SigningKey);
        }
        catch (FormatException)
        {
            failures.Add(
                "DropShield:ActionProofs:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
            return;
        }

        if (key.Length < 32)
        {
            failures.Add(
                "DropShield:ActionProofs:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
        }

        CryptographicOperations.ZeroMemory(key);
    }

    private static void ValidateInventoryReservation(
        DropShieldOptions options,
        ICollection<string> failures)
    {
        var inventory = options.InventoryReservation;
        if (!inventory.Enabled)
        {
            return;
        }

        if (!options.Admission.Enabled || !options.ActionProofs.Enabled)
        {
            failures.Add("DropShield:InventoryReservation requires enabled admission and action proofs.");
        }

        if (inventory.InitialStock is < 1 or > 1_000_000 ||
            inventory.ReservationTtlSeconds is < 1 or > 86_400 ||
            inventory.MaximumInMemoryReservations is < 1 or > 1_000_000)
        {
            failures.Add("DropShield inventory reservation settings are outside supported bounds.");
        }

        if (options.Admission.Enabled &&
            inventory.ReservationTtlSeconds > options.Admission.SessionTtlSeconds)
        {
            failures.Add("DropShield reservation TTL must not exceed the admission session TTL.");
        }
    }

    private static void ValidateBehaviourScoring(
        DropShieldOptions options,
        ICollection<string> failures)
    {
        var scoring = options.BehaviourScoring;
        if (!scoring.Enabled)
        {
            return;
        }

        if (!options.Admission.Enabled || !options.AdmissionTokens.Enabled ||
            !options.ActionProofs.Enabled)
        {
            failures.Add("DropShield:BehaviourScoring requires admission, admission tokens, and action proofs.");
        }

        if (scoring.ObservationWindowSeconds is < 30 or > 120 ||
            scoring.StateTtlSeconds is < 30 or > 300 ||
            scoring.StateTtlSeconds < scoring.ObservationWindowSeconds ||
            scoring.MaximumInMemoryActors is < 1 or > 1_000_000 ||
            scoring.MaximumEventsPerActor is < 16 or > 1_024 ||
            scoring.RestrictionRetryAfterSeconds is < 1 or > 60)
        {
            failures.Add("DropShield behavioural scoring settings are outside supported bounds.");
        }
    }

    private static void ValidateOriginAssertions(
        DropShieldOptions options,
        bool isControlledEnvironment,
        ICollection<string> failures)
    {
        var assertions = options.OriginAssertions;
        if (!assertions.Enabled)
        {
            return;
        }

        if (!HeaderNamePattern().IsMatch(assertions.HeaderName))
        {
            failures.Add("DropShield:OriginAssertions:HeaderName is invalid.");
        }

        if (assertions.LifetimeSeconds is < 1 or > 30)
        {
            failures.Add("DropShield:OriginAssertions:LifetimeSeconds must be between 1 and 30.");
        }

        if (!KeyIdPattern().IsMatch(assertions.KeyId))
        {
            failures.Add("DropShield:OriginAssertions:KeyId is invalid.");
        }

        if (string.IsNullOrWhiteSpace(assertions.SigningKey))
        {
            if (!isControlledEnvironment)
            {
                failures.Add(
                    "DropShield:OriginAssertions:SigningKey is required in Production or other non-controlled environments.");
            }

            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(assertions.SigningKey);
        }
        catch (FormatException)
        {
            failures.Add(
                "DropShield:OriginAssertions:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
            return;
        }

        if (key.Length < 32)
        {
            failures.Add(
                "DropShield:OriginAssertions:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
        }

        CryptographicOperations.ZeroMemory(key);
    }

    private static void ValidateInternalHashing(
        DropShieldOptions options,
        bool isControlledEnvironment,
        ICollection<string> failures)
    {
        if (!options.InventoryReservation.Enabled && !options.BehaviourScoring.Enabled)
        {
            return;
        }

        var configuredKey = options.InternalHashing.SigningKey;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (!isControlledEnvironment)
            {
                failures.Add(
                    "DropShield:InternalHashing:SigningKey is required in Production or other non-controlled environments.");
            }

            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException)
        {
            failures.Add(
                "DropShield:InternalHashing:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
            return;
        }

        if (key.Length < 32)
        {
            failures.Add(
                "DropShield:InternalHashing:SigningKey must be Base64-encoded and contain at least 32 random bytes.");
        }

        CryptographicOperations.ZeroMemory(key);
    }

    private static void ValidateEdgeTrust(
        DropShieldOptions options,
        bool isControlledEnvironment,
        ICollection<string> failures)
    {
        var edge = options.EdgeTrust;
        if (!edge.Enabled)
        {
            return;
        }

        if (!HeaderNamePattern().IsMatch(edge.HeaderName))
        {
            failures.Add("DropShield:EdgeTrust:HeaderName is invalid.");
        }

        if (string.IsNullOrWhiteSpace(edge.SharedKey))
        {
            if (!isControlledEnvironment)
            {
                failures.Add(
                    "DropShield:EdgeTrust:SharedKey is required in Production or other non-controlled environments.");
            }

            return;
        }

        try
        {
            var key = Convert.FromBase64String(edge.SharedKey);
            if (key.Length < 32)
            {
                failures.Add("DropShield:EdgeTrust:SharedKey must be Base64-encoded and contain at least 32 random bytes.");
            }

            CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException)
        {
            failures.Add("DropShield:EdgeTrust:SharedKey must be Base64-encoded and contain at least 32 random bytes.");
        }
    }

    private static void ValidateKeySeparation(
        DropShieldOptions options,
        ICollection<string> failures)
    {
        var configuredKeys = new (string Purpose, string Value)[]
        {
            ("admission signing", options.AdmissionTokens.SigningKey),
            ("action-proof signing", options.ActionProofs.SigningKey),
            ("origin-assertion signing", options.OriginAssertions.SigningKey),
            ("internal hashing", options.InternalHashing.SigningKey),
            ("edge trust", options.EdgeTrust.SharedKey),
        };
        var decodedKeys = new List<(string Purpose, byte[] Material)>();
        try
        {
            foreach (var (purpose, value) in configuredKeys)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                try
                {
                    decodedKeys.Add((purpose, Convert.FromBase64String(value)));
                }
                catch (FormatException)
                {
                    // The purpose-specific validator reports the configuration error.
                }
            }

            for (var left = 0; left < decodedKeys.Count; left++)
            {
                for (var right = left + 1; right < decodedKeys.Count; right++)
                {
                    if (decodedKeys[left].Material.Length == decodedKeys[right].Material.Length &&
                        CryptographicOperations.FixedTimeEquals(
                            decodedKeys[left].Material,
                            decodedKeys[right].Material))
                    {
                        failures.Add(
                            $"DropShield configured key material must not be reused between {decodedKeys[left].Purpose} and {decodedKeys[right].Purpose}.");
                    }
                }
            }
        }
        finally
        {
            foreach (var (_, material) in decodedKeys)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdmissionProductPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CookieNamePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();
}
