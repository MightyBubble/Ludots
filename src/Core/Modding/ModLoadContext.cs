using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.Core.Modding
{
    internal sealed class ModLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly Func<AssemblyName, Assembly> _sharedAssemblyResolver;

        public string ModMainAssemblyPath { get; }

        public ModLoadContext(string modMainAssemblyPath, Func<AssemblyName, Assembly> sharedAssemblyResolver) : base(isCollectible: true)
        {
            ModMainAssemblyPath = modMainAssemblyPath ?? throw new ArgumentNullException(nameof(modMainAssemblyPath));
            _resolver = new AssemblyDependencyResolver(ModMainAssemblyPath);
            _sharedAssemblyResolver = sharedAssemblyResolver;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

            if (shared != null)
            {
                return shared;
            }

            var sharedModAssembly = _sharedAssemblyResolver?.Invoke(assemblyName);
            if (sharedModAssembly != null)
            {
                return sharedModAssembly;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path != null)
            {
                return LoadFromAssemblyPath(path);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path != null)
            {
                return LoadUnmanagedDllFromPath(path);
            }

            return IntPtr.Zero;
        }
    }

}
