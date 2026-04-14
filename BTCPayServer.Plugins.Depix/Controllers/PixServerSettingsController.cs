#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Depix.Data.Models;
using BTCPayServer.Plugins.Depix.Data.Models.ViewModels;
using BTCPayServer.Plugins.Depix.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Depix.Controllers;

[Route("server/depix")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PixServerSettingsController(
    ISettingsRepository settingsRepository,
    ISecretProtector protector,
    DepixService depixService)
    : Controller
{
    [HttpGet("settings")]
    public async Task<IActionResult> PixServerSettings()
    {
        var cfg = await settingsRepository.GetSettingAsync<PixServerConfig>() ?? new PixServerConfig();

        var webhookUrl = Utils.BuildWebhookUrl(Request);

        var maskedApiKey = "";
        if (!string.IsNullOrEmpty(cfg.EncryptedApiKey))
        {
            var plain = protector.Unprotect(cfg.EncryptedApiKey);
            if (!string.IsNullOrEmpty(plain))
                maskedApiKey = "••••••••" + plain[^4..];
        }

        var maskedWebhookSecret = "";
        if (!string.IsNullOrEmpty(cfg.EncryptedWebhookSecret))
        {
            var plain = protector.Unprotect(cfg.EncryptedWebhookSecret);
            if (!string.IsNullOrEmpty(plain))
                maskedWebhookSecret = "••••••••" + plain[^4..];
        }

        var apiKeyConfigured = !string.IsNullOrEmpty(cfg.EncryptedApiKey);
        var isServerCfgValid = DepixService.IsConfigValid(cfg.EncryptedApiKey, cfg.EncryptedWebhookSecret);

        string webhookSecretDisplay;
        if (isServerCfgValid)
            webhookSecretDisplay = maskedWebhookSecret;
        else
            webhookSecretDisplay = "<not configured>";

        var model = new PixServerSettingsViewModel
        {
            ApiKey = maskedApiKey,
            WebhookSecret = "",
            WebhookUrl = webhookUrl,
            WebhookSecretDisplay = webhookSecretDisplay,
            ApiKeyConfigured = apiKeyConfigured,
            IsServerCfgValid = isServerCfgValid,
        };

        return View(model);
    }

    [HttpPost("settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PixServerSettings(PixServerSettingsViewModel viewModel)
    {
        var cfg = await settingsRepository.GetSettingAsync<PixServerConfig>() ?? new PixServerConfig();

        var newApiKey = !string.IsNullOrWhiteSpace(viewModel.ApiKey) && !viewModel.ApiKey.Contains('•');
        if (newApiKey)
        {
            var candidate = viewModel.ApiKey!.Trim();

            var validationResult = await depixService.ValidateApiKeyAsync(candidate, HttpContext.RequestAborted);
            if (!validationResult.IsValid)
            {
                TempData[WellKnownTempData.ErrorMessage] = validationResult.Message;
                return RedirectToAction(nameof(PixServerSettings));
            }

            cfg.EncryptedApiKey = protector.Protect(candidate);
        }

        if (!string.IsNullOrEmpty(cfg.EncryptedApiKey))
        {
            var newWebhookSecret = !string.IsNullOrWhiteSpace(viewModel.WebhookSecret) && !viewModel.WebhookSecret.Contains('•');
            if (newWebhookSecret)
            {
                cfg.EncryptedWebhookSecret = protector.Protect(viewModel.WebhookSecret!.Trim());
            }
        }
        else
        {
            TempData[WellKnownTempData.ErrorMessage] = "Set the DePix API key first.";
            return RedirectToAction(nameof(PixServerSettings));
        }

        await settingsRepository.UpdateSetting(cfg);

        TempData[WellKnownTempData.SuccessMessage] = "DePix server configuration applied";
        return RedirectToAction(nameof(PixServerSettings));
    }
}
