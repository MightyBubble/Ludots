using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Ludots.Core.Config;
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
            string? runtimeRootPath = ResolveOptionalPath(baseDirectory, runtimeConfig.RuntimeRootPath);
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
            Assembly assembly = LoadProviderAssembly(providerAssemblyPath);
            Type hostType = assembly.GetType(CefProviderHostTypeName, throwOnError: true)
                ?? throw new InvalidOperationException($"CEF provider host type '{CefProviderHostTypeName}' was not found.");
            MethodInfo installMethod = ResolveInstallMethod(hostType, runtimeRootPath != null);
            object?[] arguments = runtimeRootPath != null
                ? new object?[] { services, runtimeRootPath, cacheRootPath }
                : new object?[] { services, cacheRootPath };

            try
            {
                object? installed = installMethod.Invoke(null, arguments);
                return installed as IBrowserRuntime
                    ?? throw new InvalidOperationException("CEF provider Install did not return an IBrowserRuntime.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static MethodInfo ResolveInstallMethod(Type hostType, bool hasRuntimeRootPath)
        {
            string methodName = hasRuntimeRootPath ? "Install" : "InstallFromAssemblyLocation";
            Type[] parameterTypes = hasRuntimeRootPath
                ? new[] { typeof(IDictionary<string, object>), typeof(string), typeof(string) }
                : new[] { typeof(IDictionary<string, object>), typeof(string) };
            return hostType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: parameterTypes,
                modifiers: null)
                ?? throw new InvalidOperationException(
                    $"CEF provider host type '{CefProviderHostTypeName}' does not expose the expected {methodName} method.");
        }

        private static Assembly LoadProviderAssembly(string assemblyPath)
        {
            AssemblyName requested = AssemblyName.GetAssemblyName(assemblyPath);
            Assembly? loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(requested, candidate.GetName()));
            return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }
    }
}
