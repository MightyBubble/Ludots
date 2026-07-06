using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;

namespace Ludots.Core.Presentation.Terrain
{
    internal static class MapVisualHeightmapLoader
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
            return new VisualHeightmapRuntime(asset);
        }

        public static string? ResolveDeclaredAssetPath(MapConfig mapConfig)
        {
            string? resolved = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(mapConfig.VisualHeightmapAsset);

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
