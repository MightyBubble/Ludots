using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.UI.Browser;

namespace Ludots.Adapter.Raylib
{
    internal static class RaylibBrowserRuntimeInstaller
    {
        private const string CefProviderId = "cef";
        private const string CefProviderHostTypeName = "Ludots.UI.Browser.Cef.CefBrowserRuntimeHost";
        private const string CefProviderAssemblyFileName = "Ludots.UI.Browser.Cef.dll";

        public static IBrowserRuntime? InstallIfConfigured(GameEngine engine, GameConfig config, string baseDirectory)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(config);

            BrowserRuntimeConfig runtimeConfig = config.BrowserRuntime ?? new BrowserRuntimeConfig();
            if (!runtimeConfig.Enabled)
            {
                if (runtimeConfig.Required)
                {
                    throw new InvalidOperationException("Browser runtime is marked required but browserRuntime.enabled is false.");
                }

                return null;
            }

            if (!string.Equals(runtimeConfig.Provider, CefProviderId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unsupported browser runtime provider '{runtimeConfig.Provider}'. Raylib host currently supports '{CefProviderId}'.");
            }

            string providerAssemblyPath = ResolveProviderAssemblyPath(baseDirectory, runtimeConfig);
            string runtimeRootPath = ResolveRequiredPath(
                baseDirectory,
                runtimeConfig.RuntimeRootPath,
                "browserRuntime.runtimeRootPath is required for CefSharp.");
            string? cacheRootPath = ResolveOptionalPath(baseDirectory, runtimeConfig.CacheRootPath);
            return InstallCef(engine.GlobalContext, providerAssemblyPath, runtimeRootPath, cacheRootPath);
        }

        private static string ResolveProviderAssemblyPath(string baseDirectory, BrowserRuntimeConfig runtimeConfig)
        {
            string resolvedPath = string.IsNullOrWhiteSpace(runtimeConfig.ProviderAssemblyPath)
                ? Path.GetFullPath(Path.Combine(baseDirectory, CefProviderAssemblyFileName))
                : ResolvePath(baseDirectory, runtimeConfig.ProviderAssemblyPath);

            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    $"CEF provider assembly was not found. browserRuntime.providerAssemblyPath must point to '{CefProviderAssemblyFileName}'.",
                    resolvedPath);
            }

            return resolvedPath;
        }

        private static string? ResolveOptionalPath(string baseDirectory, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            return ResolvePath(baseDirectory, configuredPath);
        }

        private static string ResolveRequiredPath(string baseDirectory, string configuredPath, string message)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException(message);
            }

            return ResolvePath(baseDirectory, configuredPath);
        }

        private static string ResolvePath(string baseDirectory, string configuredPath)
        {
            string expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }

        private static IBrowserRuntime InstallCef(
            IDictionary<string, object> services,
            string providerAssemblyPath,
            string? runtimeRootPath,
            string? cacheRootPath)
        {
            BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
                new BrowserRuntimeProviderLoadOptions(
                    services,
                    providerAssemblyPath,
                    CefProviderHostTypeName)
                {
                    ProviderId = CefProviderId,
                    RuntimeRootPath = runtimeRootPath,
                    BrowserCacheRootPath = cacheRootPath,
                    Log = message => Log.Info(in LogChannels.Engine, message)
                });
            return handle.Runtime;
        }
    }
}
