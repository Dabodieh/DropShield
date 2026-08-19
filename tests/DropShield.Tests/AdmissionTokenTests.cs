using System.Net;
using System.Net.Http.Json;
using DropShield.Api.Admission;
using DropShield.Api.Models;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

public sealed class AdmissionTokenTests
{
    private const string StockPath = "/api/products/pokemon-etb/stock";
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public async Task AdmittedSession_ReceivesValidScopedAdmissionToken()
    {
        using var factory = new DropShieldApiFactory(TokenSettings());
        using var client = CreateClient(factory);
        const string session = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var admitted = await SendStockAsync(client, "client-a", session);
        var token = GetCookie(admitted, "DropShield.Admission");
        var tokenService = factory.Services.GetRequiredService<IAdmissionTokenService>();

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.NotNull(token);
        Assert.True(tokenService.Validate(token, "pokemon-etb", session).IsValid);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task ValidToken_ReachesOriginAndVerifiesAcrossInstancesWithSharedKey()
    {
        var settings = TokenSettings();
        using var factoryA = new DropShieldApiFactory(settings);
        using var factoryB = new DropShieldApiFactory(settings);
        const string session = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var token = factoryA.Services.GetRequiredService<IAdmissionTokenService>()
            .Issue("pokemon-etb", session);
        var serviceB = factoryB.Services.GetRequiredService<IAdmissionTokenService>();
        using var client = CreateClient(factoryA);

        var response = await SendStockAsync(client, "client-b", session, token);

        Assert.True(serviceB.Validate(token, "pokemon-etb", session).IsValid);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factoryA.Origin.TotalRequests);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("signature")]
    public async Task ModifiedToken_DoesNotReachOrigin(string modifiedPart)
    {
        using var factory = new DropShieldApiFactory(TokenSettings());
        using var client = CreateClient(factory);
        const string session = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        var token = factory.Services.GetRequiredService<IAdmissionTokenService>()
            .Issue("pokemon-etb", session);
        var parts = token.Split('.');
        var index = modifiedPart == "payload" ? 1 : 2;
        var mutatedPart = MutateFirstByte(parts[index]);
        Assert.NotEqual(parts[index], mutatedPart);
        parts[index] = mutatedPart;
        var tampered = string.Join('.', parts);
        Assert.NotEqual(token, tampered);

        var response = await SendStockAsync(client, "client-c", session, tampered);
        var body = await response.Content.ReadFromJsonAsync<GatewayErrorResponse>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("admission_required", body.Error);
        Assert.Equal(0, factory.Origin.TotalRequests);
    }

    [Fact]
    public void ExpiredWrongDropWrongSessionAndUnsupportedVersion_FailDeterministically()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(TokenSettings(), timeProvider: clock);
        const string session = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        var service = factory.Services.GetRequiredService<IAdmissionTokenService>();
        var token = service.Issue("pokemon-etb", session);

