#nullable enable
namespace BTCPayServer.Plugins.DepixApp.Data.Models.ViewModels;

public class PixStoreViewModel
{
    public bool IsEnabled { get; set; }
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretDisplay { get; set; }
    public bool IsStoreCfgValid { get; set; }
    public bool IsServerCfgValid { get; set; }
    public bool EffectiveUsesServerConfig { get; set; }
}
