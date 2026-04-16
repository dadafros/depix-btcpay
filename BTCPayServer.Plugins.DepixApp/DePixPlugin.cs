using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.DepixApp.PaymentHandlers;
using BTCPayServer.Plugins.DepixApp.Services;
using BTCPayServer.Common;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer;

namespace BTCPayServer.Plugins.DepixApp;
public class DePixPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(DePixPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.7" },
    ];

    internal static readonly PaymentMethodId PixPmid = new("PIX");
    private const string PixDisplayName = "Pix";
    public static readonly PaymentMethodId DePixPmid = new("DEPIX-CHAIN");
    public const string DePixCryptoCode = "DePix";

    public override void Execute(IServiceCollection services)
    {
        var plugins = (PluginServiceCollection)services;

        plugins.AddSingleton<DepixService>();
        plugins.AddSingleton<ISecretProtector, SecretProtector>();
        plugins.AddHttpClient();
        plugins.AddHttpContextAccessor();

        plugins.AddSingleton(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(PixPaymentMethodHandler)));
        plugins.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(PixCheckoutModelExtension)));

        plugins.AddTransactionLinkProvider(PixPmid, new PixTransactionLinkProvider(PixDisplayName));
        plugins.AddDefaultPrettyName(PixPmid, PixDisplayName);

        plugins.AddUIExtension("store-wallets-nav", "PixStoreNav");
        plugins.AddUIExtension("checkout-payment", "PixCheckout");
        plugins.AddUIExtension("server-nav", "PixServerNav");

        using var sp = plugins.BuildServiceProvider();
        var depixService = sp.GetRequiredService<DepixService>();
        var nbxProvider = sp.GetRequiredService<NBXplorerNetworkProvider>();
        var selectedChains = sp.GetRequiredService<SelectedChains>();
        depixService.InitDePix(plugins, nbxProvider, selectedChains);
    }
}