        Assert.Equal(
            AdmissionTokenValidationFailure.WrongDrop,
            service.Validate(token, "another-drop", session).Failure);
        Assert.Equal(
            AdmissionTokenValidationFailure.WrongSession,
            service.Validate(token, "pokemon-etb", new string('e', 64)).Failure);
        Assert.Equal(
            AdmissionTokenValidationFailure.UnsupportedVersion,
            service.Validate($"v2.{token[(token.IndexOf('.') + 1)..]}", "pokemon-etb", session).Failure);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(
            AdmissionTokenValidationFailure.Expired,
            service.Validate(token, "pokemon-etb", session).Failure);
    }

    [Fact]
    public void UnknownSigningKeyIdentifier_FailsCleanly()
    {
        var issuingSettings = TokenSettings();
        issuingSettings["DropShield:AdmissionTokens:KeyId"] = "previous";
        using var issuingFactory = new DropShieldApiFactory(issuingSettings);
        using var validatingFactory = new DropShieldApiFactory(TokenSettings());
        const string session = "edededededededededededededededededededededededededededededededed";
        var token = issuingFactory.Services.GetRequiredService<IAdmissionTokenService>()
            .Issue("pokemon-etb", session);

        var result = validatingFactory.Services.GetRequiredService<IAdmissionTokenService>()
            .Validate(token, "pokemon-etb", session);

        Assert.Equal(AdmissionTokenValidationFailure.UnknownKeyId, result.Failure);
    }

    [Fact]
    public async Task MissingToken_FollowsAdmissionFlowWhileWaitingSessionCannotMintProof()
    {
        var settings = TokenSettings();
        settings["DropShield:Admission:MaximumActiveSessions"] = "1";
        settings["DropShield:Admission:AdmissionBatchSize"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);

        var admitted = await SendStockAsync(client, "client-owner", new string('a', 64));
        var waiting = await SendStockAsync(client, "client-waiting", new string('b', 64));

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.NotNull(GetCookie(admitted, "DropShield.Admission"));
        Assert.Equal(HttpStatusCode.Accepted, waiting.StatusCode);
        Assert.Null(GetCookie(waiting, "DropShield.Admission"));
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    [Fact]
    public async Task AdmittedRateLimit_RemainsActiveAndMetricsDoNotExposeTokenOrSession()
    {
        var settings = TokenSettings();
        settings["DropShield:Policies:Stock:ClientPermitLimit"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);
        const string session = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        var admitted = await SendStockAsync(client, "hammering-client", session);
        var token = GetCookie(admitted, "DropShield.Admission");
        var limited = await SendStockAsync(client, "hammering-client", session, token);
        var metricsJson = await client.GetStringAsync("/internal/metrics");
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(metrics);
        Assert.NotNull(token);
        Assert.Equal(1, metrics.AdmissionTokens.Issued);
        Assert.DoesNotContain(session, metricsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(token!, metricsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidToken_UsesSafeResponseAndIsCountedWithoutReachingOrigin()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(TokenSettings(), timeProvider: clock);
        using var client = CreateClient(factory);
        const string session = "9999999999999999999999999999999999999999999999999999999999999999";
        var token = factory.Services.GetRequiredService<IAdmissionTokenService>()
            .Issue("pokemon-etb", session);
        clock.Advance(TimeSpan.FromSeconds(61));

        var response = await SendStockAsync(client, "expired-client", session, token);
        var metrics = await client.GetFromJsonAsync<TrafficMetricsSnapshot>("/internal/metrics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Origin.TotalRequests);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.AdmissionTokens.Validations);
        Assert.Equal(1, metrics.AdmissionTokens.ValidationFailures);
        Assert.Equal(1, metrics.AdmissionTokens.Expired);
    }

    [Fact]
    public void InMemoryTestingMode_AllowsDocumentedEphemeralKey()
    {
        var settings = TokenSettings();
        settings["DropShield:AdmissionTokens:SigningKey"] = "";
        using var factory = new DropShieldApiFactory(settings);
        const string session = "1212121212121212121212121212121212121212121212121212121212121212";
        var service = factory.Services.GetRequiredService<IAdmissionTokenService>();
        var token = service.Issue("pokemon-etb", session);

        Assert.True(service.Validate(token, "pokemon-etb", session).IsValid);
    }

    private static Dictionary<string, string?> TokenSettings() => new()
    {
        ["DropShield:Admission:Enabled"] = "true",
        ["DropShield:Admission:DropId"] = "pokemon-etb",
        ["DropShield:Admission:MaximumActiveSessions"] = "10",
        ["DropShield:Admission:AdmissionBatchSize"] = "10",
        ["DropShield:Admission:MaximumWaitingSessions"] = "10",
        ["DropShield:Admission:SessionTtlSeconds"] = "300",
        ["DropShield:Admission:WaitingTtlSeconds"] = "300",
        ["DropShield:Admission:RetryAfterSeconds"] = "1",
        ["DropShield:AdmissionTokens:Enabled"] = "true",
        ["DropShield:AdmissionTokens:CookieName"] = "DropShield.Admission",
        ["DropShield:AdmissionTokens:LifetimeSeconds"] = "60",
        ["DropShield:AdmissionTokens:KeyId"] = "primary",
        ["DropShield:AdmissionTokens:SigningKey"] = SigningKey,
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Stock:AggregatePermitLimit"] = "1",
    };

    private static HttpClient CreateClient(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<HttpResponseMessage> SendStockAsync(
        HttpClient client,
        string identity,
        string session,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, StockPath);
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Headers.Add(
            "Cookie",
            token is null
                ? $"DropShield.Session={session}"
                : $"DropShield.Session={session}; DropShield.Admission={token}");
        return await client.SendAsync(request);
    }

    private static string? GetCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        var header = headers.SingleOrDefault(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return header?.Split(';', 2)[0].Split('=', 2)[1];
    }

    /// <summary>
    /// Flips the first decoded byte of a Base64Url segment with XOR 0xFF, guaranteeing a
    /// genuinely different byte sequence. A fixed-target-character substitution (e.g. always
    /// replacing the last character with 'A' or 'B') can occasionally decode to the same
    /// underlying bytes as the original due to Base64 padding-bit slack in the final character,
    /// producing a token that is not actually mutated and an intermittently-passing test. XOR
    /// 0xFF on a full byte has no such collision — the same pattern used by
    /// <see cref="OriginAssertionTests"/>'s equivalent signature-mutation test.
    /// </summary>
    private static string MutateFirstByte(string value)
    {
        var bytes = Base64UrlDecode(value);
        bytes[0] ^= 0xFF;
        return Base64UrlEncode(bytes);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };
        return Convert.FromBase64String(base64);
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
