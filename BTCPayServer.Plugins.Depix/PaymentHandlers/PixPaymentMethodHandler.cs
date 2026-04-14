#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Plugins.Depix.Errors;
using BTCPayServer.Plugins.Depix.Services;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace BTCPayServer.Plugins.Depix.PaymentHandlers;

/// <summary>
/// Handler for Pix payment method
/// </summary>
public class PixPaymentMethodHandler(
    BTCPayNetworkProvider networkProvider,
    DepixService depixService,
    ISecretProtector secretProtector,
    IHttpContextAccessor httpContextAccessor)
    : IPaymentMethodHandler, IHasNetwork
{
    /// <summary>
    /// The payment method ID for Pix
    /// </summary>
    public PaymentMethodId PaymentMethodId => DePixPlugin.PixPmid;

    /// <summary>
    /// The network associated with this handler
    /// </summary>
    public BTCPayNetwork Network { get; } = networkProvider.GetNetwork<ElementsBTCPayNetwork>("DePix");

    /// <summary>
    /// Called before fetching rates to configure the prompt
    /// </summary>
    /// <param name="context">The payment method context</param>
    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = "BRL";
        context.Prompt.Divisibility = 2;
        context.Prompt.PaymentMethodFee = 0.00m;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Configures the payment prompt with Pix details
    /// </summary>
    /// <param name="context">The payment method context</param>
    /// <exception cref="PaymentMethodUnavailableException">Thrown if configuration is missing or invalid</exception>
    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        var store = context.Store;
        if (ParsePaymentMethodConfig(store.GetPaymentMethodConfigs()[PaymentMethodId]) is not PixPaymentMethodConfig pixCfg)
            throw new PaymentMethodUnavailableException("DePix payment method not configured");

        var effectiveConfig = await depixService.GetEffectiveConfigAsync(pixCfg);
        if (effectiveConfig.Source == DepixService.DepixConfigSource.None)
            throw new PaymentMethodUnavailableException("DePix API key/webhook secret not configured (store or server)");

        var apiKey = secretProtector.Unprotect(effectiveConfig.EncryptedApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new PaymentMethodUnavailableException("DePix API key not configured");

        var amountInBrl = context.Prompt.Calculate().Due;
        var amountInCents = (int)Math.Round(amountInBrl * 100m, MidpointRounding.AwayFromZero);

        using var client = depixService.CreateDepixClient(apiKey);

        var callbackUrl = BuildCallbackUrl(store.Id);
        var description = $"BTCPay Invoice {context.InvoiceEntity.Id}";

        var checkout = await depixService.RequestCheckoutAsync(
            client,
            amountInCents,
            description,
            callbackUrl,
            CancellationToken.None);

        // Reserve address only after checkout is created — avoids orphaned Liquid addresses on API failures.
        // Wrap plugin exceptions so BTCPay treats a missing Liquid wallet as "method unavailable" rather than a hard error.
        string address;
        try
        {
            address = await depixService.GenerateFreshDePixAddress(store.Id);
        }
        catch (Exception ex) when (ex is PixPluginException or PixPaymentException)
        {
            throw new PaymentMethodUnavailableException($"DePix Liquid wallet not configured: {ex.Message}");
        }

        depixService.ApplyPromptDetails(context, checkout, address);
    }

    private string BuildCallbackUrl(string storeId)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
            return Utils.BuildWebhookUrl(httpContext.Request, storeId);

        throw new PaymentMethodUnavailableException(
            "Cannot build absolute webhook callback URL: no HTTP context available. " +
            "Ensure a canonical BTCPay Server URL is configured.");
    }

    /// <summary>
    /// JSON serializer for the handler
    /// </summary>
    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    /// <summary>
    /// Parses payment prompt details from JSON
    /// </summary>
    /// <param name="details">The JSON details</param>
    /// <returns>The parsed DePixPaymentMethodDetails</returns>
    public object ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<DePixPaymentMethodDetails>(Serializer);
    }

    /// <summary>
    /// Parses payment method configuration from JSON
    /// </summary>
    /// <param name="config">The JSON configuration</param>
    /// <returns>The parsed PixPaymentMethodConfig</returns>
    public object ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<PixPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(PixPaymentMethodHandler)}");
    }

    /// <summary>
    /// Parses payment details from JSON
    /// </summary>
    /// <param name="details">The JSON details</param>
    /// <returns>The parsed DePixPaymentData</returns>
    public object ParsePaymentDetails(JToken details)
    {
        return details.ToObject<DePixPaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(PixPaymentMethodHandler)}");
    }
}

/// <summary>
/// Data model for DePix payment data (stored on completed payments)
/// </summary>
public class DePixPaymentData
{
    public string? CheckoutId { get; set; }
    public string? BlockchainTxId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>
/// Details for DePix payment method (stored on payment prompt)
/// </summary>
public class DePixPaymentMethodDetails
{
    public string? CheckoutId { get; set; }
    public string? PaymentUrl { get; set; }
    public string? PixPayload { get; set; }
    public string? ExpiresAt { get; set; }
    public string? DepixAddress { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? BlockchainTxId { get; set; }
}
