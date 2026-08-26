using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    public readonly record struct FieldCellRegionEntry(int X, int Y, string RegionKey);

    public readonly record struct FieldCellRectEntry(int X0, int Y0, int X1, int Y1, string RegionKey);

    public sealed class FieldCellsAsset
    {
        /// <summary>Layer key this asset was loaded for; always matches the requested layer.</summary>
        public required string LayerKey { get; init; }

        /// <summary>Region keys in Ordinal order; regionId = index + 1, id 0 reserved for "no region".</summary>
        public required string[] RegionKeys { get; init; }

        /// <summary>Inclusive rect strokes. Preferred authoring form for large provinces.</summary>
        public required FieldCellRectEntry[] Rects { get; init; }

        /// <summary>Sparse point strokes (schema v1 cells land here; v2 optional leftovers).</summary>
        public required FieldCellRegionEntry[] Points { get; init; }
    }

    /// <summary>
    /// Loads and merges the authored cell data of one layer from
    /// <c>Fields/cells/&lt;layerKey&gt;.json</c> across Core + mod fragments.
    /// Region ids are derived from the Ordinal-sorted union of all region keys,
    /// so fragment load order never changes an id. Two fragments assigning the
    /// same cell to different regions fail the load with both keys named.
    /// schemaVersion 1 uses per-cell <c>cells</c>; schemaVersion 2 uses <c>rects</c>
    /// (+ optional <c>points</c>) and rejects <c>cells</c>.
    /// </summary>
    public sealed class FieldCellsConfigLoader
    {
        public const string CellsDirectory = "Fields/cells";
        public const int SchemaVersionCells = 1;
        public const int SchemaVersionRects = 2;

        private readonly ConfigPipeline _pipeline;

        public FieldCellsConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public FieldCellsAsset? Load(string layerKey)
        {
            if (string.IsNullOrWhiteSpace(layerKey))
            {
                throw new ArgumentException("Layer key is required.", nameof(layerKey));
            }

            string path = $"{CellsDirectory}/{layerKey}.json";
            List<ConfigFragment> fragments = _pipeline.CollectFragmentsWithSources(path);
            if (fragments.Count == 0)
            {
                return null;
            }

            var parsed = new List<FragmentStrokes>(fragments.Count);
            var allKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigFragment fragment in fragments)
            {
                FragmentStrokes entry = ParseFragment(fragment, layerKey, path);
                parsed.Add(entry);
                allKeys.UnionWith(entry.SortedKeys);
            }

            var sortedUnion = new string[allKeys.Count];
            allKeys.CopyTo(sortedUnion);
            Array.Sort(sortedUnion, StringComparer.Ordinal);

            var rects = new List<FieldCellRectEntry>();
            var points = new List<FieldCellRegionEntry>();
            var claimedRects = new List<FieldCellRectEntry>();
            var ownerByPoint = new Dictionary<long, string>();
            foreach (FragmentStrokes fragment in parsed)
            {
                foreach (FieldCellRectEntry rect in fragment.Rects)
                {
                    ClaimRect(claimedRects, ownerByPoint, rect, path);
                    claimedRects.Add(rect);
                    rects.Add(rect);
                }

                foreach (FieldCellRegionEntry point in fragment.Points)
                {
                    if (!ClaimPoint(claimedRects, ownerByPoint, point, path))
                    {
                        continue;
                    }

                    points.Add(point);
                }
            }

            return new FieldCellsAsset
            {
                LayerKey = layerKey,
                RegionKeys = sortedUnion,
                Rects = rects.ToArray(),
                Points = points.ToArray(),
            };
        }

        private static FragmentStrokes ParseFragment(
            ConfigFragment fragment, string layerKey, string path)
        {
            FieldCellsConfig cfg;
            try
            {
                cfg = fragment.Node.Deserialize<FieldCellsConfig>(StrictJsonOptions.CreateCamelCase())
                    ?? throw new InvalidOperationException($"Field cells asset '{path}' from {fragment.SourceUri} is null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Field cells asset '{path}' from {fragment.SourceUri}: {ex.Message}", ex);
            }

            if (cfg.SchemaVersion != SchemaVersionCells && cfg.SchemaVersion != SchemaVersionRects)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {fragment.SourceUri}: schemaVersion {cfg.SchemaVersion} is not supported; expected {SchemaVersionCells} or {SchemaVersionRects}.");
            }

            if (!string.Equals(cfg.Layer, layerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {fragment.SourceUri}: 'layer' is '{cfg.Layer}' but the asset is loaded for '{layerKey}'.");
            }

            string[] sortedKeys = ParseRegionKeys(cfg.Regions, path, fragment.SourceUri);
            var keyById = new Dictionary<int, string>(sortedKeys.Length);
            for (int i = 0; i < sortedKeys.Length; i++)
            {
                keyById[i + 1] = sortedKeys[i];
            }

            if (cfg.SchemaVersion == SchemaVersionCells)
            {
                if (cfg.Rects != null || cfg.Points != null)
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {fragment.SourceUri}: schemaVersion {SchemaVersionCells} forbids 'rects'/'points'; use schemaVersion {SchemaVersionRects}.");
                }

                List<FieldCellRegionEntry> cells = ParsePoints(cfg.Cells, keyById, path, fragment.SourceUri, "cells");
                return new FragmentStrokes(sortedKeys, Array.Empty<FieldCellRectEntry>(), cells.ToArray());
            }

            if (cfg.Cells != null)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {fragment.SourceUri}: schemaVersion {SchemaVersionRects} forbids 'cells'; use 'rects' and optional 'points'.");
            }

            if (cfg.Rects == null)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {fragment.SourceUri}: schemaVersion {SchemaVersionRects} requires 'rects' (use an empty array when the layer has no painted area).");
            }

            FieldCellRectEntry[] rects = ParseRects(cfg.Rects, keyById, path, fragment.SourceUri);
            FieldCellRegionEntry[] points = cfg.Points == null
                ? Array.Empty<FieldCellRegionEntry>()
                : ParsePoints(cfg.Points, keyById, path, fragment.SourceUri, "points").ToArray();
            return new FragmentStrokes(sortedKeys, rects, points);
        }

        private static string[] ParseRegionKeys(List<string> regions, string path, string source)
        {
            if (regions == null || regions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {source}: 'regions' must contain at least one key.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in regions)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: 'regions' must not contain blank keys.");
                }

                if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: region key '{key}' must not include leading or trailing whitespace.");
                }

                if (!seen.Add(key))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: duplicate region key '{key}'.");
                }
            }

            var sorted = regions.ToArray();
            Array.Sort(sorted, StringComparer.Ordinal);
            return sorted;
        }

        private static FieldCellRectEntry[] ParseRects(
            JsonNode rectsNode, Dictionary<int, string> keyById, string path, string source)
        {
            if (rectsNode is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {source}: 'rects' must be an array of [x0, y0, x1, y1, regionId] entries.");
            }

            var rects = new FieldCellRectEntry[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonArray quintuple || quintuple.Count != 5)
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: each 'rects' entry must be an array of exactly 5 integers.");
                }

                int x0 = RequireInt(quintuple[0], path, source);
                int y0 = RequireInt(quintuple[1], path, source);
                int x1 = RequireInt(quintuple[2], path, source);
                int y1 = RequireInt(quintuple[3], path, source);
                int regionId = RequireInt(quintuple[4], path, source);
                if (x1 < x0 || y1 < y0)
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: rect [{x0},{y0},{x1},{y1},{regionId}] ends precede starts.");
                }

                if (regionId < 1 || !keyById.TryGetValue(regionId, out string? regionKey))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: 'rects' entry references regionId {regionId} which has no key in 'regions'.");
                }

                rects[i] = new FieldCellRectEntry(x0, y0, x1, y1, regionKey);
            }

            return rects;
        }

        private static List<FieldCellRegionEntry> ParsePoints(
            JsonNode? cellsNode, Dictionary<int, string> keyById, string path, string source, string fieldName)
        {
            if (cellsNode is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {source}: '{fieldName}' must be an array of [x, y, regionId] entries.");
            }

            var cells = new List<FieldCellRegionEntry>(array.Count);
            foreach (JsonNode? entry in array)
            {
                if (entry is not JsonArray triple || triple.Count != 3)
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: each '{fieldName}' entry must be an array of exactly 3 integers.");
                }

                int x = RequireInt(triple[0], path, source);
                int y = RequireInt(triple[1], path, source);
                int regionId = RequireInt(triple[2], path, source);
                if (regionId < 1 || !keyById.TryGetValue(regionId, out string? regionKey))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: '{fieldName}' entry [{x},{y},{regionId}] references regionId {regionId} which has no key in 'regions'.");
                }

                cells.Add(new FieldCellRegionEntry(x, y, regionKey));
            }

            return cells;
        }

        private static void ClaimRect(
            List<FieldCellRectEntry> claimedRects,
            Dictionary<long, string> ownerByPoint,
            FieldCellRectEntry rect,
            string path)
        {
            for (int i = 0; i < claimedRects.Count; i++)
            {
                FieldCellRectEntry existing = claimedRects[i];
                if (!Overlaps(rect, existing))
                {
                    continue;
                }

                if (!string.Equals(existing.RegionKey, rect.RegionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' assigns overlapping rects to both '{existing.RegionKey}' and '{rect.RegionKey}' near ({rect.X0},{rect.Y0})-({rect.X1},{rect.Y1}).");
                }
            }

            foreach (KeyValuePair<long, string> pair in ownerByPoint)
            {
                UnpackCell(pair.Key, out int x, out int y);
                if (Contains(rect, x, y) &&
                    !string.Equals(pair.Value, rect.RegionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' assigns cell ({x},{y}) to both '{pair.Value}' and '{rect.RegionKey}'.");
                }
            }
        }

        private static bool ClaimPoint(
            List<FieldCellRectEntry> claimedRects,
            Dictionary<long, string> ownerByPoint,
            FieldCellRegionEntry point,
            string path)
        {
            for (int i = 0; i < claimedRects.Count; i++)
            {
                FieldCellRectEntry rect = claimedRects[i];
                if (Contains(rect, point.X, point.Y) &&
                    !string.Equals(rect.RegionKey, point.RegionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' assigns cell ({point.X},{point.Y}) to both '{rect.RegionKey}' and '{point.RegionKey}'.");
                }

                if (Contains(rect, point.X, point.Y))
                {
                    return false;
                }
            }

            long key = PackCell(point.X, point.Y);
            if (ownerByPoint.TryGetValue(key, out string? existing))
            {
                if (!string.Equals(existing, point.RegionKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' assigns cell ({point.X},{point.Y}) to both '{existing}' and '{point.RegionKey}'.");
                }

                return false;
            }

            ownerByPoint[key] = point.RegionKey;
            return true;
        }

        private static bool Overlaps(in FieldCellRectEntry a, in FieldCellRectEntry b) =>
            !(a.X1 < b.X0 || b.X1 < a.X0 || a.Y1 < b.Y0 || b.Y1 < a.Y0);

        private static bool Contains(in FieldCellRectEntry rect, int x, int y) =>
            x >= rect.X0 && x <= rect.X1 && y >= rect.Y0 && y <= rect.Y1;

        private static int RequireInt(JsonNode? node, string path, string source)
        {
            if (node is JsonValue value && value.TryGetValue<int>(out int integer))
            {
                return integer;
            }

            throw new InvalidOperationException(
                $"Field cells asset '{path}' from {source}: stroke entries must contain integers only.");
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private static void UnpackCell(long key, out int x, out int y)
        {
            x = (int)(key >> 32);
            y = (int)(key & 0xFFFFFFFF);
        }

        private readonly record struct FragmentStrokes(
            string[] SortedKeys,
            FieldCellRectEntry[] Rects,
            FieldCellRegionEntry[] Points);
    }
}
