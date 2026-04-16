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
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.DepixApp.Tests;

[Collection(SharedPluginTestCollection.CollectionName)]
public class PixSandboxE2ETests : PlaywrightBaseTest
{
    public PixSandboxE2ETests(SharedPluginTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    /// <summary>
    /// Webhook + settlement flow using real sandbox credentials.
    /// Skipped when DEPIX_TEST_API_KEY and DEPIX_TEST_WEBHOOK_SECRET are not set.
    ///
    /// What this tests:
    ///   - Webhook HMAC validation accepts a correctly signed payload (200 OK)
    ///   - ProcessWebhookAsync records the payment and updates the invoice prompt
    ///
    /// What this does NOT test:
    ///   - API key validation via UI (tested separately in PixSettingsTests)
    ///   - The Liquid wallet / address generation (bypassed via direct SQL injection)
    ///   - DePix delivering the webhook to our endpoint (simulated locally)
    /// </summary>
    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanSettleInvoiceViaSignedWebhook()
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

        // Step 2: seed real sandbox credentials directly into the store config.
        // This avoids calling the DePix API (GET /api/me) which is unreliable from CI.
        // API key validation via the settings UI is covered by PixSettingsTests.
        await SeedStorePixConfigWithCredentialsAsync(apiKey, webhookSecret);

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

    private async Task SeedStorePixConfigWithCredentialsAsync(string apiKey, string webhookSecret)
    {
        var storeId = Tester.StoreId ?? throw new InvalidOperationException("Create a store first.");
        var storeRepository = Server.PayTester.GetService<StoreRepository>();
        var store = await storeRepository.FindStore(storeId)
                    ?? throw new InvalidOperationException($"Store {storeId} not found.");

        var config = new JObject
        {
            ["encryptedApiKey"] = ProtectSecret(apiKey),
            ["encryptedWebhookSecret"] = ProtectSecret(webhookSecret),
            ["isEnabled"] = true
        };

        store.SetPaymentMethodConfig(PixPaymentMethodId, config);

        var blob = store.GetStoreBlob();
        blob.SetExcluded(PixPaymentMethodId, false);
        store.SetStoreBlob(blob);

        await storeRepository.UpdateStore(store);
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
