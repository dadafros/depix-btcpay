using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;

namespace BTCPayServer.Plugins.DepixAppApp.Tests;

public class DepixPlaywrightTester : PlaywrightTester
{
    private static readonly FieldInfo BrowserBackingField =
        typeof(PlaywrightTester).GetField("<Browser>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not access PlaywrightTester.Browser backing field.");

    public new async Task StartAsync()
    {
        Server.PayTester.NoCSP = true;
        TaskCanceledException? syncTimeout = null;

        try
        {
            await Server.StartAsync();
        }
        catch (TaskCanceledException ex)
        {
            syncTimeout = ex;
        }

        if (syncTimeout is not null)
        {
            if (!await ServerRespondsAsync())
                throw syncTimeout;

            TestLogs.LogInformation("BTCPay host is reachable; continuing after sync wait timeout caused by DePix chain registration.");
        }

        var builder = new ConfigurationBuilder();
        builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
        builder.AddEnvironmentVariables();
        var conf = builder.Build();
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Server.PayTester.InContainer || conf["PLAYWRIGHT_HEADLESS"] == "true",
            ExecutablePath = conf["PLAYWRIGHT_EXECUTABLE"],
            SlowMo = 0,
            Args = ["--disable-frame-rate-limit"]
        });
        BrowserBackingField.SetValue(this, browser);

        var context = await browser.NewContextAsync();
        Page = await context.NewPageAsync();
        ServerUri = Server.PayTester.ServerUri;

        TestLogs.LogInformation($"Playwright: Using {Page.GetType()}");
        TestLogs.LogInformation($"Playwright: Browsing to {ServerUri}");

        await GoToRegister();
        await Page.AssertNoError();
    }

    private async Task<bool> ServerRespondsAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var response = await Server.PayTester.HttpClient.GetAsync("/", cts.Token);
                return (int)response.StatusCode < 500;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250, cts.Token);
        }

        return false;
    }
}
