using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Hosting
{
    /// <summary>
    /// Launch-time mod injection so debug/tooling mods can be enabled without
    /// editing launch graphs. Two env vars, evaluated after the authored graph
    /// has passed schema/fingerprint validation:
    ///   LUDOTS_AGENT_BRIDGE=1  → inject the AgentBridgeMod (agent debug bridge)
    ///   LUDOTS_EXTRA_MODS=a,b  → inject arbitrary mods by id (resolved as mods/&lt;id&gt;)
    /// Injected mods append after the authored plan, keeping authored ordering
    /// and validation intact. Missing mod directories fail explicitly.
    /// </summary>
    public static class LaunchModInjection
    {
        public const string AgentBridgeEnvVar = "LUDOTS_AGENT_BRIDGE";
        public const string ExtraModsEnvVar = "LUDOTS_EXTRA_MODS";
        public const string AgentBridgeModId = "AgentBridgeMod";

        public static void Apply(List<ResolvedModLoadEntry> orderedMods, string graphPath)
        {
            var requested = new List<string>();
            string? bridge = Environment.GetEnvironmentVariable(AgentBridgeEnvVar);
            if (string.Equals(bridge, "1", StringComparison.Ordinal) ||
                string.Equals(bridge, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bridge, "on", StringComparison.OrdinalIgnoreCase))
            {
                requested.Add(AgentBridgeModId);
            }

            string? extra = Environment.GetEnvironmentVariable(ExtraModsEnvVar);
            if (!string.IsNullOrWhiteSpace(extra))
            {
                foreach (string id in extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    requested.Add(id);
                }
            }

            if (requested.Count == 0) return;

            string graphDir = Path.GetDirectoryName(Path.GetFullPath(graphPath)) ?? AppContext.BaseDirectory;
            foreach (string modId in requested)
            {
                bool alreadyPlanned = false;
                foreach (ResolvedModLoadEntry entry in orderedMods)
                {
                    if (string.Equals(entry.Id, modId, StringComparison.Ordinal))
                    {
                        alreadyPlanned = true;
                        break;
                    }
                }

                if (alreadyPlanned) continue;

                string root = ResolveModRoot(graphDir, modId);
                orderedMods.Add(new ResolvedModLoadEntry(modId, root));
            }
        }

        private static string ResolveModRoot(string startDir, string modId)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "mods", modId);
                if (File.Exists(Path.Combine(candidate, "mod.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Env-requested mod '{modId}' not found: no mods/{modId}/mod.json in any ancestor of '{startDir}'.");
        }
    }
}
