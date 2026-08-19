using System.Net;
using System.Net.Http.Json;
using DropShield.Demo;

const string ProductId = "pokemon-etb";

var dropShieldBaseUrl = Environment.GetEnvironmentVariable("DROPSHIELD_DEMO_API_URL") ?? "http://localhost:5257";
var demoStoreBaseUrl = Environment.GetEnvironmentVariable("DROPSHIELD_DEMO_STORE_URL") ?? "http://localhost:5058";

if (!LocalhostGuard.TryValidate(dropShieldBaseUrl, out var dropShieldUri, out var dropShieldError))
{
    Console.Error.WriteLine($"Refusing to run: {dropShieldError}");
    return 1;
}

if (!LocalhostGuard.TryValidate(demoStoreBaseUrl, out var demoStoreUri, out var demoStoreError))
{
    Console.Error.WriteLine($"Refusing to run: {demoStoreError}");
    return 1;
}

var report = new DemoReport();
report.Title("DropShield high-demand product drop demo");

using var demoStoreHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = demoStoreUri, Timeout = TimeSpan.FromSeconds(10) };
using var dropShieldHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true };
using var dropShieldHttp = new HttpClient(dropShieldHandler) { BaseAddress = dropShieldUri, Timeout = TimeSpan.FromSeconds(10) };

if (!await CheckHealthAsync(demoStoreHttp, "DemoStore", report) ||
    !await CheckHealthAsync(dropShieldHttp, "DropShield.Api", report))
{
    return 1;
}

try
{
    await Stage1_HealthyOrigin(demoStoreHttp, report);
    await Stage2_NormalShopper(dropShieldHttp, report);
    await Stage3_ExcessivePolling(dropShieldHttp, report);
    await Stage4_WaitingRoom(dropShieldHttp, report);
    using var cartShopper = new DropShieldClient(dropShieldUri, "demo-shopper-cart");
    var (cartOk, actionOutcome) = await Stage5_ActionProofAndCart(cartShopper, report);
    if (cartOk)
    {
        await Stage6_ReplayRejected(cartShopper, report, actionOutcome!.Token!);
        await Stage7_Reservation(dropShieldHttp, report);
        await Stage8_Checkout(cartShopper, dropShieldHttp, report);
    }

    await Stage9_FinalMetrics(dropShieldHttp, report);
}
catch (HttpRequestException exception)
{
    report.Fail($"HTTP request failed: {exception.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    report.Fail("A request timed out. Is DropShield.Api running in the demo configuration?");
    return 1;
}

Console.WriteLine();
Console.WriteLine("Demo complete.");
return 0;

static async Task<bool> CheckHealthAsync(HttpClient client, string name, DemoReport report)
{
    try
    {
        using var response = await client.GetAsync("/health");
        if (response.IsSuccessStatusCode)
        {
            report.Line($"{name} health", "OK");
            return true;
        }

        report.Fail($"{name} returned {(int)response.StatusCode} for /health.");
        return false;
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        report.Fail($"{name} is not reachable at the configured URL. Start it first — see docs/demo.md.");
        return false;
    }
}

static async Task Stage1_HealthyOrigin(HttpClient demoStore, DemoReport report)
{
    report.Section("1. Healthy origin");
    using var product = await demoStore.GetAsync($"/api/products/{ProductId}");
    using var stock = await demoStore.GetAsync($"/api/products/{ProductId}/stock");
    report.Line("Product lookup", product.IsSuccessStatusCode ? "OK" : $"unexpected {(int)product.StatusCode}");
    report.Line("Stock lookup", stock.IsSuccessStatusCode ? "OK" : $"unexpected {(int)stock.StatusCode}");
}

static async Task Stage2_NormalShopper(HttpClient dropShield, DemoReport report)
{
    report.Section("2. Normal shopper");
    using var shopper = new DropShieldClient(dropShield.BaseAddress!, "demo-shopper-normal");
    using var stock = await shopper.GetStockAsync(ProductId, CancellationToken.None);
    report.Line("Stock request", DescribeAdmissionOutcome(stock.StatusCode, shopper.HasAdmissionProof));
}

static async Task Stage3_ExcessivePolling(HttpClient dropShield, DemoReport report)
{
    report.Section("3. Excessive stock polling");
    using var poller = new DropShieldClient(dropShield.BaseAddress!, "demo-shopper-poller");
    const int requestCount = 12;
    var forwarded = 0;
    var rateLimited = 0;

    for (var i = 0; i < requestCount; i++)
    {
        using var response = await poller.GetStockAsync(ProductId, CancellationToken.None);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            rateLimited++;
        }
        else if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
        {
            forwarded++;
        }
    }

    report.Line("Requests sent", requestCount.ToString());
    report.Line("Forwarded", forwarded.ToString());
    report.Line("Rate limited", rateLimited.ToString());
    report.Line("Note", "excessive polling / bot-like synthetic traffic, not a bot-detection claim");
}

