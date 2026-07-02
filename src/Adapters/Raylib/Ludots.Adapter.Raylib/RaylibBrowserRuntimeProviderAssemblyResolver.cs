using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Ludots.Adapter.Raylib
{
    internal sealed class RaylibBrowserRuntimeProviderAssemblyResolver
    {
        private readonly AssemblyDependencyResolver _dependencyResolver;

        public RaylibBrowserRuntimeProviderAssemblyResolver(string providerAssemblyPath)
        {
            if (string.IsNullOrWhiteSpace(providerAssemblyPath))
            {
                throw new ArgumentException("Provider assembly path is required.", nameof(providerAssemblyPath));
            }

            string fullProviderAssemblyPath = Path.GetFullPath(providerAssemblyPath);
            if (!File.Exists(fullProviderAssemblyPath))
            {
                throw new FileNotFoundException("Browser runtime provider assembly was not found.", fullProviderAssemblyPath);
            }

            _dependencyResolver = new AssemblyDependencyResolver(fullProviderAssemblyPath);
        }

        public Assembly? ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            Assembly? loadedAssembly = context.Assemblies.FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
            if (loadedAssembly != null)
            {
                return loadedAssembly;
            }

            string? assemblyPath = ResolveManagedAssemblyPath(assemblyName);
            return string.IsNullOrWhiteSpace(assemblyPath)
                ? null
                : context.LoadFromAssemblyPath(assemblyPath);
        }

        public IntPtr ResolveUnmanagedDll(Assembly assembly, string unmanagedDllName)
        {
            string? dllPath = ResolveUnmanagedDllPath(unmanagedDllName);
            return string.IsNullOrWhiteSpace(dllPath)
                ? IntPtr.Zero
                : NativeLibrary.Load(dllPath);
        }

        internal string? ResolveManagedAssemblyPath(AssemblyName assemblyName)
        {
            return _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        }

        internal string? ResolveUnmanagedDllPath(string unmanagedDllName)
        {
            return _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        }
    }
}
