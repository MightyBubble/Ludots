using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.Core.Modding
{
    internal sealed class ModLoadContext : AssemblyLoadContext
    {
        private readonly List<AssemblyDependencyResolver> _resolvers = new();
        private readonly HashSet<string> _registeredMainAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<AssemblyName, Assembly> _sharedAssemblyResolver;

        public ModLoadContext(Func<AssemblyName, Assembly> sharedAssemblyResolver) : base(isCollectible: true)
        {
            _sharedAssemblyResolver = sharedAssemblyResolver;
        }

        public void RegisterMainAssemblyPath(string modMainAssemblyPath)
        {
            if (string.IsNullOrWhiteSpace(modMainAssemblyPath))
            {
                throw new ArgumentException("Mod main assembly path is required.", nameof(modMainAssemblyPath));
            }

            var fullPath = System.IO.Path.GetFullPath(modMainAssemblyPath);
            if (_registeredMainAssemblyPaths.Add(fullPath))
            {
                _resolvers.Add(new AssemblyDependencyResolver(fullPath));
            }
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var alreadyLoaded = Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded != null)
            {
                return alreadyLoaded;
            }

            var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

            if (shared != null)
            {
                return shared;
            }

            var hostAlc = AssemblyLoadContext.GetLoadContext(typeof(ModLoadContext).Assembly);
            if (hostAlc != null && hostAlc != AssemblyLoadContext.Default)
            {
                var hostShared = hostAlc.Assemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
                if (hostShared != null)
                {
                    return hostShared;
                }
            }

            var sharedModAssembly = _sharedAssemblyResolver?.Invoke(assemblyName);
            if (sharedModAssembly != null)
            {
                return sharedModAssembly;
            }

            for (int i = 0; i < _resolvers.Count; i++)
            {
                var path = _resolvers[i].ResolveAssemblyToPath(assemblyName);
                if (path != null)
                {
                    return LoadFromAssemblyPath(path);
                }
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            for (int i = 0; i < _resolvers.Count; i++)
            {
                var path = _resolvers[i].ResolveUnmanagedDllToPath(unmanagedDllName);
                if (path != null)
                {
                    return LoadUnmanagedDllFromPath(path);
                }
            }

            return IntPtr.Zero;
        }
    }

}
