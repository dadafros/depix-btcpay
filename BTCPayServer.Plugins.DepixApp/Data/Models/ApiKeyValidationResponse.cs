#nullable enable
namespace BTCPayServer.Plugins.DepixApp.Data.Models;

public sealed record ApiKeyValidationResponse(bool IsValid, string Message);
