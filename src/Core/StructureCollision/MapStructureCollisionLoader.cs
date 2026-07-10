using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;

namespace Ludots.Core.StructureCollision
{
    internal static class MapStructureCollisionLoader
    {
        public static StructureCollisionAsset? Load(IVirtualFileSystem vfs, IEnumerable<string>? loadedModIds, MapConfig mapConfig)
        {
            if (vfs == null) throw new ArgumentNullException(nameof(vfs));
            if (mapConfig == null) throw new ArgumentNullException(nameof(mapConfig));

            string? assetPath = ResolveDeclaredAssetPath(mapConfig);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            using Stream stream = OpenDeclaredAsset(vfs, loadedModIds, assetPath);
            return StructureCollisionAssetJson.Read(stream);
        }

        public static string? ResolveDeclaredAssetPath(MapConfig mapConfig)
        {
            if (mapConfig == null) throw new ArgumentNullException(nameof(mapConfig));
            string? resolved = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(mapConfig.StructureCollisionAsset);
            bool required = mapConfig.StructureAwareGrounding || mapConfig.StructureAwareNavigation;

            if (mapConfig.Boards != null)
            {
                for (int i = 0; i < mapConfig.Boards.Count; i++)
                {
                    BoardConfig board = mapConfig.Boards[i];
                    if (board == null)
                    {
                        continue;
                    }

                    required |= board.StructureAwareGrounding || board.StructureAwareNavigation;
                    string? boardAssetPath = MapDeclaredAssetResolver.NormalizeDeclaredAssetPath(board.StructureCollisionAsset);
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
                            $"Map '{mapConfig.Id}' declares conflicting structure collision assets. Map and board contracts must resolve to a single shared asset path.");
                    }
                }
            }

            if (required && resolved == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' declares structure-aware grounding or navigation but does not declare a structureCollisionAsset.");
            }

            return resolved;
        }

        private static Stream OpenDeclaredAsset(IVirtualFileSystem vfs, IEnumerable<string>? loadedModIds, string assetPath)
        {
            return MapDeclaredAssetResolver.OpenSingleMountedAsset(
                vfs,
                loadedModIds,
                assetPath,
                "structure collision",
                "assets/structure/example.scoll.json",
                "Structure collision truth");
        }
    }
}
