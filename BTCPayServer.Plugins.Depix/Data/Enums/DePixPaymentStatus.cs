#nullable enable
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.Depix.Data.Enums;
public enum DepixStatus
{
    Pending,
    Processing,
    Completed,
    Expired,
    Cancelled
}

public static class DepixStatusExtensions
{
    public static bool TryParse(string? value, out DepixStatus result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var s = value.Trim().ToLowerInvariant().Replace("-", "_");
        switch (s)
        {
            case "pending":      result = DepixStatus.Pending;    return true;
            case "processing":   result = DepixStatus.Processing; return true;
            case "completed":    result = DepixStatus.Completed;  return true;
            case "expired":      result = DepixStatus.Expired;    return true;
            case "cancelled":    result = DepixStatus.Cancelled;  return true;
            default: return false;
        }
    }

    public static InvoiceState? ToInvoiceState(this DepixStatus s, InvoiceState current)
    {
        return s switch
        {
            DepixStatus.Pending => null,
            DepixStatus.Processing => current.Status == InvoiceStatus.Settled
                ? null
                : new InvoiceState(InvoiceStatus.Processing, InvoiceExceptionStatus.None),
            DepixStatus.Completed => current.Status == InvoiceStatus.Settled
                ? null
                : new InvoiceState(InvoiceStatus.Settled, InvoiceExceptionStatus.None),
            DepixStatus.Expired => current.Status == InvoiceStatus.Settled
                ? null
                : new InvoiceState(InvoiceStatus.Expired, InvoiceExceptionStatus.None),
            DepixStatus.Cancelled => current.Status == InvoiceStatus.Settled
                ? null
                : new InvoiceState(InvoiceStatus.Invalid, InvoiceExceptionStatus.Marked),
            _ => null
        };
    }
}
