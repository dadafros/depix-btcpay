#nullable enable
using System.Text.Json.Serialization;

namespace BTCPayServer.Plugins.Depix.Data.Models;

public sealed class DepixWebhookPayload
{
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("data")] public DepixWebhookData Data { get; set; } = new();
}

public sealed class DepixWebhookData
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("amount")] public decimal? Amount { get; set; }
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }
    [JsonPropertyName("blockchain_tx_id")] public string? BlockchainTxId { get; set; }
    [JsonPropertyName("metadata")] public DepixWebhookMetadata? Metadata { get; set; }
}

public sealed class DepixWebhookMetadata
{
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
}
