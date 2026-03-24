using System;
using System.IO;
using Xunit;

namespace BTCPayServer.Plugins.Depix.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class SharedPluginTestCollection : ICollectionFixture<SharedPluginTestFixture>
{
    public const string CollectionName = "DePix Playwright UI Tests";
}

public sealed class SharedPluginTestFixture : IDisposable
{
    private const string DebugPluginsEnvironmentVariable = "DEBUG_PLUGINS";
    private const string PluginDirEnvironmentVariable = "plugindir";
    private const string PrefixedPluginDirEnvironmentVariable = "BTCPAY_PLUGINDIR";
    private const string PluginProjectFile = "BTCPayServer.Plugins.DePix/BTCPayServer.Plugins.Depix.csproj";
    private const string PluginAssemblyName = "BTCPayServer.Plugins.Depix.dll";
    private readonly string? _originalDebugPlugins;
    private readonly string? _originalPluginDir;
    private readonly string? _originalPrefixedPluginDir;

    public SharedPluginTestFixture()
    {
        RepositoryRoot = FindRepositoryRoot();
        PluginDllPath = ResolvePluginDllPath(RepositoryRoot);
        IsolatedPluginDirectory = CreateIsolatedPluginDirectory();

        _originalDebugPlugins = Environment.GetEnvironmentVariable(DebugPluginsEnvironmentVariable);
        _originalPluginDir = Environment.GetEnvironmentVariable(PluginDirEnvironmentVariable);
        _originalPrefixedPluginDir = Environment.GetEnvironmentVariable(PrefixedPluginDirEnvironmentVariable);

        Environment.SetEnvironmentVariable(DebugPluginsEnvironmentVariable, PluginDllPath);
        Environment.SetEnvironmentVariable(PluginDirEnvironmentVariable, IsolatedPluginDirectory);
        Environment.SetEnvironmentVariable(PrefixedPluginDirEnvironmentVariable, IsolatedPluginDirectory);
    }

    public string RepositoryRoot { get; }
    public string PluginDllPath { get; }
    public string IsolatedPluginDirectory { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DebugPluginsEnvironmentVariable, _originalDebugPlugins);
        Environment.SetEnvironmentVariable(PluginDirEnvironmentVariable, _originalPluginDir);
        Environment.SetEnvironmentVariable(PrefixedPluginDirEnvironmentVariable, _originalPrefixedPluginDir);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var pluginProjectPath = Path.Combine(directory.FullName, PluginProjectFile);
            if (File.Exists(pluginProjectPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root containing {PluginProjectFile}.");
    }

    private static string ResolvePluginDllPath(string repositoryRoot)
    {
        var pluginDllPath = Path.Combine(
            repositoryRoot,
            "BTCPayServer.Plugins.DePix",
            "bin",
            GetBuildConfiguration(),
            "net8.0",
            PluginAssemblyName);

        if (!File.Exists(pluginDllPath))
            throw new FileNotFoundException($"Could not find built plugin assembly at {pluginDllPath}.", pluginDllPath);

        return pluginDllPath;
    }

    private static string CreateIsolatedPluginDirectory()
    {
        var pluginDirectory = Path.Combine(Path.GetTempPath(), "depix-plugin-dir", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pluginDirectory);
        return pluginDirectory;
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
