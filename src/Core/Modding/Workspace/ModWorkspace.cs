using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Modding.Workspace
{
    /// <summary>
    /// A workspace lists directories that contain mods.
    /// Analogous to a VS Code .code-workspace — you can point to any directories,
    /// and all sub-folders with a mod.json are discovered as mods.
    /// </summary>
    public sealed class ModWorkspace
    {
        [JsonPropertyName("sources")]
        public List<string> Sources { get; set; } = new();

        public static ModWorkspace Load(string workspaceFilePath)
        {
            if (string.IsNullOrWhiteSpace(workspaceFilePath))
                throw new ArgumentException("Workspace file path is required.", nameof(workspaceFilePath));

            var fullPath = Path.GetFullPath(workspaceFilePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Workspace file not found: {fullPath}");

            var json = File.ReadAllText(fullPath);
            var workspace = JsonSerializer.Deserialize<ModWorkspace>(json, SerializerOptions);
            if (workspace == null)
                throw new InvalidOperationException($"Failed to deserialize workspace: {fullPath}");

            var baseDir = Path.GetDirectoryName(fullPath)!;
            workspace.ResolvePaths(baseDir);
            return workspace;
        }

        public static ModWorkspace CreateDefault(string baseDirectory)
        {
            var ws = new ModWorkspace();
            ws.Sources.Add(Path.GetFullPath(Path.Combine(baseDirectory, "mods")));
            return ws;
        }

        /// <summary>
        /// Discover all mod directories from all workspace sources.
        /// Each source directory is scanned for sub-folders containing mod.json.
        /// Returns absolute paths to each mod folder.
        /// </summary>
        public List<string> DiscoverModDirectories()
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Sources.Count; i++)
            {
                var source = Sources[i];
                if (!Directory.Exists(source)) continue;

                foreach (var dir in Directory.GetDirectories(source))
                {
                    var modJsonPath = Path.Combine(dir, "mod.json");
                    if (!File.Exists(modJsonPath)) continue;

                    var fullDir = Path.GetFullPath(dir);
                    if (seen.Add(fullDir))
                        results.Add(fullDir);
                }
            }

            return results;
        }

        /// <summary>
        /// Discover mod directories and filter to only include the named mods.
        /// Includes transitive dependencies resolved from mod.json manifests.
        /// </summary>
        public List<string> ResolveModPaths(IEnumerable<string> modNames)
        {
            var allDirs = DiscoverModDirectories();
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in allDirs)
            {
                var manifestPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifestPath)) continue;
                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                byName[manifest.Name] = dir;
            }

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Collect(string name)
            {
                if (!required.Add(name)) return;
                if (!byName.TryGetValue(name, out var dir))
                    throw new InvalidOperationException($"Mod not found in workspace: {name}");

                var manifestPath = Path.Combine(dir, "mod.json");
                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                foreach (var dep in manifest.Dependencies.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(dep)) Collect(dep);
                }
                order.Add(name);
            }

            foreach (var name in modNames) Collect(name);

            var result = new List<string>(order.Count);
            for (int i = 0; i < order.Count; i++)
                result.Add(byName[order[i]]);
            return result;
        }

        private void ResolvePaths(string baseDir)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                var raw = Sources[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                Sources[i] = Path.IsPathRooted(raw)
                    ? Path.GetFullPath(raw)
                    : Path.GetFullPath(Path.Combine(baseDir, raw));
            }
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
    }
}
