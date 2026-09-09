using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Navigation.NavMesh;

namespace Ludots.Tool
{
    /// <summary>
    /// Backfills per-board nav tile manifests for .ntil sets baked before manifests existed.
    /// Walks the physical nav root, reads each tile's persisted checksum and tile version, and
    /// records a stable legacy source label. A later CLI bake replaces this manifest with a
    /// source-fingerprinted one. Used by `nav write-manifest`.
    /// </summary>
    public static class NavManifestBackfill
    {
        public static int Run(string repoRoot, string? mapId, string? modId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                Console.Error.WriteLine("write-manifest requires --mapId.");
                return 2;
            }

            if (!Directory.Exists(Path.Combine(repoRoot, "mods")) && !Directory.Exists(Path.Combine(repoRoot, "assets")))
            {
                Console.Error.WriteLine($"Not a Ludots repo root: {repoRoot}");
                return 2;
            }

            var navRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(modId))
            {
                string modRoot = ToolMapConfigResolver.ResolveModRoot(repoRoot, modId);
                string candidate = Path.Combine(modRoot, "assets", "Data", "Nav", mapId);
                if (Directory.Exists(candidate)) navRoots.Add(candidate);
            }
            else
            {
                foreach (string dir in Directory.GetDirectories(Path.Combine(repoRoot, "mods"), "Nav", SearchOption.AllDirectories))
                {
                    string mapRoot = Path.Combine(dir, mapId);
                    if (Directory.Exists(mapRoot) &&
                        Directory.GetFiles(mapRoot, "*.ntil", SearchOption.AllDirectories).Length > 0)
                    {
                        navRoots.Add(mapRoot);
                    }
                }
            }

            if (navRoots.Count == 0)
            {
                Console.Error.WriteLine($"No baked .ntil set found for map '{mapId}' under '{repoRoot}'.");
                return 2;
            }

            int written = 0;
            foreach (string navRoot in navRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                written += WriteManifestForNavRoot(navRoot, mapId, repoRoot);
            }

            Console.WriteLine($"write-manifest done for map '{mapId}': {written} manifest(s).");
            return written == 0 ? 2 : 0;
        }

        private static int WriteManifestForNavRoot(string navRoot, string mapId, string repoRoot)
        {
            string[] files = Directory.GetFiles(navRoot, "*.ntil", SearchOption.AllDirectories);
            var byBoard = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string rel = Path.GetRelativePath(navRoot, file).Replace('\\', '/');
                string[] parts = rel.Split('/');
                string boardKey = string.Empty;
                if (parts.Length >= 2 && parts[0].StartsWith("board_", StringComparison.Ordinal))
                {
                    boardKey = parts[0]["board_".Length..];
                }

                if (!byBoard.TryGetValue(boardKey, out var list))
                {
                    list = new List<string>();
                    byBoard[boardKey] = list;
                }

                list.Add(file);
            }

            string algorithm = "recast";
            string mode = "offline";
            TryReadBakeMode(repoRoot, navRoot, ref algorithm, ref mode);

            int count = 0;
            foreach (KeyValuePair<string, List<string>> pair in byBoard)
            {
                string boardId = pair.Key;
                var entries = new List<NavTileManifestEntry>(pair.Value.Count);
                foreach (string file in pair.Value)
                {
                    NavTile tile;
                    try
                    {
                        using var fs = File.OpenRead(file);
                        tile = NavTileBinary.Read(fs);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Skipping unreadable tile '{file}': {ex.Message}");
                        return 2;
                    }

                    string fileName = Path.GetFileName(file);
                    if (!fileName.StartsWith("navtile_", StringComparison.Ordinal) || !fileName.EndsWith(".ntil", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unexpected tile filename '{fileName}' under '{navRoot}'.");
                        return 2;
                    }

                    string coords = fileName["navtile_".Length..].Replace(".ntil", "");
                    string[] xy = coords.Split('_');
                    if (xy.Length != 2 || !int.TryParse(xy[0], out int cx) || !int.TryParse(xy[1], out int cy))
                    {
                        Console.Error.WriteLine($"Unexpected tile filename '{fileName}' under '{navRoot}'.");
                        return 2;
                    }

                    entries.Add(new NavTileManifestEntry
                    {
                        Layer = tile.TileId.Layer,
                        ProfileId = ProfileIdFromPath(Path.GetRelativePath(navRoot, file)),
                        ChunkX = cx,
                        ChunkY = cy,
                        TileVersion = tile.TileVersion,
                        TileChecksum = "fnv1a64:" + tile.Checksum.ToString("x16")
                    });
                }

                var manifest = new NavTileManifest
                {
                    MapId = mapId,
                    BoardId = boardId,
                    SourceRevision = "legacy-backfill:" + mapId + ":" + (string.IsNullOrEmpty(boardId) ? "default" : boardId),
                    Algorithm = algorithm,
                    Mode = mode,
                    TileVersion = 0,
                    Tiles = entries.ToArray(),
                    WrittenUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                };

                string manifestPath = string.IsNullOrEmpty(boardId)
                    ? Path.Combine(navRoot, "navtiles.manifest.json")
                    : Path.Combine(navRoot, "board_" + NavAssetPaths.GetBoardPathSegment(boardId), "navtiles.manifest.json");

                NavTileManifestSerializer.Write(manifestPath, manifest);
                Console.WriteLine($"Nav manifest written: {Path.GetRelativePath(repoRoot, manifestPath)} buildHash={manifest.BuildHash} tiles={manifest.Tiles.Length}");
                count++;
            }

            return count;
        }

        private static string ProfileIdFromPath(string rel)
        {
            int marker = rel.IndexOf("profile_", StringComparison.Ordinal);
            if (marker < 0) return "unknown";
            string rest = rel[(marker + "profile_".Length)..];
            int slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest[..slash];
        }

        private static void TryReadBakeMode(string repoRoot, string navRoot, ref string algorithm, ref string mode)
        {
            string? modAssets = null;
            int idx = navRoot.IndexOf(Path.DirectorySeparatorChar + "assets" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (idx >= 0)
            {
                modAssets = navRoot[..(idx + 1)];
            }

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(modAssets))
            {
                candidates.Add(Path.Combine(modAssets, "Navigation", "navmesh.json"));
            }

            candidates.Add(Path.Combine(repoRoot, "assets", "Navigation", "navmesh.json"));
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                    if (doc.RootElement.TryGetProperty("mode", out var m)) mode = m.GetString() ?? mode;
                    if (doc.RootElement.TryGetProperty("algorithm", out var a)) algorithm = a.GetString() ?? algorithm;
                    return;
                }
                catch
                {
                    // fall through to next candidate
                }
            }
        }
    }
}
