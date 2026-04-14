#nullable enable
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Depix.Data.Models;
using BTCPayServer.Plugins.Depix.Data.Models.ViewModels;
using BTCPayServer.Plugins.Depix.PaymentHandlers;
using BTCPayServer.Plugins.Depix.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Depix.Controllers;

[Route("stores/{storeId}/pix")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PixController(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers,
    ISecretProtector protector,
    DepixService depixService)
    : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet("settings")]
    public async Task<IActionResult> PixSettings([FromQuery] string? walletId)
    {
        var pmid = DePixPlugin.PixPmid;
        var cfg = StoreData.GetPaymentMethodConfig<PixPaymentMethodConfig>(pmid, handlers);
        var webhookUrl = Utils.BuildWebhookUrl(Request, StoreData.Id);

        var serverCfg = await depixService.GetServerConfigAsync();
        var isServerCfgValid = DepixService.IsConfigValid(serverCfg.EncryptedApiKey, serverCfg.EncryptedWebhookSecret);
        var isStoreCfgValid = DepixService.IsConfigValid(cfg?.EncryptedApiKey, cfg?.EncryptedWebhookSecret);
        var effectiveCfg = await depixService.GetEffectiveConfigAsync(cfg);
        var effectiveUsesServer = effectiveCfg.Source == DepixService.DepixConfigSource.Server;

        var maskedApiKey = "";
        if (!string.IsNullOrEmpty(cfg?.EncryptedApiKey))
        {
            var plain = protector.Unprotect(cfg.EncryptedApiKey);
            if (!string.IsNullOrEmpty(plain))
                maskedApiKey = "••••••••" + plain[^4..];
        }

        var maskedWebhookSecret = "";
        if (!string.IsNullOrEmpty(cfg?.EncryptedWebhookSecret))
        {
            var plain = protector.Unprotect(cfg.EncryptedWebhookSecret);
            if (!string.IsNullOrEmpty(plain))
                maskedWebhookSecret = "••••••••" + plain[^4..];
        }

        string webhookSecretDisplay;
        if (isStoreCfgValid)
            webhookSecretDisplay = maskedWebhookSecret;
        else if (effectiveUsesServer)
            webhookSecretDisplay = "<using server configuration>";
        else
            webhookSecretDisplay = "<not configured>";

        var model = new PixStoreViewModel
        {
            IsEnabled = cfg is { IsEnabled: true },
            ApiKey = maskedApiKey,
            WebhookSecret = "",
            WebhookUrl = webhookUrl,
            WebhookSecretDisplay = webhookSecretDisplay,
            IsStoreCfgValid = isStoreCfgValid,
            IsServerCfgValid = isServerCfgValid,
            EffectiveUsesServerConfig = effectiveUsesServer
        };
        return View(model);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> PixSettings(PixStoreViewModel viewModel, [FromQuery] string? walletId)
    {
        var pmid  = DePixPlugin.PixPmid;
        var store = StoreData;
        var blob  = store.GetStoreBlob();

        var cfg = store.GetPaymentMethodConfig<PixPaymentMethodConfig>(pmid, handlers)
                  ?? new PixPaymentMethodConfig();

        var newApiKey = !string.IsNullOrWhiteSpace(viewModel.ApiKey) && !viewModel.ApiKey.Contains('•');
        if (newApiKey)
        {
            var candidate = viewModel.ApiKey!.Trim();

            var validationResult = await depixService.ValidateApiKeyAsync(candidate, HttpContext.RequestAborted);
            if (!validationResult.IsValid)
            {
                TempData[WellKnownTempData.ErrorMessage] = validationResult.Message;
                return RedirectToAction(nameof(PixSettings), new { walletId });
            }
            cfg.EncryptedApiKey = protector.Protect(candidate);
        }

        var newWebhookSecret = !string.IsNullOrWhiteSpace(viewModel.WebhookSecret) && !viewModel.WebhookSecret.Contains('•');
        if (newWebhookSecret)
        {
            cfg.EncryptedWebhookSecret = protector.Protect(viewModel.WebhookSecret!.Trim());
        }

        var effective = await depixService.GetEffectiveConfigAsync(cfg);
        var effectiveConfigured = effective.Source != DepixService.DepixConfigSource.None;
        var requestedEnable = newApiKey || viewModel.IsEnabled;
        var willEnable = requestedEnable && effectiveConfigured;

        if (requestedEnable && !effectiveConfigured)
            TempData[WellKnownTempData.ErrorMessage] =
                "Cannot enable DePix: neither store nor server configuration is complete (API key + webhook secret).";

        cfg.IsEnabled = willEnable;
        store.SetPaymentMethodConfig(handlers[pmid], cfg);
        blob.SetExcluded(pmid, !willEnable);
        store.SetStoreBlob(blob);

        await storeRepository.UpdateStore(store);

        TempData[WellKnownTempData.SuccessMessage] = "Pix configuration applied";
        return RedirectToAction(nameof(PixSettings), new { storeId = StoreData.Id, walletId });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> PixTransactions([FromRoute] string storeId, [FromQuery] PixTxQueryRequest query, CancellationToken ct)
    {
        query.StoreId = storeId;
        var depixWalletId = new WalletId(query.StoreId, DePixPlugin.DePixCryptoCode);

        var model = new PixTransactionsViewModel
        {
            StoreId = query.StoreId,
            WalletId = depixWalletId.ToString(),
            Transactions = await depixService.LoadPixTransactionsAsync(query, ct),
            ConfigStatus = await depixService.GetPixConfigStatus(query.StoreId)
        };

        ViewData["StatusFilter"] = query.Status;
        ViewData["Query"]        = query.SearchTerm;
        ViewData["From"]         = query.From?.ToString("yyyy-MM-dd");
        ViewData["To"]           = query.To?.ToString("yyyy-MM-dd");

        return View("PixTransactions", model);
    }
}
