using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake;

public static class NavBakeHeightmapLoader
{
    public static IVisualHeightmap LoadFromRepoRoot(string repoRoot, MapConfig mapConfig, string? targetModId = null)
    {
        if (string.IsNullOrWhiteSpace(repoRoot)) throw new ArgumentException("Repo root is required.", nameof(repoRoot));
        ArgumentNullException.ThrowIfNull(mapConfig);

        string? assetPath = MapVisualHeightmapLoader.ResolveDeclaredAssetPath(mapConfig);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new InvalidOperationException(
                $"Map '{mapConfig.Id}' selects continuous-heightmap baking but declares no visual heightmap asset.");
        }

        string root = Path.GetFullPath(repoRoot);
        var candidates = new List<string>();
        AddCandidate(candidates, root, assetPath);
        string modsRoot = Path.Combine(root, "mods");
        if (Directory.Exists(modsRoot))
        {
            foreach (string manifest in Directory.EnumerateFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
            {
                string? modRoot = Path.GetDirectoryName(manifest);
                if (modRoot == null) continue;
                if (!string.IsNullOrWhiteSpace(targetModId))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (!document.RootElement.TryGetProperty("name", out JsonElement nameElement) ||
                        !string.Equals(nameElement.GetString(), targetModId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddCandidate(candidates, modRoot, assetPath);
                    continue;
                }

                AddCandidate(candidates, modRoot, assetPath);
            }
        }

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
        {
            throw new FileNotFoundException(
                $"Declared visual heightmap asset '{assetPath}' for map '{mapConfig.Id}' was not found under Core or mods.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"Declared visual heightmap asset '{assetPath}' for map '{mapConfig.Id}' resolves to multiple files: {string.Join(", ", candidates)}.");
        }

        using FileStream stream = File.OpenRead(candidates[0]);
        VisualHeightmapAsset asset = VisualHeightmapBinary.Read(stream);
        return new VisualHeightmapRuntime(asset, MapVisualHeightmapLoader.ResolveRenderProfile(mapConfig));
    }

    private static void AddCandidate(List<string> candidates, string mountRoot, string assetPath)
    {
        string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        AddIfExists(candidates, Path.Combine(mountRoot, normalized));
        if (!normalized.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            AddIfExists(candidates, Path.Combine(mountRoot, "assets", normalized));
        }
    }

    private static void AddIfExists(List<string> candidates, string path)
    {
        string full = Path.GetFullPath(path);
        if (File.Exists(full)) candidates.Add(full);
    }
}
