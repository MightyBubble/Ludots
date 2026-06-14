using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;

namespace Ludots.Core.Presentation.Terrain
{
    internal static class MapVisualHeightmapLoader
    {
        public static IVisualHeightmap? Load(IVirtualFileSystem vfs, IEnumerable<string> loadedModIds, MapConfig mapConfig)
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
            string? resolved = NormalizeDeclaredAssetPath(mapConfig.VisualHeightmapAsset);

            if (mapConfig.Boards == null)
            {
                return resolved;
            }

            for (int i = 0; i < mapConfig.Boards.Count; i++)
            {
                BoardConfig board = mapConfig.Boards[i];
                string? boardAssetPath = NormalizeDeclaredAssetPath(board?.VisualHeightmapAsset);
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

        private static Stream OpenDeclaredAsset(IVirtualFileSystem vfs, IEnumerable<string> loadedModIds, string assetPath)
        {
            string normalized = NormalizeDeclaredAssetPath(assetPath)
                ?? throw new InvalidOperationException("Visual heightmap asset path must not be empty.");
            string resolvedUri = ResolveMountedAssetUri(vfs, loadedModIds, normalized);
            return vfs.GetStream(resolvedUri);
        }

        private static string ResolveMountedAssetUri(IVirtualFileSystem vfs, IEnumerable<string> loadedModIds, string assetPath)
        {
            var matches = new List<string>(4);
            AddMatchIfExists(vfs, matches, $"Core:{assetPath}");

            if (loadedModIds != null)
            {
                foreach (string modId in loadedModIds)
                {
                    AddMatchIfExists(vfs, matches, $"{modId}:{assetPath}");
                }
            }

            if (matches.Count == 0)
            {
                throw new FileNotFoundException(
                    $"Declared visual heightmap asset '{assetPath}' could not be resolved. Use one mounted asset path relative to the owning Core/mod root, for example 'assets/terrain/example.vhtm'.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Declared visual heightmap asset '{assetPath}' resolves to multiple mounted assets ({string.Join(", ", matches)}). Visual heightmap truth must be unique.");
            }

            return matches[0];
        }

        private static void AddMatchIfExists(IVirtualFileSystem vfs, List<string> matches, string uri)
        {
            if (!vfs.TryResolveFullPath(uri, out string fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            matches.Add(uri);
        }

        private static string? NormalizeDeclaredAssetPath(string? assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string normalized = assetPath.Replace('\\', '/').Trim();
            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized.Length == 0 ? null : normalized;
        }
    }
}
