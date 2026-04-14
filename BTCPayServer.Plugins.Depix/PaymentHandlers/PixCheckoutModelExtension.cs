using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Depix.PaymentHandlers;
public class PixCheckoutModelExtension : ICheckoutModelExtension
{
    private const string CheckoutBodyComponentName = "PixCheckout";
    public PaymentMethodId PaymentMethodId => DePixPlugin.PixPmid;
    public string Image => "Resources/img/depix.svg";
    public string Badge => "🇧🇷";
    
    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context is not { Handler: PixPaymentMethodHandler })
            return;

        context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;
        context.Model.ExpirationSeconds = 900;
        context.Model.Activated = true;
        // Fall back to legacy "copyPaste" key for invoices created before the migration
        context.Model.InvoiceBitcoinUrl = context.Prompt.Details["pixPayload"]?.ToString()
                                       ?? context.Prompt.Details["copyPaste"]?.ToString();
        context.Model.InvoiceBitcoinUrlQR = context.Prompt.Destination;
        context.Model.ShowPayInWalletButton = false;
        context.Model.CelebratePayment = true;
    }
}