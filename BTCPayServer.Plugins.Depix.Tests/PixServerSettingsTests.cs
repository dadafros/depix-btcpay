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
    public async Task CanSaveServerSettings()
    {
        await InitializeAdminAsync();
        await SeedValidServerPixConfigAsync();
        await GoToPixServerSettingsAsync();

        await Page.GetByRole(AriaRole.Heading, new() { Name = "Pix Server Settings" }).WaitForAsync();

        // Webhook URL should be visible
        await Page.Locator("#WebhookUrl").WaitForAsync();

        // Save without changes should succeed
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Tester.FindAlertMessage(partialText: "DePix server configuration applied");
    }
}
