using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    public readonly record struct FieldCellRegionEntry(int X, int Y, string RegionKey);

    public sealed class FieldCellsAsset
    {
        /// <summary>Layer key this asset was loaded for; always matches the requested layer.</summary>
        public required string LayerKey { get; init; }

        /// <summary>Region keys in Ordinal order; regionId = index + 1, id 0 reserved for "no region".</summary>
        public required string[] RegionKeys { get; init; }

        public required FieldCellRegionEntry[] Cells { get; init; }
    }

    /// <summary>
    /// Loads and merges the authored cell data of one layer from
    /// <c>Fields/cells/&lt;layerKey&gt;.json</c> across Core + mod fragments.
    /// Region ids are derived from the Ordinal-sorted union of all region keys,
    /// so fragment load order never changes an id. Two fragments assigning the
    /// same cell to different regions fail the load with both keys named.
    /// </summary>
    public sealed class FieldCellsConfigLoader
    {
        public const string CellsDirectory = "Fields/cells";
        public const int SupportedSchemaVersion = 1;

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

            var parsed = new List<(string[] SortedKeys, List<FieldCellRegionEntry> Cells)>(fragments.Count);
            var allKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigFragment fragment in fragments)
            {
                (string[] SortedKeys, List<FieldCellRegionEntry> Cells) entry =
                    ParseFragment(fragment, layerKey, path);
                parsed.Add(entry);
                allKeys.UnionWith(entry.SortedKeys);
            }

            var sortedUnion = new string[allKeys.Count];
            allKeys.CopyTo(sortedUnion);
            Array.Sort(sortedUnion, StringComparer.Ordinal);
            var globalIds = new Dictionary<string, int>(sortedUnion.Length, StringComparer.Ordinal);
            for (int i = 0; i < sortedUnion.Length; i++)
            {
                globalIds[sortedUnion[i]] = i + 1;
            }

            var cells = new List<FieldCellRegionEntry>();
            var ownerByCell = new Dictionary<long, string>();
            foreach ((string[] sortedKeys, List<FieldCellRegionEntry> fragmentCells) in parsed)
            {
                foreach (FieldCellRegionEntry cell in fragmentCells)
                {
                    long key = PackCell(cell.X, cell.Y);
                    if (ownerByCell.TryGetValue(key, out string? existing))
                    {
                        if (!string.Equals(existing, cell.RegionKey, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Field cells asset '{path}' assigns cell ({cell.X},{cell.Y}) to both '{existing}' and '{cell.RegionKey}'.");
                        }

                        continue;
                    }

                    ownerByCell[key] = cell.RegionKey;
                    cells.Add(new FieldCellRegionEntry(cell.X, cell.Y, cell.RegionKey));
                }
            }

            return new FieldCellsAsset
            {
                LayerKey = layerKey,
                RegionKeys = sortedUnion,
                Cells = cells.ToArray(),
            };
        }

        private static (string[] SortedKeys, List<FieldCellRegionEntry> Cells) ParseFragment(
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

            if (cfg.SchemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {fragment.SourceUri}: schemaVersion {cfg.SchemaVersion} is not supported; expected {SupportedSchemaVersion}.");
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

            List<FieldCellRegionEntry> cells = ParseCells(cfg.Cells, keyById, path, fragment.SourceUri);
            return (sortedKeys, cells);
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

        private static List<FieldCellRegionEntry> ParseCells(
            JsonNode? cellsNode, Dictionary<int, string> keyById, string path, string source)
        {
            if (cellsNode is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Field cells asset '{path}' from {source}: 'cells' must be an array of [x, y, regionId] entries.");
            }

            var cells = new List<FieldCellRegionEntry>(array.Count);
            foreach (JsonNode? entry in array)
            {
                if (entry is not JsonArray triple || triple.Count != 3)
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: each 'cells' entry must be an array of exactly 3 integers.");
                }

                int x = RequireInt(triple[0], path, source);
                int y = RequireInt(triple[1], path, source);
                int regionId = RequireInt(triple[2], path, source);
                if (regionId < 1 || !keyById.TryGetValue(regionId, out string? regionKey))
                {
                    throw new InvalidOperationException(
                        $"Field cells asset '{path}' from {source}: 'cells' entry [{x},{y},{regionId}] references regionId {regionId} which has no key in 'regions'.");
                }

                cells.Add(new FieldCellRegionEntry(x, y, regionKey));
            }

            return cells;
        }

        private static int RequireInt(JsonNode? node, string path, string source)
        {
            if (node is JsonValue value && value.TryGetValue<int>(out int integer))
            {
                return integer;
            }

            throw new InvalidOperationException(
                $"Field cells asset '{path}' from {source}: 'cells' entries must contain integers only.");
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }
}
