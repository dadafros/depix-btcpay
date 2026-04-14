using System.Threading.Tasks;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Depix.Tests;

[Collection(SharedPluginTestCollection.CollectionName)]
public class PixSettingsTests : PlaywrightBaseTest
{
    public PixSettingsTests(SharedPluginTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanOpenPixSettingsWhenPluginLoaded()
    {
        await InitializeStoreOwnerAsync();
        await GoToPixSettingsAsync();

        await Page.GetByRole(AriaRole.Heading, new() { Name = "Pix Settings" }).WaitForAsync();
        await Page.Locator("#ApiKey").WaitForAsync();
        await Page.GetByText("DePix is not configured.", new() { Exact = false }).WaitForAsync();

        Assert.Contains($"/stores/{Tester.StoreId}/pix/settings", Page.Url);
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanSaveAndPersistPixStoreSettings()
    {
        await InitializeStoreOwnerAsync();
        await SeedValidStorePixConfigAsync();
        await GoToPixSettingsAsync();

        await Page.Locator("#IsEnabled").SetCheckedAsync(true);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Tester.FindAlertMessage(partialText: "Pix configuration applied");

        Assert.True(await Page.Locator("#IsEnabled").IsCheckedAsync());
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanEnablePixUsingServerLevelConfiguration()
    {
        await InitializeStoreOwnerAsync();
        await SeedValidServerPixConfigAsync();
        await GoToPixSettingsAsync();

        await Page.GetByText("server-level DePix configuration", new() { Exact = false }).WaitForAsync();

        await Page.Locator("#IsEnabled").SetCheckedAsync(true);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Tester.FindAlertMessage(partialText: "Pix configuration applied");
        Assert.True(await Page.Locator("#IsEnabled").IsCheckedAsync());

        var storeConfig = await GetStorePixConfigAsync();
        Assert.NotNull(storeConfig);
        Assert.True(storeConfig!.IsEnabled);
        Assert.Null(storeConfig.EncryptedApiKey);
        Assert.Null(storeConfig.EncryptedWebhookSecret);
    }
}
