namespace BTCPayServer.Plugins.Depix.Data.Models;

public sealed record DepixCheckoutResponse(string Id, string PaymentUrl, string PixPayload, string ExpiresAt);
