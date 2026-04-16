namespace BTCPayServer.Plugins.DepixApp.Data.Models;

public sealed record PixConfigStatus(
    bool DePixActive,
    bool PixEnabled,
    bool ApiKeyConfigured)
{
    public bool PixUsable => DePixActive && PixEnabled && ApiKeyConfigured;
}
