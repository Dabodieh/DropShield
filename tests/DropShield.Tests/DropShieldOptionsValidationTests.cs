using DropShield.Api.Options;
using DropShield.Tests.Support;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DropShield.Tests;

public sealed class DropShieldOptionsValidationTests
{
    [Fact]
    public void EnabledPolicy_RejectsInvalidLimits()
    {
        var options = ValidOptions();
        options.Policies.Stock.ClientPermitLimit = 0;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Stock", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:5058/hidden-path")]
    [InlineData("ftp://localhost:5058")]
    public void OriginValidation_RejectsNonLocalOrMalformedOrigins(string origin)
    {
        var options = ValidOptions();
        options.OriginBaseUrl = origin;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("OriginBaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void SyntheticIdentity_IsRejectedOutsideControlledEnvironment()
    {
        var options = ValidOptions();
        options.SyntheticClientIdentity.Enabled = true;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Production"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Development or Testing", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalOriginConfiguration_IsRejectedWhenApplicationStarts()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DropShield:OriginBaseUrl"] = "https://example.com",
        };
        using var factory = new DropShieldApiFactory(settings);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("OriginBaseUrl", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("example.com:6379")]
    [InlineData("192.0.2.10:6379")]
    public void RedisMode_RejectsExternalEndpoints(string connectionString)
    {
        var options = ValidOptions();
        options.StateProvider = TrafficStateProvider.Redis;
        options.Redis.ConnectionString = connectionString;
        options.Redis.IdentityHashKey = new string('x', 32);

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Redis endpoints", StringComparison.Ordinal));
    }

    [Fact]
    public void RedisMode_RequiresIdentityHashKey()
    {
        var options = ValidOptions();
        options.StateProvider = TrafficStateProvider.Redis;
        options.Redis.IdentityHashKey = string.Empty;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("IdentityHashKey", StringComparison.Ordinal));
    }

    [Fact]
    public void InMemoryMode_DoesNotRequireRedisCredentials()
    {
        var options = ValidOptions();
        options.StateProvider = TrafficStateProvider.InMemory;
        options.Redis.IdentityHashKey = string.Empty;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Admission_RequiresConfiguredProtectedProduct()
    {
        var options = ValidOptions();
        options.Admission.Enabled = true;
        options.Admission.ProtectedProduct = "not-protected";

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Admission:ProtectedProduct", StringComparison.Ordinal));
    }

    [Fact]
    public void Admission_RejectsBatchLargerThanActiveCapacity()
    {
        var options = ValidOptions();
        options.Admission.Enabled = true;
        options.Admission.MaximumActiveSessions = 10;
        options.Admission.AdmissionBatchSize = 11;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("AdmissionBatchSize", StringComparison.Ordinal));
    }

    [Fact]
    public void Admission_RejectsTtlShorterThanRetryInterval()
    {
        var options = ValidOptions();
        options.Admission.Enabled = true;
        options.Admission.SessionTtlSeconds = 4;
        options.Admission.RetryAfterSeconds = 5;

        var result = new DropShieldOptionsValidator(new TestHostEnvironment("Testing"))
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("shorter than the retry", StringComparison.Ordinal));
    }

    private static DropShieldOptions ValidOptions() => new()
    {
        OriginBaseUrl = "http://localhost:5058",
        OriginTimeoutSeconds = 10,
        ProtectedProducts = ["pokemon-etb"],
        SyntheticClientIdentity = new SyntheticClientIdentityOptions
        {
            Enabled = true,
            HeaderName = "X-DropShield-Test-Client",
        },
        InternalMetrics = new InternalMetricsOptions { Enabled = true },
        Admission = new AdmissionOptions
        {
            Enabled = false,
            ProtectedProduct = "pokemon-etb",
            MaximumActiveSessions = 200,
            AdmissionBatchSize = 20,
            MaximumWaitingSessions = 2_000,
            SessionTtlSeconds = 300,
            WaitingTtlSeconds = 600,
            RetryAfterSeconds = 5,
        },
        Policies = new TrafficPoliciesOptions
        {
            Stock = new StockPolicyOptions
            {
                Enabled = true,
                ClientPermitLimit = 5,
                ClientWindowSeconds = 1,
                AggregatePermitLimit = 200,
                AggregateWindowSeconds = 1,
            },
            Cart = new ClientPolicyOptions
            {
                Enabled = true,
                ClientPermitLimit = 2,
                ClientWindowSeconds = 1,
            },
            Checkout = new ClientPolicyOptions
            {
                Enabled = true,
                ClientPermitLimit = 1,
                ClientWindowSeconds = 5,
            },
        },
    };

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "DropShield.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
