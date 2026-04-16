#nullable enable
namespace BTCPayServer.Plugins.DepixApp.PaymentHandlers;

public class PixPaymentMethodConfig
{
    /// <summary>
    /// Encrypted API Key for DePix service (sk_live_... / sk_test_...)
    /// </summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>
    /// Encrypted webhook secret from DePix (whsec_...)
    /// </summary>
    public string? EncryptedWebhookSecret { get; set; }

    /// <summary>
    /// Whether the Pix payment method is enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
