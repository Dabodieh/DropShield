using System.Net;
using System.Net.Http.Json;
using System.Text;
using DropShield.Api.Origin;
using DropShield.Api.Traffic;
using DropShield.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DropShield.Tests;

public sealed class OriginAssertionTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"productId":"pokemon-etb"}""");

    [Fact]
    public void ValidAssertion_ValidatesWithKnownKey()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ModifiedPayload_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);
        var parts = assertion.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}A.{parts[2]}";

        var result = service.Validate(tampered, "pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ModifiedSignature_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);
        var parts = assertion.Split('.');
        var tamperedSignature = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        var tampered = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        var result = service.Validate(tampered, "pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        Assert.Equal(OriginAssertionValidationFailure.InvalidSignature, result.Failure);
    }

    [Fact]
    public void ExpiredAssertion_FailsValidation()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var (service, _) = CreateService(clock);
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        clock.Advance(TimeSpan.FromSeconds(21));
        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        Assert.Equal(OriginAssertionValidationFailure.Expired, result.Failure);
    }

    [Fact]
    public void WrongMethod_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        var result = service.Validate(assertion, "pokemon-etb", "cart", "PUT", "POST /api/cart", Body);

        Assert.Equal(OriginAssertionValidationFailure.WrongMethod, result.Failure);
    }

    [Fact]
    public void WrongRoute_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/checkout", Body);

        Assert.Equal(OriginAssertionValidationFailure.WrongRoute, result.Failure);
    }

    [Fact]
    public void WrongAction_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        var result = service.Validate(assertion, "pokemon-etb", "checkout", "POST", "POST /api/cart", Body);

        Assert.Equal(OriginAssertionValidationFailure.WrongAction, result.Failure);
    }

    [Fact]
    public void WrongDrop_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);

        var result = service.Validate(assertion, "another-drop", "cart", "POST", "POST /api/cart", Body);

        Assert.Equal(OriginAssertionValidationFailure.WrongDrop, result.Failure);
    }

    [Fact]
    public void KnownCrossLanguageTestVector_Validates()
    {
        var clock = new TestTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1755432010));
        var settings = Settings();
        settings["DropShield:OriginAssertions:KeyId"] = "test-key-1";
        settings["DropShield:OriginAssertions:SigningKey"] =
            "dGVzdC1vbmx5LW9yaWdpbi1hc3NlcnRpb24ta2V5LTAwMDAwMDAwMDA=";
        using var factory = new DropShieldApiFactory(settings, timeProvider: clock);
        var service = factory.Services.GetRequiredService<IOriginAssertionService>();
        var body = Encoding.UTF8.GetBytes("""{"productId":"pokemon-etb","quantity":1}""");
        const string assertion =
            "v1.eyJ2IjoxLCJraWQiOiJ0ZXN0LWtleS0xIiwiZHJvcCI6InBva2Vtb24tZXRiIiwiYWN0aW9uIjoiY2FydCIsIm1ldGhvZCI6IlBPU1QiLCJyb3V0ZSI6IlBPU1QgL2FwaS9jYXJ0IiwiYm9keUhhc2giOiJvSkVXNGpncTFTNWRROGxVd1VwODlBWTdxYVBGM2lGdEVxTkdTTW55Q3AwIiwianRpIjoiQUFFQ0F3UUZCZ2NJQ1FvTERBME9EdyIsImlhdCI6MTc1NTQzMjAwMCwiZXhwIjoxNzU1NDMyMDIwfQ._8K7IlF3kXGEexqo6wb0FSwxvIBkvCvhPwCxlOL8pY0";

        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/cart", body);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ChangedBody_FailsValidation()
    {
        var (service, _) = CreateService();
        var assertion = service.Issue("pokemon-etb", "cart", "POST", "POST /api/cart", Body);
        var differentBody = Encoding.UTF8.GetBytes("""{"productId":"other-item"}""");

        var result = service.Validate(assertion, "pokemon-etb", "cart", "POST", "POST /api/cart", differentBody);

        Assert.Equal(OriginAssertionValidationFailure.BodyMismatch, result.Failure);
    }

    private static (IOriginAssertionService Service, TestTimeProvider Clock) CreateService(
        TestTimeProvider? clock = null)
    {
        var timeProvider = clock ?? new TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        using var factory = new DropShieldApiFactory(Settings(), timeProvider: timeProvider);
        return (factory.Services.GetRequiredService<IOriginAssertionService>(), timeProvider);
    }

    [Fact]
    public async Task IncomingFakeAssertionHeader_IsStripped()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/pokemon-etb/stock");
        request.Headers.Add("X-DropShield-Test-Client", "probe");
        request.Headers.Add("X-DropShield-Origin-Assertion", "v1.fake.fake");
        await client.SendAsync(request);

        Assert.Null(factory.Origin.LastOriginAssertionHeader);
    }

    [Fact]
    public async Task SuccessfulProtectedMutation_ReceivesFreshAssertion()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart");
        request.Headers.Add("X-DropShield-Test-Client", "buyer");
        request.Headers.Add("X-DropShield-Origin-Assertion", "v1.client-forged.forged");
        request.Content = new StringContent("""{"productId":"pokemon-etb"}""", Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(factory.Origin.LastOriginAssertionHeader);
        Assert.Equal("X-DropShield-Origin-Assertion", factory.Origin.LastOriginAssertionHeader!.Value.HeaderName);
        Assert.NotEqual("v1.client-forged.forged", factory.Origin.LastOriginAssertionHeader.Value.Value);
    }

    [Fact]
    public async Task RejectedMutation_ReceivesNoOriginAssertion()
    {
        var settings = Settings();
        settings["DropShield:Policies:Cart:ClientPermitLimit"] = "1";
        using var factory = new DropShieldApiFactory(settings);
        using var client = CreateClient(factory);

        await client.SendAsync(BuildCartRequest("buyer"));
        var limited = await client.SendAsync(BuildCartRequest("buyer"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(1, factory.Origin.TotalRequests);
    }

    private static HttpRequestMessage BuildCartRequest(string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart");
        request.Headers.Add("X-DropShield-Test-Client", identity);
        request.Content = new StringContent("""{"productId":"pokemon-etb"}""", Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task UnprotectedRequest_IsUnaffectedByOriginAssertions()
    {
        using var factory = new DropShieldApiFactory(Settings());
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(factory.Origin.LastOriginAssertionHeader);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["DropShield:Admission:Enabled"] = "false",
        ["DropShield:AdmissionTokens:Enabled"] = "false",
        ["DropShield:ActionProofs:Enabled"] = "false",
        ["DropShield:InventoryReservation:Enabled"] = "false",
        ["DropShield:BehaviourScoring:Enabled"] = "false",
        ["DropShield:OriginAssertions:Enabled"] = "true",
        ["DropShield:OriginAssertions:LifetimeSeconds"] = "20",
        ["DropShield:OriginAssertions:SigningKey"] = SigningKey,
        ["DropShield:Policies:Stock:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Cart:ClientPermitLimit"] = "100",
        ["DropShield:Policies:Checkout:ClientPermitLimit"] = "100",
    };

    private static HttpClient CreateClient(DropShieldApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
}
