using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Ludots.Tests.Gas.Graph.Codegen
{
    /// <summary>
    /// Spike-local Collectible ALC for generated graph assemblies (GasTests only).
    /// Intentionally mirrors host-sharing intent of <c>ModLoadContext</c> (Ludots.*/Arch from host),
    /// but is <b>not</b> a second production ALC policy SSOT — R2 must extract a shared helper
    /// rather than keep this parallel whitelist as the long-term host rule.
    /// </summary>
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

            if (IsHostSharedAssembly(assemblyName.Name))
            {
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

            return null;
        }

        private static bool IsHostSharedAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("Ludots.", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, "Arch", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, "Arch.System", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, "System.Runtime", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, "System.Private.CoreLib", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                   string.Equals(assemblyName, "netstandard", StringComparison.Ordinal);
        }
    }
}
