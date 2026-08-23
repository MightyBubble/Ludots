using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Terrain
{
    public static class MapVisualHeightmapLoader
    {
        public static IVisualHeightmap? Load(IVirtualFileSystem vfs, IEnumerable<string>? loadedModIds, MapConfig mapConfig)
        {
            if (vfs == null)
            {
                throw new ArgumentNullException(nameof(vfs));
            }

            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            string? assetPath = ResolveDeclaredAssetPath(mapConfig);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            using Stream stream = OpenDeclaredAsset(vfs, loadedModIds, assetPath);
            VisualHeightmapAsset asset = VisualHeightmapBinary.Read(stream);
            return new VisualHeightmapRuntime(asset, ResolveRenderProfile(mapConfig));
        }

        public static string? ResolveDeclaredAssetPath(MapConfig mapConfig)
        {
            string? resolved = ResolveMapLevelDeclaredAssetPath(mapConfig);

            if (mapConfig.Boards == null)
            {
                return resolved;
            }

            for (int i = 0; i < mapConfig.Boards.Count; i++)
            {
                BoardConfig board = mapConfig.Boards[i];
                string? boardAssetPath = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(board?.VisualHeightmapAsset);
                if (boardAssetPath == null)
                {
                    continue;
                }

                if (resolved == null)
                {
                    resolved = boardAssetPath;
                    continue;
                }

                if (!string.Equals(resolved, boardAssetPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapConfig.Id}' declares conflicting visual heightmap assets. Map and board contracts must resolve to a single shared asset path.");
                }
            }

            return resolved;
        }

        public static VisualHeightmapRenderProfile ResolveRenderProfile(MapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                throw new ArgumentNullException(nameof(mapConfig));
            }

            return (mapConfig.VisualHeightmap?.RenderProfile ?? VisualHeightmapRenderProfile.CreateDefault())
                .NormalizeAndValidate();
        }

        private static string? ResolveMapLevelDeclaredAssetPath(MapConfig mapConfig)
        {
            string? legacy = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(mapConfig.VisualHeightmapAsset);
            string? binding = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(mapConfig.VisualHeightmap?.Asset);
            if (legacy != null &&
                binding != null &&
                !string.Equals(legacy, binding, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' declares conflicting visual heightmap assets. visualHeightmapAsset and visualHeightmap.asset must resolve to the same asset path.");
            }

            return binding ?? legacy;
        }

        private static Stream OpenDeclaredAsset(IVirtualFileSystem vfs, IEnumerable<string>? loadedModIds, string assetPath)
        {
            return MapDeclaredAssetResolver.OpenSingleMountedAsset(
                vfs,
                loadedModIds,
                assetPath,
                "visual heightmap",
                "assets/terrain/example.vhtm",
                "Visual heightmap truth");
        }
    }
}
