#nullable enable
namespace BTCPayServer.Plugins.DepixApp.Data.Models.ViewModels;

public class PixServerSettingsViewModel
{
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretDisplay { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public bool IsServerCfgValid { get; set; }
}
