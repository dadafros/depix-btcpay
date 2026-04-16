#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Plugins.Depix.Data.Enums;
using BTCPayServer.Plugins.Depix.Data.Models;
using BTCPayServer.Plugins.Depix.Data.Models.ViewModels;
using BTCPayServer.Plugins.Depix.Errors;
using BTCPayServer.Plugins.Depix.PaymentHandlers;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Depix.Services;

/// <summary>
/// Service for managing DePix integration
/// </summary>
public class DepixService(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    ILogger<PixPaymentMethodHandler> logger,
    ApplicationDbContextFactory dbFactory,
    EventAggregator events,
    DisplayFormatter displayFormatter,
    ISettingsRepository settingsRepository
)
{

    /// <summary>
    /// Source of the DePix configuration
    /// </summary>
    public enum DepixConfigSource
    {
        /// <summary>
        /// Not configured
        /// </summary>
        None,
        /// <summary>
        /// Configured at store level
        /// </summary>
        Store,
        /// <summary>
        /// Configured at server level
        /// </summary>
        Server
    }

    /// <summary>
    /// Effective DePix configuration
    /// </summary>
    public record EffectivePixConfig(
        DepixConfigSource Source,
        string? EncryptedApiKey,
        string? EncryptedWebhookSecret);

    /// <summary>
    /// Checks if DePix is enabled for a store
    /// </summary>
    /// <param name="storeId">The store ID</param>
    /// <returns>True if enabled, false otherwise</returns>
    public async Task<bool> IsDePixEnabled(string storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            if (storeData == null)
            {
                logger.LogWarning($"[DePix] Store {storeId} not found.");
                return false;
            }

            var paymentMethods = storeData.GetPaymentMethodConfigs();

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var isConfigured = paymentMethods.ContainsKey(DePixPlugin.DePixPmid);
            var isExcluded = excludeFilters.Match(DePixPlugin.DePixPmid);

            return isConfigured && !isExcluded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"[DePix] Error checking if {DePixPlugin.DePixPmid} is enabled for store {storeId}");
            return false;
        }
    }

    /// <summary>
    /// Gets the Pix configuration status for a store
    /// </summary>
    /// <param name="storeId">The store ID</param>
    /// <returns>The Pix configuration status</returns>
    public async Task<PixConfigStatus> GetPixConfigStatus(string storeId)
    {
        var store = await storeRepository.FindStore(storeId);
        if (store is null)
            return new PixConfigStatus(DePixActive: false, PixEnabled: false, ApiKeyConfigured: false);

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();

        var dePixActive = await IsDePixEnabled(storeId);

        var pixCfg = store.GetPaymentMethodConfig<PixPaymentMethodConfig>(DePixPlugin.PixPmid, handlers);
        var effectiveConfig = await GetEffectiveConfigAsync(pixCfg);

        var pixEnabled       = pixCfg?.IsEnabled == true;
        var apiKeyConfigured = effectiveConfig.Source != DepixConfigSource.None;

        return new PixConfigStatus(dePixActive, pixEnabled, apiKeyConfigured);
    }

    /// <summary>
    /// Checks if Pix is enabled for a store
    /// </summary>
    /// <param name="storeId">The store ID</param>
    /// <returns>True if enabled, false otherwise</returns>
    public async Task<bool> IsPixEnabled(string storeId)
    {
        var store = await storeRepository.FindStore(storeId);
        if (store is null) return false;

        if (!await IsDePixEnabled(storeId)) return false;

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();

        var cfg = store.GetPaymentMethodConfig<PixPaymentMethodConfig>(DePixPlugin.PixPmid, handlers);
        return cfg is not null && cfg.IsEnabled;
    }

    /// <summary>
    /// Generates a fresh DePix address for a store
    /// </summary>
    /// <param name="storeId">The store ID</param>
    /// <returns>The generated address</returns>
    /// <exception cref="PixPluginException">Thrown if store or network not configured</exception>
    /// <exception cref="PixPaymentException">Thrown if wallet not configured</exception>
    public async Task<string> GenerateFreshDePixAddress(string storeId)
    {
        var walletProvider = serviceProvider.GetRequiredService<BTCPayWalletProvider>();
        var networkProvider = serviceProvider.GetRequiredService<BTCPayNetworkProvider>();

        var store = await storeRepository.FindStore(storeId);
        if (store == null)
            throw new PixPluginException("Store not found");


        var depixNetwork = networkProvider.GetNetwork<ElementsBTCPayNetwork>(DePixPlugin.DePixCryptoCode);
        if (depixNetwork == null)
            throw new PixPluginException("DePix asset network not configured");


        var wallet = walletProvider.GetWallet(depixNetwork);
        if (wallet == null)
            throw new PixPaymentException("DePix wallet not configured");


        const string generatedBy = "invoice";
        var handlers = serviceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();

        var derivationSettings = store.GetDerivationSchemeSettings(handlers, DePixPlugin.DePixCryptoCode, onlyEnabled: true);
        if (derivationSettings == null)
            throw new PixPluginException("DePix derivation scheme not configured for this store.");

        var addressData = await wallet.ReserveAddressAsync(storeId, derivationSettings.AccountDerivation, generatedBy);
        var address = addressData.Address.ToString();

        return address;
    }

    /// <summary>
    /// Creates a DePix API client
    /// </summary>
    /// <param name="apiKey">The API key</param>
    /// <returns>The HttpClient</returns>
    public HttpClient CreateDepixClient(string apiKey)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://depix-backend.vercel.app/api/");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    /// <summary>
    /// Validates a DePix API key via GET /api/me
    /// </summary>
    /// <param name="apiKey">The API key to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The validation response</returns>
    public async Task<ApiKeyValidationResponse> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        try
        {
            var client = CreateDepixClient(apiKey);
            using var req  = new HttpRequestMessage(HttpMethod.Get, "me");
            using var resp = await client.SendAsync(req, ct);

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new ApiKeyValidationResponse(false, "Invalid API key (401/403).");

            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                var msg  = $"Invalid API key ({code}).";
                return new ApiKeyValidationResponse(false, msg);
            }

            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("merchant_id", out _))
            {
                return new ApiKeyValidationResponse(true, "OK");
            }

            return new ApiKeyValidationResponse(false, "API responded but did not return merchant info.");
        }
        catch (TaskCanceledException)
        {
            return new ApiKeyValidationResponse(false, "Timed out reaching DePix API. Try again.");
        }
        catch (HttpRequestException)
        {
            return new ApiKeyValidationResponse(false, "Network error contacting DePix API.");
        }
        catch
        {
            return new ApiKeyValidationResponse(false, "API key validation failed due to an unexpected error.");
        }
    }

    /// <summary>
    /// Creates a new checkout via POST /api/checkouts
    /// </summary>
    /// <param name="client">Authenticated HttpClient</param>
    /// <param name="amountInCents">Amount in cents</param>
    /// <param name="description">Payment description</param>
    /// <param name="callbackUrl">Webhook callback URL for this checkout</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The checkout response</returns>
    /// <exception cref="PaymentMethodUnavailableException">Thrown if request fails</exception>
    public async Task<DepixCheckoutResponse> RequestCheckoutAsync(HttpClient client, int amountInCents, string description, string callbackUrl, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["amount"] = amountInCents,
            ["description"] = description,
            ["callback_url"] = callbackUrl
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("checkouts", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync(ct);
            throw new PaymentMethodUnavailableException(
                $"Failed to create DePix checkout: HTTP {(int)resp.StatusCode} — {errorBody[..Math.Min(500, errorBody.Length)]}");
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var id         = root.GetProperty("id").GetString();
        var paymentUrl = root.GetProperty("payment_url").GetString();
        var pixPayload = root.GetProperty("pix").GetProperty("qr_code").GetString();
        var expiresAt  = root.GetProperty("expires_at").GetString();

        if (string.IsNullOrEmpty(id))
            throw new PaymentMethodUnavailableException("DePix response did not include id");
        if (string.IsNullOrEmpty(pixPayload))
            throw new PaymentMethodUnavailableException("DePix response did not include pix_payload");

        return new DepixCheckoutResponse(id!, paymentUrl ?? "", pixPayload!, expiresAt ?? "");
    }

    /// <summary>
    /// Applies the checkout details to the payment prompt
    /// </summary>
    /// <param name="context">The payment method context</param>
    /// <param name="checkoutResponse">The checkout response from DePix</param>
    /// <param name="depixAddress">The DePix address</param>
    public void ApplyPromptDetails(PaymentMethodContext context, DepixCheckoutResponse checkoutResponse, string depixAddress)
    {
        context.Prompt.Destination = checkoutResponse.PixPayload;

        var details = context.Prompt.Details is null
            ? new DePixPaymentMethodDetails()
            : context.Handler.ParsePaymentPromptDetails(context.Prompt.Details) as DePixPaymentMethodDetails ??
              new DePixPaymentMethodDetails();

        details.CheckoutId = checkoutResponse.Id;
        details.PaymentUrl = checkoutResponse.PaymentUrl;
        details.PixPayload = checkoutResponse.PixPayload;
        details.ExpiresAt = checkoutResponse.ExpiresAt;
        details.DepixAddress = depixAddress;

        context.Prompt.Details = JToken.FromObject(details, context.Handler.Serializer);
    }

    /// <summary>
    /// Loads Pix transactions for a store
    /// </summary>
    /// <param name="query">The query parameters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A list of Pix transactions</returns>
    public async Task<List<PixTxResponse>> LoadPixTransactionsAsync(PixTxQueryRequest query, CancellationToken ct)
    {
        await using var db = invoiceRepository.DbContextFactory.CreateContext();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var where = new List<string>
        {
            "\"StoreDataId\" = @storeId",
            // Include both new (checkoutId) and legacy (qrId) invoices — outer parens required by PostgreSQL operator precedence
            "((\"Blob2\"->'prompts'->'PIX'->'details'->>'checkoutId') IS NOT NULL OR (\"Blob2\"->'prompts'->'PIX'->'details'->>'qrId') IS NOT NULL)"
        };
        if (!string.IsNullOrWhiteSpace(query.Status))
            where.Add("\"Blob2\"->'prompts'->'PIX'->'details'->>'status' = @status");
        if (query.From is not null)
            where.Add("\"Created\" >= @fromUtc");
        if (query.To is not null)
            where.Add("\"Created\" <= @toUtc");
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            where.Add("""
                      (
                          "Id" ILIKE '%' || @search || '%'
                          OR ("Blob2"->'prompts'->'PIX'->'details'->>'checkoutId') ILIKE '%' || @search || '%'
                          OR ("Blob2"->'prompts'->'PIX'->'details'->>'depixAddress') ILIKE '%' || @search || '%'
                      )
                      """);
        }

        var sql = $"""
                   SELECT
                     "Id" AS "InvoiceId",
                     "Created"::timestamptz AS "Created",
                     COALESCE(("Blob2"->'prompts'->'PIX'->'details'->>'checkoutId'), ("Blob2"->'prompts'->'PIX'->'details'->>'qrId')) AS "QrId",
                     ("Blob2"->'prompts'->'PIX'->'details'->>'depixAddress')   AS "DepixAddress",
                     NULLIF(("Blob2"->'prompts'->'PIX'->'details'->>'amount'), '')::int AS "ValueInCents",
                     COALESCE(("Blob2"->'prompts'->'PIX'->'details'->>'status'), 'pending')   AS "DepixStatusRaw"
                   FROM "Invoices"
                   WHERE {string.Join(" AND ", where)}
                   ORDER BY "Created" DESC
                   LIMIT 200;
                   """;

        var args = new
        {
            storeId = query.StoreId,
            status  = query.Status,
            search  = query.SearchTerm,
            fromUtc = query.From?.UtcDateTime,
            toUtc   = query.To?.UtcDateTime
        };

        var rows = await conn.QueryAsync<PixTxResponse>(
            new CommandDefinition(sql, args, cancellationToken: ct));

        var transactions = rows.ToList();
        foreach (var transaction in transactions)
        {
            transaction.DepixStatus = DepixStatusExtensions.TryParse(transaction.DepixStatusRaw, out var st) ? st : null;
            transaction.AmountBrl = transaction.ValueInCents is { } v ? displayFormatter.Currency(v / 100m, "BRL") : "-";
        }

        return transactions;
    }

    /// <summary>
    /// Processes a webhook for a specific store
    /// </summary>
    /// <param name="storeId">The store ID</param>
    /// <param name="payload">The webhook payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ProcessWebhookAsync(string storeId, DepixWebhookPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var checkoutId = payload.Data.Id;
            var invoiceId = await FindInvoiceByCheckoutIdAsync(checkoutId, storeId, cancellationToken);
            if (invoiceId is null)
            {
                logger.LogWarning("DePix webhook: invoice not found for store {StoreId} and checkoutId {CheckoutId}", storeId, checkoutId);
                return;
            }

            await ProcessWebhookForInvoiceAsync(invoiceId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DePix webhook processing failed for store {StoreId}", storeId);
        }
    }

    /// <summary>
    /// Processes a webhook (server-level or global)
    /// </summary>
    /// <param name="payload">The webhook payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ProcessWebhookAsync(DepixWebhookPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var checkoutId = payload.Data.Id;
            var invoiceId = await FindInvoiceByCheckoutIdAsync(checkoutId, storeId: null, cancellationToken);
            if (invoiceId is null)
            {
                logger.LogWarning("DePix webhook (server): invoice not found for checkoutId {CheckoutId}", checkoutId);
                return;
            }

            await ProcessWebhookForInvoiceAsync(invoiceId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DePix webhook (server) processing failed");
        }
    }

    private async Task ProcessWebhookForInvoiceAsync(string invoiceId, DepixWebhookPayload payload, CancellationToken cancellationToken)
    {
        var entity = await invoiceRepository.GetInvoice(invoiceId);
        if (entity is null)
        {
            logger.LogWarning("DePix webhook: invoice entity null for {InvoiceId}", invoiceId);
            return;
        }

        var pmid = DePixPlugin.PixPmid;
        var pixPrompt = entity.GetPaymentPrompt(pmid);
        if (pixPrompt is null)
        {
            logger.LogWarning("DePix webhook: PIX prompt not found on invoice {InvoiceId}", invoiceId);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        if (!handlers.TryGetValue(pmid, out var handler))
        {
            logger.LogWarning("DePix webhook: PIX handler missing for invoice {InvoiceId}", invoiceId);
            return;
        }

        var data = payload.Data;
        var details = pixPrompt.Details is null
            ? new DePixPaymentMethodDetails()
            : handler.ParsePaymentPromptDetails(pixPrompt.Details) as DePixPaymentMethodDetails ??
              new DePixPaymentMethodDetails();

        if (data.Status is not null) details.Status = data.Status;
        if (data.Amount is not null) details.Amount = data.Amount;
        if (data.BlockchainTxId is not null) details.BlockchainTxId = data.BlockchainTxId;

        await invoiceRepository.UpdatePaymentDetails(invoiceId, handler, details);

        if (DepixStatusExtensions.TryParse(data.Status, out var depixStatus))
        {
            if (depixStatus != DepixStatus.Completed)
            {
                await ApplyStatusAndNotifyAsync(invoiceId, depixStatus);
                return;
            }

            await TryRecordPaymentAsync(entity, pixPrompt, data, depixStatus);
        }
    }

    private async Task TryRecordPaymentAsync(InvoiceEntity entity, PaymentPrompt pixPrompt, DepixWebhookData data,
        DepixStatus depixStatus)
    {
        if (depixStatus != DepixStatus.Completed)
            return;

        if (string.IsNullOrWhiteSpace(data.Id))
        {
            logger.LogWarning("DePix webhook: completed without checkout id for invoice {InvoiceId}", entity.Id);
            return;
        }

        var amount = data.Amount ?? pixPrompt.Calculate().TotalDue;
        if (amount <= 0m)
        {
            logger.LogWarning("DePix webhook: completed with invalid amount {Amount} for invoice {InvoiceId}", amount, entity.Id);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var paymentService = scope.ServiceProvider.GetRequiredService<PaymentService>();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        if (!handlers.TryGetValue(DePixPlugin.PixPmid, out var handler))
        {
            logger.LogWarning("DePix webhook: completed but PIX handler missing for invoice {InvoiceId}", entity.Id);
            return;
        }

        var paymentId = $"{entity.Id}:{data.Id}";
        var paymentData = new PaymentData
        {
            Id = paymentId,
            Created = DateTimeOffset.UtcNow,
            Status = PaymentStatus.Settled,
            Currency = pixPrompt.Currency,
            InvoiceDataId = entity.Id,
            Amount = amount
        }.Set(entity, handler, BuildPaymentData(data));

        var payment = await paymentService.AddPayment(paymentData, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { paymentId });
        if (payment is null)
        {
            // Payment already recorded — ensure invoice reaches Settled state.
            await ApplyStatusAndNotifyAsync(entity.Id, DepixStatus.Completed);
            return;
        }

        var invoice = await invoiceRepository.GetInvoice(entity.Id);
        if (invoice is not null)
            events.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
    }

    private static DePixPaymentData BuildPaymentData(DepixWebhookData data)
    {
        return new DePixPaymentData
        {
            CheckoutId = data.Id,
            BlockchainTxId = data.BlockchainTxId,
            Status = data.Status,
            Amount = data.Amount
        };
    }

    private async Task ApplyStatusAndNotifyAsync(string invoiceId, DepixStatus depix)
    {
        var entity = await invoiceRepository.GetInvoice(invoiceId);
        if (entity is null) return;

        var current  = entity.GetInvoiceState();
        var newState = depix.ToInvoiceState(current);
        if (newState is null) return;

        await invoiceRepository.UpdateInvoiceStatus(invoiceId, newState);

        events.Publish(new InvoiceNeedUpdateEvent(invoiceId));
        events.Publish(new InvoiceDataChangedEvent(entity));
    }

    /// <summary>
    /// Gets the Pix configuration for a store
    /// </summary>
    /// <param name="store">The store data</param>
    /// <param name="handlers">The payment method handlers</param>
    /// <returns>The Pix configuration or null</returns>
    public PixPaymentMethodConfig? GetPixConfig(StoreData store, PaymentMethodHandlerDictionary handlers)
    {
        var pmid = DePixPlugin.PixPmid;
        var configs = store.GetPaymentMethodConfigs();

        if (!configs.TryGetValue(pmid, out var raw))
            return null;

        if (!handlers.TryGetValue(pmid, out var handlerObj) || handlerObj is not PixPaymentMethodHandler handler)
            return null;

        return handler.ParsePaymentMethodConfig(raw) as PixPaymentMethodConfig;
    }

    /// <summary>
    /// Gets the server-level DePix configuration
    /// </summary>
    /// <returns>The server configuration</returns>
    public async Task<PixServerConfig> GetServerConfigAsync()
    {
        return await settingsRepository.GetSettingAsync<PixServerConfig>() ?? new PixServerConfig();
    }

    /// <summary>
    /// Validates the configuration
    /// </summary>
    /// <param name="encryptedApiKey">The encrypted API key</param>
    /// <param name="encryptedWebhookSecret">The encrypted webhook secret</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsConfigValid(string? encryptedApiKey, string? encryptedWebhookSecret)
        => !string.IsNullOrEmpty(encryptedApiKey) && !string.IsNullOrEmpty(encryptedWebhookSecret);

    /// <summary>
    /// Gets the effective configuration (merging store and server configs)
    /// </summary>
    /// <param name="storeCfg">The store configuration</param>
    /// <returns>The effective configuration</returns>
    public async Task<EffectivePixConfig> GetEffectiveConfigAsync(PixPaymentMethodConfig? storeCfg)
    {
        if (storeCfg is not null && IsConfigValid(storeCfg.EncryptedApiKey, storeCfg.EncryptedWebhookSecret))
        {
            return new EffectivePixConfig(
                DepixConfigSource.Store,
                storeCfg.EncryptedApiKey,
                storeCfg.EncryptedWebhookSecret);
        }

        var serverCfg = await GetServerConfigAsync();
        if (IsConfigValid(serverCfg.EncryptedApiKey, serverCfg.EncryptedWebhookSecret))
        {
            return new EffectivePixConfig(
                DepixConfigSource.Server,
                serverCfg.EncryptedApiKey,
                serverCfg.EncryptedWebhookSecret);
        }

        return new EffectivePixConfig(
            DepixConfigSource.None,
            null,
            null);
    }

    private async Task<string?> FindInvoiceByCheckoutIdAsync(string checkoutId, string? storeId, CancellationToken ct)
    {
        await using var db = dbFactory.CreateContext();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string sql = """
                           SELECT "Id"
                           FROM "Invoices"
                           WHERE (@storeId IS NULL OR "StoreDataId" = @storeId)
                             AND (
                                 ("Blob2"->'prompts'->'PIX'->'details'->>'checkoutId') = @checkoutId
                                 OR ("Blob2"->'prompts'->'PIX'->'details'->>'qrId') = @checkoutId
                             )
                           ORDER BY "Created" DESC
                           LIMIT 1;
                           """;

        return await conn.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(sql, new { checkoutId, storeId }, cancellationToken: ct));
    }

    /// <summary>
    /// Initializes the DePix plugin
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="nbxProvider">The NBXplorer network provider</param>
    /// <param name="selectedChains">The selected chains registry</param>
    public void InitDePix(IServiceCollection services, NBXplorerNetworkProvider nbxProvider, SelectedChains selectedChains)
    {
        var lbtc = nbxProvider.GetFromCryptoCode("LBTC");
        if (lbtc is null)
        {
            logger.LogWarning("[DePix] LBTC network not available. Skipping DePix init.");
            return;
        }

        if (!selectedChains.Contains("LBTC"))
            selectedChains.Add("LBTC");

        var chainName = lbtc.NBitcoinNetwork.ChainName;

        var network = new ElementsBTCPayNetwork
        {
            CryptoCode        = DePixPlugin.DePixCryptoCode,
            NetworkCryptoCode = "LBTC",
            ShowSyncSummary   = false,
            DefaultRateRules =
            [
                "BTC_USD = kraken(BTC_USD)",
                "USD_BTC = 1 / BTC_USD",

                "BTC_BRL = binance(BTC_BRL)",
                "BRL_BTC = 1 / BTC_BRL",

                // Swap BRL <-> USD via BTC
                "BRL_USD = BTC_USD / BTC_BRL",
                "USD_BRL = 1 / BRL_USD",

                // DePix (1 DEPIX = 1 BRL)
                "DEPIX_BRL = 1",
                "DEPIX_USD = BRL_USD",
                "USD_DEPIX = USD_BRL",
                "DEPIX_BTC = BRL_BTC",
                "BTC_DEPIX = BTC_BRL"
            ],
            AssetId           = new uint256("02f22f8d9c76ab41661a2729e4752e2c5d1a263012141b86ea98af5472df5189"),
            DisplayName       = DePixPlugin.DePixCryptoCode,
            NBXplorerNetwork  = lbtc,
            CryptoImagePath   = "~/Resources/img/depix.svg",
            DefaultSettings   = BTCPayDefaultSettings.GetDefaultSettings(chainName),
            CoinType          = chainName == ChainName.Mainnet ? new KeyPath("1776'") : new KeyPath("1'"),
            SupportRBF        = true,
            SupportLightning  = false,
            SupportPayJoin    = false,
            VaultSupported    = false,
            ReadonlyWallet    = true
        }.SetDefaultElectrumMapping(chainName);

        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(
                    PaymentTypes.CHAIN.GetPaymentMethodId(DePixPlugin.DePixCryptoCode),
                    new DefaultTransactionLinkProvider(GetLiquidBlockExplorer(chainName)));

        services.AddCurrencyData(new CurrencyData
        {
            Code         = DePixPlugin.DePixCryptoCode,
            Name         = DePixPlugin.DePixCryptoCode,
            Divisibility = 2,
            Symbol       = null,
            Crypto       = true
        });
    }

    private static string GetLiquidBlockExplorer(ChainName chainName)
    {
        return chainName == ChainName.Mainnet
            ? "https://liquid.network/tx/{0}"
            : "https://liquid.network/testnet/tx/{0}";
    }
}
