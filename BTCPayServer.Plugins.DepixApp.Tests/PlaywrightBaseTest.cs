using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.DepixApp.Tests;

public abstract class PlaywrightBaseTest : IAsyncLifetime
{
    protected static readonly PaymentMethodId PixPaymentMethodId = new("PIX");
    private const string PluginAssemblySimpleName = "BTCPayServer.Plugins.DepixApp";
    private const string SecretProtectorTypeName = "BTCPayServer.Plugins.DepixApp.Services.ISecretProtector";
    private const string PixServerConfigTypeName = "BTCPayServer.Plugins.DepixApp.Data.Models.PixServerConfig";
    private readonly UnitTestBase _unitTestBase;

    protected PlaywrightBaseTest(SharedPluginTestFixture fixture, ITestOutputHelper output)
    {
        Fixture = fixture;
        _unitTestBase = new UnitTestBase(output);
    }

    protected SharedPluginTestFixture Fixture { get; }
    protected DepixPlaywrightTester Tester { get; private set; } = null!;
    protected IPage Page => Tester.Page;
    protected ServerTester Server => Tester.Server;

    public virtual async Task InitializeAsync()
    {
        var serverTester = _unitTestBase.CreateServerTester(scope: CreateScopePath(), newDb: true);
        serverTester.PayTester.LoadPluginsInDefaultAssemblyContext = false;
        Tester = new DepixPlaywrightTester
        {
            Server = serverTester
        };
        await Tester.StartAsync();
    }

    public virtual async Task DisposeAsync()
    {
        if (Tester is not null)
            await Tester.DisposeAsync();
    }

    protected Task InitializeAdminAsync()
    {
        return Tester.RegisterNewUser(isAdmin: true);
    }

    protected async Task InitializeStoreOwnerAsync()
    {
        await InitializeAdminAsync();
        await Tester.CreateNewStore();
    }

    protected Task GoToPixSettingsAsync(string? storeId = null)
    {
        storeId ??= Tester.StoreId ?? throw new InvalidOperationException("Create a store before navigating to Pix settings.");
        return Tester.GoToUrl($"/stores/{storeId}/pix/settings");
    }

    protected Task GoToPixServerSettingsAsync()
    {
        return Tester.GoToUrl("/server/depix/settings");
    }

    protected async Task SeedValidStorePixConfigAsync(
        bool isEnabled = false)
    {
        var storeId = Tester.StoreId ?? throw new InvalidOperationException("Create a store before seeding Pix configuration.");
        var storeRepository = Server.PayTester.GetService<StoreRepository>();
        var store = await storeRepository.FindStore(storeId)
                    ?? throw new InvalidOperationException($"Store {storeId} was not found.");

        var config = new JObject
        {
            ["encryptedApiKey"] = ProtectSecret("fixture-api-key"),
            ["encryptedWebhookSecret"] = ProtectSecret("whsec_fixture_secret_value"),
            ["isEnabled"] = isEnabled
        };

        store.SetPaymentMethodConfig(PixPaymentMethodId, config);

        var storeBlob = store.GetStoreBlob();
        storeBlob.SetExcluded(PixPaymentMethodId, !isEnabled);
        store.SetStoreBlob(storeBlob);

        await storeRepository.UpdateStore(store);
    }

    protected async Task<PixStoreConfigSnapshot?> GetStorePixConfigAsync(string? storeId = null)
    {
        storeId ??= Tester.StoreId ?? throw new InvalidOperationException("Create a store before reading Pix configuration.");
        var storeRepository = Server.PayTester.GetService<StoreRepository>();

        var store = await storeRepository.FindStore(storeId)
                    ?? throw new InvalidOperationException($"Store {storeId} was not found.");

        var configs = store.GetPaymentMethodConfigs();
        if (!configs.TryGetValue(PixPaymentMethodId, out var config))
            return null;

        return new PixStoreConfigSnapshot(
            config.Value<string>("encryptedApiKey") ?? config.Value<string>("EncryptedApiKey"),
            config.Value<string>("encryptedWebhookSecret") ?? config.Value<string>("EncryptedWebhookSecret"),
            config.Value<bool?>("isEnabled") ?? config.Value<bool?>("IsEnabled") ?? false);
    }

    protected async Task SeedValidServerPixConfigAsync()
    {
        var settingsRepository = Server.PayTester.GetService<ISettingsRepository>();
        var pluginAssembly = GetPluginRuntimeAssembly();
        var configType = pluginAssembly.GetType(PixServerConfigTypeName)
                         ?? throw new InvalidOperationException($"Could not find {PixServerConfigTypeName} in plugin runtime assembly.");
        var config = Activator.CreateInstance(configType)
                    ?? throw new InvalidOperationException($"Could not create {PixServerConfigTypeName}.");

        configType.GetProperty("EncryptedApiKey")!.SetValue(config, ProtectSecret("fixture-server-api-key"));
        configType.GetProperty("EncryptedWebhookSecret")!.SetValue(config, ProtectSecret("whsec_fixture_server_secret"));

        var updateMethod = settingsRepository.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "UpdateSetting" && method.IsGenericMethodDefinition);
        var closedUpdateMethod = updateMethod.MakeGenericMethod(configType);
        await (Task)(closedUpdateMethod.Invoke(settingsRepository, [config, null])!
                     ?? throw new InvalidOperationException("Could not invoke UpdateSetting."));
    }

    private static string CreateScopePath()
    {
        return Path.Combine(Path.GetTempPath(), "depix-playwright", Guid.NewGuid().ToString("N"));
    }

    private Assembly GetPluginRuntimeAssembly()
    {
        var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => string.Equals(assembly.GetName().Name, PluginAssemblySimpleName, StringComparison.Ordinal))
            .FirstOrDefault(assembly =>
            {
                var protectorType = assembly.GetType(SecretProtectorTypeName, throwOnError: false);
                return protectorType is not null && Server.PayTester.ServiceProvider.GetService(protectorType) is not null;
            });

        return runtimeAssembly
               ?? throw new InvalidOperationException("Could not find the runtime-loaded DePix plugin assembly.");
    }

    protected string ProtectSecret(string value)
    {
        var pluginAssembly = GetPluginRuntimeAssembly();
        var protectorType = pluginAssembly.GetType(SecretProtectorTypeName)
                            ?? throw new InvalidOperationException($"Could not find {SecretProtectorTypeName} in plugin runtime assembly.");
        var protector = Server.PayTester.ServiceProvider.GetService(protectorType)
                        ?? throw new InvalidOperationException("Could not resolve runtime-loaded ISecretProtector.");
        var protectMethod = protectorType.GetMethod("Protect", [typeof(string)])
                            ?? throw new InvalidOperationException("Could not find ISecretProtector.Protect.");

        return (string)(protectMethod.Invoke(protector, [value])
                        ?? throw new InvalidOperationException("ISecretProtector.Protect returned null."));
    }

    protected sealed record PixStoreConfigSnapshot(
        string? EncryptedApiKey,
        string? EncryptedWebhookSecret,
        bool IsEnabled);
}
