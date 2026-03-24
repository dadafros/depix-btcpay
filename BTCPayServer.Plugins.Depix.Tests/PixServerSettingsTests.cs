using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Depix.Tests;

[Collection(SharedPluginTestCollection.CollectionName)]
public class PixServerSettingsTests : PlaywrightBaseTest
{
    public PixServerSettingsTests(SharedPluginTestFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CannotSaveServerSettingsWithoutApiKey()
    {
        await InitializeAdminAsync();
        await GoToPixServerSettingsAsync();

        await Page.GetByRole(AriaRole.Heading, new() { Name = "Pix Server Settings" }).WaitForAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Tester.FindAlertMessage(StatusMessageModel.StatusSeverity.Error, "Set the DePix API key first.");
        Assert.Contains("/server/depix/settings", Page.Url);
    }

    [Fact(Timeout = TestUtils.TestTimeout)]
    [Trait("Category", "PlaywrightUITest")]
    public async Task CanRegenerateServerWebhookSecret()
    {
        await InitializeAdminAsync();
        await SeedValidServerPixConfigAsync(useWhitelist: true, passFeeToCustomer: true);
        await GoToPixServerSettingsAsync();

        Assert.True(await Page.Locator("#UseWhitelist").IsCheckedAsync());
        Assert.True(await Page.Locator("#PassFeeToCustomer").IsCheckedAsync());

        await Page.Locator("#RegenerateWebhookSecret").SetCheckedAsync(true);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Tester.FindAlertMessage(partialText: "DePix server configuration applied");

        var oneShotSecret = await Page.Locator("#OneShotSecret").InputValueAsync();
        Assert.Matches("^[0-9a-f]{64}$", oneShotSecret);

        await Page.ReloadAsync();

        Assert.Equal(0, await Page.Locator("#OneShotSecret").CountAsync());
        Assert.Contains("stored securely", await Page.Locator("#WebhookSecretDisplay").InputValueAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(await Page.Locator("#UseWhitelist").IsCheckedAsync());
        Assert.True(await Page.Locator("#PassFeeToCustomer").IsCheckedAsync());
    }
}
