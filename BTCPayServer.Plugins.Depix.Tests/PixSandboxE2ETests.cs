#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests;
using Dapper;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Depix.Tests;

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

        // Step 1: save real credentials via the store settings UI.
        // PixController validates the API key against GET /api/me before saving.
        await GoToPixSettingsAsync();
        await Page.Locator("#ApiKey").FillAsync(apiKey);
        await Page.Locator("#WebhookSecret").FillAsync(webhookSecret);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Tester.FindAlertMessage(partialText: "Pix configuration applied");

        // Step 2: create a 100 BRL invoice.
        var invoiceId = await Tester.CreateInvoice(100m, "BRL");
        Assert.False(string.IsNullOrEmpty(invoiceId));

        // Step 3: inject a fake DePix checkout ID directly into the invoice Blob2.
        // ConfigurePrompt (which would normally do this) requires a Liquid wallet that
        // is not available in the CI test environment, so we bypass it here.
        var checkoutId = $"test-{Guid.NewGuid():N}";
        await InjectPixPromptAsync(invoiceId, checkoutId);

        // Step 4: POST a correctly signed "checkout.completed" webhook to the store endpoint.
        var body = JsonSerializer.Serialize(new
        {
            @event = "checkout.completed",
            data = new { id = checkoutId, status = "completed", amount = 100 }
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

        // Step 5: poll until the invoice reaches Settled status.
        // Webhook processing runs via Task.Run in the controller, so it is async.
        var invoiceRepo = Server.PayTester.GetService<InvoiceRepository>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        InvoiceStatus? finalStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var invoice = await invoiceRepo.GetInvoice(invoiceId);
            finalStatus = invoice?.GetInvoiceState().Status;
            if (finalStatus == InvoiceStatus.Settled)
                break;
            await Task.Delay(500);
        }

        Assert.Equal(InvoiceStatus.Settled, finalStatus);
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

        var promptJson = new JsonObject
        {
            ["currency"]         = "BRL",
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
