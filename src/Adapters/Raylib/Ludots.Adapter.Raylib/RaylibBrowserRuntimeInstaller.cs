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

            if (string.IsNullOrWhiteSpace(runtimeConfig.Provider))
            {
                throw new InvalidOperationException(
                    "Browser runtime is enabled but browserRuntime.provider is empty.");
            }

            string providerAssemblyPath = ResolveProviderAssemblyPath(runtimeConfig);
            string providerHostTypeName = RequireProviderHostTypeName(runtimeConfig);
            string? runtimeRootPath = ResolveOptionalPath(baseDirectory, runtimeConfig.RuntimeRootPath);
            string? cacheRootPath = ResolveOptionalPath(baseDirectory, runtimeConfig.CacheRootPath);
            return InstallProvider(
                engine.GlobalContext,
                runtimeConfig.Provider,
                providerAssemblyPath,
                providerHostTypeName,
                runtimeConfig.UseCollectibleLoadContext ?? true,
                runtimeConfig.ProcessSharedAssemblyNamePrefixes,
                runtimeRootPath,
                cacheRootPath);
        }

        private static string ResolveProviderAssemblyPath(BrowserRuntimeConfig runtimeConfig)
        {
            if (string.IsNullOrWhiteSpace(runtimeConfig.ProviderAssemblyPath))
            {
                throw new InvalidOperationException(
                    "Browser runtime is enabled but browserRuntime.providerAssemblyPath is empty.");
            }

            string expanded = Environment.ExpandEnvironmentVariables(runtimeConfig.ProviderAssemblyPath);
            if (!Path.IsPathRooted(expanded))
            {
                throw new InvalidOperationException(
                    "Browser runtime provider assembly path must be a launcher-resolved absolute path.");
            }

            string resolvedPath = Path.GetFullPath(expanded);

            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    $"Browser runtime provider assembly was not found for provider '{runtimeConfig.Provider}'.",
                    resolvedPath);
            }

            return resolvedPath;
        }

        private static string RequireProviderHostTypeName(BrowserRuntimeConfig runtimeConfig)
        {
            if (string.IsNullOrWhiteSpace(runtimeConfig.ProviderHostTypeName))
            {
                throw new InvalidOperationException(
                    "Browser runtime is enabled but browserRuntime.providerHostTypeName is empty.");
            }

            return runtimeConfig.ProviderHostTypeName.Trim();
        }

        private static string? ResolveOptionalPath(string baseDirectory, string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
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

        private static IBrowserRuntime InstallProvider(
            IDictionary<string, object> services,
            string providerId,
            string providerAssemblyPath,
            string providerHostTypeName,
            bool useCollectibleLoadContext,
            string[] processSharedAssemblyNamePrefixes,
            string? runtimeRootPath,
            string? cacheRootPath)
        {
            BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
                new BrowserRuntimeProviderLoadOptions(
                    services,
                    providerAssemblyPath,
                    providerHostTypeName)
                {
                    ProviderId = providerId,
                    UseCollectibleLoadContext = useCollectibleLoadContext,
                    ProcessSharedAssemblyNamePrefixes = processSharedAssemblyNamePrefixes ?? Array.Empty<string>(),
                    RuntimeRootPath = runtimeRootPath,
                    BrowserCacheRootPath = cacheRootPath,
                    Log = message => Log.Info(in LogChannels.Engine, message)
                });
            return handle.Runtime;
        }
    }
}
