using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.Graph.Codegen
{
    public sealed class GraphGeneratedAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyLoadContext _hostContext;

        public GraphGeneratedAssemblyLoadContext(string name)
            : base(name, isCollectible: true)
        {
            _hostContext = GetLoadContext(typeof(GraphGeneratedAssemblyLoadContext).Assembly)
                ?? Default;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
            {
                return null;
            }

            if (!IsHostSharedAssembly(assemblyName.Name))
            {
                return null;
            }

            Assembly? fromHost = _hostContext.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
            if (fromHost != null)
            {
                return fromHost;
            }

            fromHost = Default.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
            if (fromHost != null)
            {
                return fromHost;
            }

            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
                return null;
            }
        }

        private static bool IsHostSharedAssembly(string assemblyName) =>
            assemblyName.StartsWith("Ludots.", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "Arch", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "Arch.System", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "System.Runtime", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "System.Private.CoreLib", StringComparison.Ordinal) ||
            assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
            assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            string.Equals(assemblyName, "netstandard", StringComparison.Ordinal);
    }
}