static async Task Stage4_WaitingRoom(HttpClient dropShield, DemoReport report)
{
    report.Section("4. Admission / waiting room (demo capacity)");
    using var shopperA = new DropShieldClient(dropShield.BaseAddress!, "demo-shopper-a");
    using var shopperB = new DropShieldClient(dropShield.BaseAddress!, "demo-shopper-b");
    using var shopperC = new DropShieldClient(dropShield.BaseAddress!, "demo-shopper-c");

    using var responseA = await shopperA.GetStockAsync(ProductId, CancellationToken.None);
    using var responseB = await shopperB.GetStockAsync(ProductId, CancellationToken.None);
    using var responseC = await shopperC.GetStockAsync(ProductId, CancellationToken.None);

    report.Line("Shopper A", DescribeAdmissionOutcome(responseA.StatusCode, shopperA.HasAdmissionProof));
    report.Line("Shopper B", DescribeAdmissionOutcome(responseB.StatusCode, shopperB.HasAdmissionProof));
    report.Line("Shopper C", DescribeAdmissionOutcome(responseC.StatusCode, shopperC.HasAdmissionProof));

    if (responseC.StatusCode != HttpStatusCode.Accepted)
    {
        report.Line("Note", "demo admission capacity was not exceeded by three shoppers");
        return;
    }

    report.Line("Waiting for capacity", "polling shopper C until admitted or timeout");
    var admitted = false;
    for (var attempt = 0; attempt < 6 && !admitted; attempt++)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        using var retry = await shopperC.GetStockAsync(ProductId, CancellationToken.None);
        admitted = retry.StatusCode == HttpStatusCode.OK;
    }

    report.Line("Shopper C (retry)", admitted ? "admitted" : "still waiting after retry window");
}

static async Task<(bool Success, ActionProofOutcome? Outcome)> Stage5_ActionProofAndCart(
    DropShieldClient shopper,
    DemoReport report)
{
    report.Section("5. Action proof and cart");
    using var stock = await shopper.GetStockAsync(ProductId, CancellationToken.None);
    if (stock.StatusCode != HttpStatusCode.OK || !shopper.HasAdmissionProof)
    {
        report.Line("Admission", "not established — cannot continue to cart");
        return (false, null);
    }

    var proof = await shopper.RequestActionProofAsync("cart", CancellationToken.None);
    report.Line("Action proof", proof.IsSuccess ? "issued" : $"failed ({(int)proof.StatusCode})");
    if (!proof.IsSuccess || proof.Token is null)
    {
        return (false, proof);
    }

    using var cart = await shopper.PostCartAsync(ProductId, 1, proof.Token, CancellationToken.None);
    report.Line("First cart request", cart.IsSuccessStatusCode ? "accepted" : $"unexpected {(int)cart.StatusCode}");

    return (cart.IsSuccessStatusCode, proof with { });
}

static async Task Stage6_ReplayRejected(DropShieldClient shopper, DemoReport report, string usedToken)
{
    report.Section("6. Replay protection (intentional reuse)");
    using var replay = await shopper.PostCartAsync(ProductId, 1, usedToken, CancellationToken.None);
    report.Line(
        "Replay of used action proof",
        replay.StatusCode == HttpStatusCode.Conflict ? "rejected (409 action_already_used)" : $"unexpected {(int)replay.StatusCode}");
}

static async Task Stage7_Reservation(HttpClient dropShield, DemoReport report)
{
    report.Section("7. Inventory reservation");
    using var response = await dropShield.GetAsync("/internal/inventory");
    if (!response.IsSuccessStatusCode)
    {
        report.Line("Reservation snapshot", "unavailable (internal diagnostics disabled?)");
        return;
    }

    var snapshot = await response.Content.ReadFromJsonAsync<InventorySnapshotPayload>();
    if (snapshot is null)
    {
        report.Line("Reservation snapshot", "empty response");
        return;
    }

    report.Line("Available", snapshot.Available.ToString());
    report.Line("Reserved", snapshot.Reserved.ToString());
    report.Line("Committed", snapshot.Committed.ToString());
}

