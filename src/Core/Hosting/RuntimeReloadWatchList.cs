using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Modding;

namespace Ludots.Core.Hosting
{
    public static class RuntimeReloadWatchList
    {
        public static IReadOnlyList<string> Create(string baseDirectory, string? gameConfigFile, GameEngine engine)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Base directory is required.", nameof(baseDirectory));
            }

            var paths = new List<string>
            {
                ResolveBootstrapPath(baseDirectory, gameConfigFile)
            };

            if (engine?.ModLoader == null)
            {
                return paths;
            }

            foreach (var pair in engine.ModLoader.GetLoadedModDirectories().OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var modDirectory = pair.Value;
                if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
                {
                    continue;
                }

                var manifestPath = Path.Combine(modDirectory, "mod.json");
                paths.Add(manifestPath);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                    if (!string.IsNullOrWhiteSpace(manifest.Main))
                    {
                        paths.Add(Path.GetFullPath(Path.Combine(modDirectory, manifest.Main)));
                    }
                }
                catch
                {
                    // Keep watching mod.json even if it is temporarily invalid during edits.
                }
            }

            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string ResolveBootstrapPath(string baseDirectory, string? gameConfigFile)
        {
            string bootstrapFile = string.IsNullOrWhiteSpace(gameConfigFile)
                ? "launcher.runtime.json"
                : gameConfigFile;

            return Path.IsPathRooted(bootstrapFile)
                ? Path.GetFullPath(bootstrapFile)
                : Path.GetFullPath(Path.Combine(baseDirectory, bootstrapFile));
        }
    }
}
