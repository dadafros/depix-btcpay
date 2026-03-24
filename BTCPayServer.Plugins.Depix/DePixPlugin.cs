using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Depix.PaymentHandlers;
using BTCPayServer.Plugins.Depix.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Depix;
public class DePixPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(DePixPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.6" },
    ];

    internal static readonly PaymentMethodId PixPmid = new("PIX");
    private const string PixDisplayName = "Pix";
    public static readonly PaymentMethodId DePixPmid = new("DEPIX-CHAIN");
    public const string DePixCryptoCode = "DePix";

    public override void Execute(IServiceCollection services)
    {
        var plugins = (PluginServiceCollection)services;
        RegisterApplicationParts(plugins);

        plugins.AddSingleton<DepixService>();
        plugins.AddSingleton<ISecretProtector, SecretProtector>();
        plugins.AddHttpClient();

        plugins.AddSingleton(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(PixPaymentMethodHandler)));
        plugins.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(PixCheckoutModelExtension)));

        plugins.AddTransactionLinkProvider(PixPmid, new PixTransactionLinkProvider(PixDisplayName));
        plugins.AddDefaultPrettyName(PixPmid, PixDisplayName);

        plugins.AddUIExtension("store-wallets-nav", "DePix/PixStoreNav");
        plugins.AddUIExtension("checkout-payment", "DePix/PixCheckout");
        plugins.AddUIExtension("server-nav", "DePix/PixServerNav");

        using var sp = plugins.BuildServiceProvider();
        var depixService = sp.GetRequiredService<DepixService>();
        depixService.InitDePix(plugins);
    }

    private static void RegisterApplicationParts(IServiceCollection services)
    {
        var manager = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(ApplicationPartManager))
            ?.ImplementationInstance as ApplicationPartManager;
        if (manager is null)
            return;

        var assembly = typeof(DePixPlugin).Assembly;
        var factory = ApplicationPartFactory.GetApplicationPartFactory(assembly);
        foreach (var part in factory.GetApplicationParts(assembly))
        {
            if (manager.ApplicationParts.Any(existing => existing.Name == part.Name && existing.GetType() == part.GetType()))
                continue;

            manager.ApplicationParts.Add(part);
        }
    }
}
