using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Modding;

namespace Ludots.Core.Navigation.NavMesh.Diagnostics
{
    public static class NavBakeDiagnosticsLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static NavBakeDiagnosticsDocument? TryLoad(
            IVirtualFileSystem vfs,
            IEnumerable<string>? loadedModIds,
            string mapId)
        {
            if (vfs == null)
            {
                throw new ArgumentNullException(nameof(vfs));
            }

            string relativePath = NavAssetPaths.GetBakeDiagnosticsRelativePath(mapId);
            string? uri = ResolveSingleExistingUri(vfs, loadedModIds, relativePath);
            if (uri == null)
            {
                return null;
            }

            using Stream stream = vfs.GetStream(uri);
            NavBakeDiagnosticsDocument? document = JsonSerializer.Deserialize<NavBakeDiagnosticsDocument>(stream, JsonOptions);
            if (document == null)
            {
                throw new InvalidOperationException($"Nav bake diagnostics '{uri}' is empty or invalid.");
            }

            if (!string.Equals(document.SchemaVersion, NavBakeDiagnosticsContract.SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Nav bake diagnostics '{uri}' schemaVersion must be '{NavBakeDiagnosticsContract.SchemaVersion}', actual='{document.SchemaVersion}'.");
            }

            return document;
        }

        private static string? ResolveSingleExistingUri(
            IVirtualFileSystem vfs,
            IEnumerable<string>? loadedModIds,
            string relativePath)
        {
            var matches = new List<string>(4);
            AddIfExists(vfs, matches, $"Core:{relativePath}");
            if (TryStripAssetsPrefix(relativePath, out string coreRelativePath))
            {
                AddIfExists(vfs, matches, $"Core:{coreRelativePath}");
            }

            if (loadedModIds != null)
            {
                foreach (string modId in loadedModIds)
                {
                    AddIfExists(vfs, matches, $"{modId}:{relativePath}");
                }
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Nav bake diagnostics '{relativePath}' resolves to multiple mounted assets ({string.Join(", ", matches)}). Bake diagnostics truth must be unique.");
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool TryStripAssetsPrefix(string relativePath, out string stripped)
        {
            stripped = string.Empty;
            const string prefix = "assets/";
            string normalized = relativePath.Replace('\\', '/').TrimStart('/');
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            stripped = normalized.Substring(prefix.Length);
            return stripped.Length > 0;
        }

        private static void AddIfExists(IVirtualFileSystem vfs, List<string> matches, string uri)
        {
            if (vfs.TryResolveFullPath(uri, out string fullPath) && File.Exists(fullPath))
            {
                matches.Add(uri);
            }
        }
    }
}
