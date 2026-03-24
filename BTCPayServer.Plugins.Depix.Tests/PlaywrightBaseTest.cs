using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Depix.Data.Models;
using BTCPayServer.Plugins.Depix.PaymentHandlers;
using BTCPayServer.Plugins.Depix.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests;
using DepixUtils = BTCPayServer.Plugins.Depix.Services.Utils;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Depix.Tests;

public abstract class PlaywrightBaseTest : IAsyncLifetime
{
    private static readonly PaymentMethodId PixPaymentMethodId = new("PIX");
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
        Tester = new DepixPlaywrightTester
        {
            Server = _unitTestBase.CreateServerTester(scope: CreateScopePath(), newDb: true)
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
        bool isEnabled = false,
        bool useWhitelist = false,
        bool passFeeToCustomer = false)
    {
        var storeId = Tester.StoreId ?? throw new InvalidOperationException("Create a store before seeding Pix configuration.");
        var storeRepository = Server.PayTester.GetService<StoreRepository>();
        var handlers = Server.PayTester.GetService<PaymentMethodHandlerDictionary>();
        var protector = Server.PayTester.GetService<ISecretProtector>();

        var store = await storeRepository.FindStore(storeId)
                    ?? throw new InvalidOperationException($"Store {storeId} was not found.");

        var config = new PixPaymentMethodConfig
        {
            EncryptedApiKey = protector.Protect("fixture-api-key"),
            WebhookSecretHashHex = DepixUtils.ComputeSecretHash("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            IsEnabled = isEnabled,
            UseWhitelist = useWhitelist,
            PassFeeToCustomer = passFeeToCustomer
        };

        store.SetPaymentMethodConfig(handlers[PixPaymentMethodId], config);

        var storeBlob = store.GetStoreBlob();
        storeBlob.SetExcluded(PixPaymentMethodId, !isEnabled);
        store.SetStoreBlob(storeBlob);

        await storeRepository.UpdateStore(store);
    }

    protected async Task<PixPaymentMethodConfig?> GetStorePixConfigAsync(string? storeId = null)
    {
        storeId ??= Tester.StoreId ?? throw new InvalidOperationException("Create a store before reading Pix configuration.");
        var storeRepository = Server.PayTester.GetService<StoreRepository>();
        var handlers = Server.PayTester.GetService<PaymentMethodHandlerDictionary>();

        var store = await storeRepository.FindStore(storeId)
                    ?? throw new InvalidOperationException($"Store {storeId} was not found.");

        return store.GetPaymentMethodConfig<PixPaymentMethodConfig>(PixPaymentMethodId, handlers);
    }

    protected async Task SeedValidServerPixConfigAsync(
        bool useWhitelist = false,
        bool passFeeToCustomer = false)
    {
        var settingsRepository = Server.PayTester.GetService<ISettingsRepository>();
        var protector = Server.PayTester.GetService<ISecretProtector>();

        await settingsRepository.UpdateSetting(new PixServerConfig
        {
            EncryptedApiKey = protector.Protect("fixture-server-api-key"),
            WebhookSecretHashHex = DepixUtils.ComputeSecretHash("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"),
            UseWhitelist = useWhitelist,
            PassFeeToCustomer = passFeeToCustomer
        });
    }

    private static string CreateScopePath()
    {
        return Path.Combine(Path.GetTempPath(), "depix-playwright", Guid.NewGuid().ToString("N"));
    }
}
