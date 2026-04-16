#nullable enable
namespace BTCPayServer.Plugins.DepixApp.Data.Models;

public class PixServerConfig
{
    public string? EncryptedApiKey { get; set; }
    public string? EncryptedWebhookSecret { get; set; }
}
