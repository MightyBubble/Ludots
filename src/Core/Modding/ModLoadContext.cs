using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.Core.Modding
{
    internal sealed class ModLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> ProcessSharedAssemblyNames = new(StringComparer.Ordinal)
        {
            "Ludots.UI.Browser"
        };

        private readonly List<AssemblyDependencyResolver> _resolvers = new();
        private readonly Dictionary<string, Assembly> _managedAssembliesByPath = new(StringComparer.Ordinal);
        private readonly HashSet<string> _registeredMainAssemblyPaths = new(StringComparer.Ordinal);
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

        public Assembly LoadMainAssembly(string modMainAssemblyPath)
        {
            return LoadManagedAssemblyFromPath(modMainAssemblyPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (TryLoadProcessSharedAssembly(assemblyName, out var processShared))
            {
                return processShared;
            }

            var alreadyLoaded = Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
            if (alreadyLoaded != null)
            {
                return alreadyLoaded;
            }

            var shared = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.Ordinal));

            if (shared != null)
            {
                return shared;
            }

            var hostAlc = AssemblyLoadContext.GetLoadContext(typeof(ModLoadContext).Assembly);
            if (hostAlc != null && hostAlc != AssemblyLoadContext.Default)
            {
                var hostShared = hostAlc.Assemblies.FirstOrDefault(a =>
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
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
                    return LoadManagedAssemblyFromPath(path);
                }
            }

            return null;
        }

        private bool TryLoadProcessSharedAssembly(AssemblyName assemblyName, out Assembly assembly)
        {
            assembly = null;
            if (string.IsNullOrWhiteSpace(assemblyName.Name) ||
                !ProcessSharedAssemblyNames.Contains(assemblyName.Name))
            {
                return false;
            }

            assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(assemblyName, candidate.GetName()));
            if (assembly != null)
            {
                return true;
            }

            var hostAlc = AssemblyLoadContext.GetLoadContext(typeof(ModLoadContext).Assembly);
            if (hostAlc != null && hostAlc != AssemblyLoadContext.Default)
            {
                assembly = hostAlc.Assemblies.FirstOrDefault(candidate =>
                    AssemblyName.ReferenceMatchesDefinition(assemblyName, candidate.GetName()));
                if (assembly != null)
                {
                    return true;
                }
            }

            var sharedModAssembly = _sharedAssemblyResolver?.Invoke(assemblyName);
            if (sharedModAssembly != null)
            {
                assembly = sharedModAssembly;
                return true;
            }

            for (int i = 0; i < _resolvers.Count; i++)
            {
                var path = _resolvers[i].ResolveAssemblyToPath(assemblyName);
                if (path != null)
                {
                    assembly = LoadProcessAssemblyFromPath(path);
                    return true;
                }
            }

            return false;
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

        private Assembly LoadManagedAssemblyFromPath(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("Assembly path is required.", nameof(assemblyPath));
            }

            var fullPath = Path.GetFullPath(assemblyPath);
            if (_managedAssembliesByPath.TryGetValue(fullPath, out var loadedAssembly))
            {
                return loadedAssembly;
            }

            loadedAssembly = RequiresProcessAssemblyLoad(fullPath)
                ? LoadProcessAssemblyFromPath(fullPath)
                : LoadManagedAssemblyFromStream(fullPath);

            _managedAssembliesByPath[fullPath] = loadedAssembly;
            return loadedAssembly;
        }

        private Assembly LoadManagedAssemblyFromStream(string fullPath)
        {
            using var assemblyStream = OpenReadStream(fullPath);
            using var pdbStream = TryOpenSymbolStream(fullPath);
            return pdbStream != null
                ? LoadFromStream(assemblyStream, pdbStream)
                : LoadFromStream(assemblyStream);
        }

        private static Assembly LoadProcessAssemblyFromPath(string fullPath)
        {
            AssemblyName assemblyName = AssemblyName.GetAssemblyName(fullPath);
            var loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()));
            return loadedAssembly ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }

        private static bool RequiresProcessAssemblyLoad(string fullPath)
        {
            string assemblyName = Path.GetFileNameWithoutExtension(fullPath);
            return assemblyName.StartsWith("CefSharp", StringComparison.OrdinalIgnoreCase);
        }

        private static FileStream OpenReadStream(string fullPath)
        {
            return new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }

        private static Stream? TryOpenSymbolStream(string assemblyPath)
        {
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (!File.Exists(pdbPath))
            {
                return null;
            }

            return OpenReadStream(pdbPath);
        }
    }

}
