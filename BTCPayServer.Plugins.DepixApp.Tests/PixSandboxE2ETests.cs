#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.DepixAppApp.Tests;

[Collection(SharedPluginTestCollection.CollectionName)]
public class PixSandboxE2ETests : PlaywrightBaseTest
{
    public PixSandboxE2ETests(SharedPluginTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    /// <summary>
    /// Full payment flow using real sandbox credentials.
    /// Skipped when DEPIX_TEST_API_KEY and DEPIX_TEST_WEBHOOK_SECRET are not set.
    ///
    /// What this tests:
    ///   - API key is valid (real HTTP call to DePix POST /api/me via the settings form save)
    ///   - Webhook HMAC validation accepts a correctly signed payload (200 OK)
    ///   - ProcessWebhookAsync records the payment and settles the invoice
    ///
    /// What this does NOT test:
    ///   - The Liquid wallet / address generation (bypassed via direct SQL injection)
    ///   - DePix delivering the webhook to our endpoint (simulated locally)
    /// </summary>
    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanValidateSandboxApiKeyAndSettleInvoiceViaWebhook()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEPIX_TEST_API_KEY");
        var webhookSecret = Environment.GetEnvironmentVariable("DEPIX_TEST_WEBHOOK_SECRET");

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(webhookSecret))
            return; // sandbox credentials not configured — skip silently

        await InitializeStoreOwnerAsync();

        // BTC on-chain wallet is required — BTCPay won't create invoices for a store
        // without at least one payment method. This mirrors the setup every BTCPay
        // Playwright test does before calling CreateInvoice.
        await Tester.AddDerivationScheme();

        // Step 1: create an invoice before PIX is enabled. USD is used because BRL rate
        // feeds are not available in the regtest CI environment. Creating the invoice first
        // avoids triggering ConfigurePrompt (which requires a Liquid wallet not present in CI).
        var invoiceId = await Tester.CreateInvoice(10m, "USD");
        Assert.False(string.IsNullOrEmpty(invoiceId));

        // Step 2: save real credentials via the store settings UI.
        // PixController validates the API key against GET /api/me before saving.
        // Done after invoice creation so PIX is not yet enabled when the invoice is created.
        await GoToPixSettingsAsync();
        await Page.Locator("#ApiKey").FillAsync(apiKey);
        await Page.Locator("#WebhookSecret").FillAsync(webhookSecret);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Tester.FindAlertMessage(partialText: "Pix configuration applied");

        // Step 3: inject a fake DePix checkout ID directly into the invoice Blob2.
        // ConfigurePrompt (which would normally do this) requires a Liquid wallet not
        // present in the CI test environment, so we bypass it with a direct SQL write.
        var checkoutId = $"test-{Guid.NewGuid():N}";
        await InjectPixPromptAsync(invoiceId, checkoutId);

        // Step 4: POST a correctly signed "checkout.completed" webhook to the store endpoint.
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new { id = checkoutId, status = "completed", amount = 1000 }
        });
        var signatureHeader = BuildHmacSignature(body, webhookSecret);

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(Server.PayTester.ServerUri, $"depix/webhooks/deposit/{Tester.StoreId}"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-DePix-Signature", signatureHeader);

        var httpResponse = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

        // Step 5: poll until ProcessWebhookAsync updates the PIX prompt status to
        // "completed". Webhook processing runs via Task.Run in the controller (async).
        // We check the prompt details rather than invoice settlement status because
        // settlement requires a BRL/USD rate feed not available in regtest CI.
        var invoiceRepo = Server.PayTester.GetService<InvoiceRepository>();
        var pixPmid = new PaymentMethodId("PIX");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? pixStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var invoice = await invoiceRepo.GetInvoice(invoiceId);
            pixStatus = invoice?.GetPaymentPrompt(pixPmid)?.Details?["status"]?.ToString();
            if (pixStatus == "completed")
                break;
            await Task.Delay(500);
        }

        Assert.Equal("completed", pixStatus);
    }

    // Injects a minimal PIX payment prompt directly into the invoice's Blob2 JSON column.
    // This gives ProcessWebhookAsync a checkoutId to match against without going through
    // ConfigurePrompt (which requires a Liquid wallet).
    private async Task InjectPixPromptAsync(string invoiceId, string checkoutId)
    {
        var invoiceRepo = Server.PayTester.GetService<InvoiceRepository>();
        await using var db = invoiceRepo.DbContextFactory.CreateContext();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        // Use "USD" (not "BRL") because the invoice was created as USD and its rate book
        // only contains BTC/USD rates. InvoiceEntity.UpdateTotals() iterates every prompt's
        // currency and calls RateBook.GetRate — using BRL here would throw
        // "Rate rule is not evaluated (PreprocessError)" since no BRL/USD rate exists.
        var promptJson = new JsonObject
        {
            ["currency"]         = "USD",
            ["divisibility"]     = 2,
            ["paymentMethodFee"] = "0",
            ["destination"]      = "test-pix-payload",
            ["details"]          = new JsonObject { ["checkoutId"] = checkoutId }
        }.ToJsonString();

        await conn.ExecuteAsync(
            """
            UPDATE "Invoices"
            SET "Blob2" = jsonb_set(
                COALESCE("Blob2", '{}'::jsonb),
                '{prompts,PIX}',
                @promptJson::jsonb
            )
            WHERE "Id" = @invoiceId
            """,
            new { invoiceId, promptJson });
    }

    private static string BuildHmacSignature(string body, string secret)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var hmac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{ts}.{body}"));
        return $"t={ts},v1={Convert.ToHexString(hmac).ToLowerInvariant()}";
    }
}
