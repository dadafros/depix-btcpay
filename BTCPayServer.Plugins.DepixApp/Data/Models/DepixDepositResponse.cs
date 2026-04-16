namespace BTCPayServer.Plugins.DepixApp.Data.Models;

public sealed record DepixCheckoutResponse(string Id, string PaymentUrl, string PixPayload, string ExpiresAt);