static async Task Stage8_Checkout(DropShieldClient shopper, HttpClient dropShield, DemoReport report)
{
    report.Section("8. Checkout");
    var proof = await shopper.RequestActionProofAsync("checkout", CancellationToken.None);
    report.Line("Checkout action proof", proof.IsSuccess ? "issued" : $"failed ({(int)proof.StatusCode})");
    if (!proof.IsSuccess || proof.Token is null)
    {
        return;
    }

    using var checkout = await shopper.PostCheckoutAsync(ProductId, proof.Token, CancellationToken.None);
    report.Line("Checkout", checkout.IsSuccessStatusCode ? "accepted" : $"unexpected {(int)checkout.StatusCode}");

    using var inventory = await dropShield.GetAsync("/internal/inventory");
    if (inventory.IsSuccessStatusCode)
    {
        var snapshot = await inventory.Content.ReadFromJsonAsync<InventorySnapshotPayload>();
        if (snapshot is not null)
        {
            report.Line("Reservation after checkout", $"available {snapshot.Available}, reserved {snapshot.Reserved}, committed {snapshot.Committed}");
        }
    }
}

static async Task Stage9_FinalMetrics(HttpClient dropShield, DemoReport report)
{
    report.Section("9. Final metrics");
    using var response = await dropShield.GetAsync("/internal/metrics");
    if (!response.IsSuccessStatusCode)
    {
        report.Line("Metrics", "unavailable (internal diagnostics disabled?)");
        return;
    }

    var snapshot = await response.Content.ReadFromJsonAsync<MetricsSummaryPayload>();
    if (snapshot is null)
    {
        report.Line("Metrics", "empty response");
        return;
    }

    report.Line("Incoming", snapshot.Traffic.Incoming.ToString());
    report.Line("Forwarded", snapshot.Traffic.Forwarded.ToString());
    report.Line("Rate limited", snapshot.Traffic.RateLimited.ToString());
    report.Line("Admitted", snapshot.Admission.Admitted.ToString());
    report.Line("Waiting", snapshot.Admission.Waiting.ToString());
    report.Line("Action proofs issued (cart)", snapshot.ActionProofs.CartTokensIssued.ToString());
    report.Line("Action proofs issued (checkout)", snapshot.ActionProofs.CheckoutTokensIssued.ToString());
    report.Line("Replay rejected", snapshot.ActionProofs.ReplayRejected.ToString());
    report.Line("Reservations created", snapshot.InventoryReservations.ReservationsCreated.ToString());
    report.Line("Reservations committed", snapshot.InventoryReservations.ReservationsCommitted.ToString());
    if (snapshot.InventoryReservations.ReservationRejectedOutOfStock > 0)
    {
        report.Line("Out of stock rejections", snapshot.InventoryReservations.ReservationRejectedOutOfStock.ToString());
    }
}

static string DescribeAdmissionOutcome(HttpStatusCode statusCode, bool hasAdmissionProof) => statusCode switch
{
    HttpStatusCode.OK when hasAdmissionProof => "admitted",
    HttpStatusCode.OK => "allowed",
    HttpStatusCode.Accepted => "waiting",
    HttpStatusCode.TooManyRequests => "rate limited",
    _ => $"unexpected {(int)statusCode}",
};

internal sealed record InventorySnapshotPayload(int Available, int Reserved, int Committed);

internal sealed record MetricsSummaryPayload(
    TrafficCountersPayload Traffic,
    AdmissionMetricsPayload Admission,
    ActionProofMetricsPayload ActionProofs,
    InventoryReservationMetricsPayload InventoryReservations);

internal sealed record TrafficCountersPayload(long Incoming, long Forwarded, long RateLimited);

internal sealed record AdmissionMetricsPayload(long Admitted, long Waiting, long QueueFull);

internal sealed record ActionProofMetricsPayload(
    long CartTokensIssued,
    long CheckoutTokensIssued,
    long ReplayRejected);

internal sealed record InventoryReservationMetricsPayload(
    long ReservationsCreated,
    long ReservationsCommitted,
    long ReservationRejectedOutOfStock);
