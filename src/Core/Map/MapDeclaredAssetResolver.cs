using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Modding;

namespace Ludots.Core.Map
{
    internal static class MapDeclaredAssetResolver
    {
        public static Stream OpenSingleMountedAsset(
            IVirtualFileSystem vfs,
            IEnumerable<string>? loadedModIds,
            string assetPath,
            string assetKind,
            string exampleRelativePath,
            string uniquenessLabel)
        {
            if (vfs == null) throw new ArgumentNullException(nameof(vfs));

            string normalized = NormalizeDeclaredAssetPath(assetPath)
                ?? throw new InvalidOperationException($"{assetKind} asset path must not be empty.");
            string resolvedUri = ResolveSingleMountedAssetUri(
                vfs,
                loadedModIds,
                normalized,
                assetKind,
                exampleRelativePath,
                uniquenessLabel);
            return vfs.GetStream(resolvedUri);
        }

        public static string? NormalizeDeclaredAssetPath(string? assetPath)
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

        private static string ResolveSingleMountedAssetUri(
            IVirtualFileSystem vfs,
            IEnumerable<string>? loadedModIds,
            string assetPath,
            string assetKind,
            string exampleRelativePath,
            string uniquenessLabel)
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
                    $"Declared {assetKind} asset '{assetPath}' could not be resolved. Use one mounted asset path relative to the owning Core/mod root, for example '{exampleRelativePath}'.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Declared {assetKind} asset '{assetPath}' resolves to multiple mounted assets ({string.Join(", ", matches)}). {uniquenessLabel} must be unique.");
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
    }
}
