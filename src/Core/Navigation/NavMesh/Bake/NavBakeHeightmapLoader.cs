using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.NavMesh.Bake;

public static class NavBakeHeightmapLoader
{
    public static IVisualHeightmap LoadFromRepoRoot(
        string repoRoot,
        MapConfig mapConfig,
        BoardConfig boardConfig,
        IReadOnlyList<string> orderedMountRoots)
    {
        if (string.IsNullOrWhiteSpace(repoRoot)) throw new ArgumentException("Repo root is required.", nameof(repoRoot));
        ArgumentNullException.ThrowIfNull(mapConfig);
        ArgumentNullException.ThrowIfNull(boardConfig);
        ArgumentNullException.ThrowIfNull(orderedMountRoots);

        string? assetPath = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(boardConfig.VisualHeightmapAsset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new InvalidOperationException(
                $"Board '{boardConfig.Name}' on map '{mapConfig.Id}' selects continuous-heightmap baking but declares no visual heightmap asset.");
        }

        var candidates = new List<string>();
        for (int i = 0; i < orderedMountRoots.Count; i++)
        {
            string mountRoot = Path.GetFullPath(orderedMountRoots[i]);
            AddCandidate(candidates, mountRoot, assetPath);
        }

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
        {
            throw new FileNotFoundException(
                $"Declared visual heightmap asset '{assetPath}' for map '{mapConfig.Id}' was not found under Core or mods.");
        }

        string selected = candidates[^1];
        using FileStream stream = File.OpenRead(selected);
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
