using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;

namespace Ludots.Tests.GAS
{
    internal static class RepoModPaths
    {
        public static List<string> ResolveExplicit(string repoRoot, IEnumerable<string> modIds)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new ArgumentException("Repository root is required.", nameof(repoRoot));
            }

            var discovered = ModDiscovery.DiscoverMods(new[] { Path.Combine(repoRoot, "mods") });
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < discovered.Count; i++)
            {
                var mod = discovered[i];
                byName[mod.Manifest.Name] = mod.DirectoryPath;
            }

            var result = new List<string>();
            foreach (var modId in modIds)
            {
                if (!byName.TryGetValue(modId, out var modPath))
                {
                    throw new DirectoryNotFoundException($"Mod not found in repo: {modId}");
                }

                result.Add(modPath);
            }

            return result;
        }
    }
}
