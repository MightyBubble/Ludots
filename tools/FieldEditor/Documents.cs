using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Tools.FieldEditor
{
    /// <summary>
    /// Authoring document for one discrete-id layer: the region key set plus a sparse
    /// cell → key map. Single-writer by construction (one cell holds one key), saved in
    /// the engine format Fields/cells/&lt;layerId&gt;.json with Ordinal-sorted keys and
    /// deterministic cell ordering.
    /// </summary>
    public sealed class CellsDocument
    {
        public const int SupportedSchemaVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public CellsDocument(string layerKey)
        {
            LayerKey = layerKey;
        }

        public string LayerKey { get; }
        public SortedDictionary<string, string> Regions { get; } = new(StringComparer.Ordinal);
        public Dictionary<(int X, int Y), string> Cells { get; } = new();

        public static string AssetPath(string modRoot, string layerKey) =>
            Path.Combine(modRoot, "assets", "Fields", "cells", $"{layerKey}.json");

        public static string CoreAssetPath(string coreRoot, string layerKey) =>
            Path.Combine(coreRoot, "Fields", "cells", $"{layerKey}.json");

        public static CellsDocument LoadOrNew(string assetPath, string layerKey)
        {
            var document = new CellsDocument(layerKey);
            if (!File.Exists(assetPath))
            {
                return document;
            }

            JsonObject root = JsonNode.Parse(File.ReadAllText(assetPath)) as JsonObject
                ?? throw new InvalidOperationException($"'{assetPath}' is not a JSON object.");
            int schemaVersion = root["schemaVersion"]?.GetValue<int>()
                ?? throw new InvalidOperationException($"'{assetPath}' is missing schemaVersion.");
            if (schemaVersion != 1 && schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' schemaVersion must be 1 or {SupportedSchemaVersion}.");
            }

            string? layer = root["layer"]?.GetValue<string>();
            if (!string.Equals(layer, layerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}' declares layer '{layer}' but was opened for '{layerKey}'.");
            }

            foreach (JsonNode? key in root["regions"]?.AsArray() ?? new JsonArray())
            {
                document.Regions[RequireCanonicalKey(key, assetPath)] = RequireCanonicalKey(key, assetPath);
            }

            if (schemaVersion == 1)
            {
                foreach (JsonNode? entry in root["cells"]?.AsArray() ?? new JsonArray())
                {
                    JsonArray triple = entry as JsonArray
                        ?? throw new InvalidOperationException($"'{assetPath}': each cell must be [x, y, regionIndex].");
                    int x = triple[0]!.GetValue<int>();
                    int y = triple[1]!.GetValue<int>();
                    int index = triple[2]!.GetValue<int>();
                    document.Cells[(x, y)] = RegionKeyAt(document, index, assetPath, x, y);
                }

                return document;
            }

            if (root["cells"] != null)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}': schemaVersion {SupportedSchemaVersion} forbids 'cells'; use 'rects'.");
            }

            foreach (JsonNode? entry in root["rects"]?.AsArray()
                ?? throw new InvalidOperationException($"'{assetPath}': schemaVersion {SupportedSchemaVersion} requires 'rects'."))
            {
                JsonArray quintuple = entry as JsonArray
                    ?? throw new InvalidOperationException($"'{assetPath}': each rect must be [x0, y0, x1, y1, regionIndex].");
                if (quintuple.Count != 5)
                {
                    throw new InvalidOperationException($"'{assetPath}': each rect must be [x0, y0, x1, y1, regionIndex].");
                }

                int x0 = quintuple[0]!.GetValue<int>();
                int y0 = quintuple[1]!.GetValue<int>();
                int x1 = quintuple[2]!.GetValue<int>();
                int y1 = quintuple[3]!.GetValue<int>();
                int index = quintuple[4]!.GetValue<int>();
                string regionKey = RegionKeyAt(document, index, assetPath, x0, y0);
                document.PaintRect(regionKey, x0, y0, x1, y1);
            }

            foreach (JsonNode? entry in root["points"]?.AsArray() ?? new JsonArray())
            {
                JsonArray triple = entry as JsonArray
                    ?? throw new InvalidOperationException($"'{assetPath}': each point must be [x, y, regionIndex].");
                int x = triple[0]!.GetValue<int>();
                int y = triple[1]!.GetValue<int>();
                int index = triple[2]!.GetValue<int>();
                document.Cells[(x, y)] = RegionKeyAt(document, index, assetPath, x, y);
            }

            return document;
        }

        private static string RegionKeyAt(CellsDocument document, int index, string assetPath, int x, int y)
        {
            if (index < 1 || index > document.Regions.Count)
            {
                throw new InvalidOperationException(
                    $"'{assetPath}': cell ({x},{y}) references region index {index} outside 'regions'.");
            }

            return document.Regions.Keys.ElementAt(index - 1);
        }

        public string AddRegion(string key)
        {
            RequireCanonicalKey(key, "<new-region>");
            Regions[key] = key;
            return key;
        }

        public void PaintRect(string regionKey, int x0, int y0, int x1, int y1)
        {
            RequireRegion(regionKey);
            ForRect(x0, y0, x1, y1, (x, y) => Cells[(x, y)] = regionKey);
        }

        public void EraseRect(int x0, int y0, int x1, int y1)
        {
            ForRect(x0, y0, x1, y1, (x, y) => Cells.Remove((x, y)));
        }

        public void RemoveRegion(string key)
        {
            if (!Regions.Remove(key))
            {
                throw new InvalidOperationException($"Region '{key}' does not exist.");
            }

            foreach (var cell in Cells.Where(pair => pair.Value == key).Select(pair => pair.Key).ToList())
            {
                Cells.Remove(cell);
            }
        }

        public void Validate(int maxRegionIds)
        {
            if (Regions.Count > maxRegionIds)
            {
                throw new InvalidOperationException(
                    $"Layer '{LayerKey}' holds {Regions.Count} regions but the catalog caps it at {maxRegionIds}.");
            }

            foreach (((int x, int y), string key) in Cells)
            {
                if (!Regions.ContainsKey(key))
                {
                    throw new InvalidOperationException($"Cell ({x},{y}) references unknown region '{key}'.");
                }
            }
        }

        public void Save(string assetPath, int maxRegionIds)
        {
            Validate(maxRegionIds);
            var points = new List<(int X, int Y, int RegionId)>(Cells.Count);
            foreach (((int x, int y), string key) in Cells)
            {
                points.Add((x, y, RegionIndex(key)));
            }

            var rects = Ludots.Core.Fields.FieldRectCodec.CoalescePoints(points);
            var root = new JsonObject
            {
                ["schemaVersion"] = SupportedSchemaVersion,
                ["layer"] = LayerKey,
                ["regions"] = new JsonArray(Regions.Keys.Select(key => JsonValue.Create(key)).ToArray()),
                ["rects"] = new JsonArray(rects
                    .OrderBy(stroke => stroke.Y0)
                    .ThenBy(stroke => stroke.X0)
                    .ThenBy(stroke => stroke.RegionId)
                    .Select(stroke => (JsonNode)new JsonArray(
                        stroke.X0, stroke.Y0, stroke.X1, stroke.Y1, stroke.RegionId))
                    .ToArray()),
            };

            string? directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = assetPath + ".tmp";
            File.WriteAllText(tempPath, root.ToJsonString(JsonOptions) + "\n");
            File.Move(tempPath, assetPath, overwrite: true);
        }

        public int RegionIndex(string key) => Regions.Keys.TakeWhile(existing => !string.Equals(existing, key, StringComparison.Ordinal)).Count() + 1;

        private void RequireRegion(string key)
        {
            if (!Regions.ContainsKey(key))
            {
                throw new InvalidOperationException($"Region '{key}' does not exist; add it first (regions add).");
            }
        }

        private static void ForRect(int x0, int y0, int x1, int y1, Action<int, int> apply)
        {
            if (x1 < x0 || y1 < y0)
            {
                throw new InvalidOperationException("Rectangle ends must not precede their starts.");
            }

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    apply(x, y);
                }
            }
        }

        private static string RequireCanonicalKey(JsonNode? node, string assetPath)
        {
            string key = node?.GetValue<string>() ?? throw new InvalidOperationException(
                $"'{assetPath}': region keys must be non-blank strings.");
            if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{assetPath}': region key '{key}' must be non-blank and carry no surrounding whitespace.");
            }

            return key;
        }
    }

    /// <summary>
    /// Thin editor view over Fields/layers.json: lists discrete-id layers and appends
    /// new layer entries (the new-layer wizard). The schema authority stays in the
    /// engine loader; this file only reads and writes the same shape.
    /// </summary>
    public sealed class CatalogDocument
    {
        public static string AssetPath(string modRoot) => Path.Combine(modRoot, "assets", "Fields", "layers.json");

        public static JsonArray LoadOrNew(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return new JsonArray();
            }

            return JsonNode.Parse(File.ReadAllText(assetPath)) as JsonArray
                ?? throw new InvalidOperationException($"'{assetPath}' must be a JSON array.");
        }

        public static JsonArray LoadCore(string coreRoot)
        {
            string path = Path.Combine(coreRoot, "Fields", "layers.json");
            return LoadOrNew(path);
        }

        public static void AppendLayer(JsonArray layers, string id, int cellSizeCm, int chunkSizeCells, int maxRegionIds, string writerDomain)
        {
            foreach (JsonNode? existing in layers)
            {
                if (string.Equals(existing!["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Layer '{id}' already exists in Fields/layers.json.");
                }
            }

            layers.Add(new JsonObject
            {
                ["id"] = id,
                ["kind"] = "discreteId",
                ["cellSizeCm"] = cellSizeCm,
                ["chunkSizeCells"] = chunkSizeCells,
                ["maxRegionIds"] = maxRegionIds,
                ["writerDomain"] = writerDomain,
                ["default"] = 0,
                ["persistent"] = true,
            });
        }

        public static void Save(string assetPath, JsonArray layers)
        {
            string? directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = assetPath + ".tmp";
            File.WriteAllText(tempPath, layers.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.Move(tempPath, assetPath, overwrite: true);
        }

        public static (int CellSizeCm, int MaxRegionIds)? TryGetDiscreteLayer(JsonArray layers, string id)
        {
            foreach (JsonNode? layer in layers)
            {
                if (!string.Equals(layer!["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(layer["kind"]?.GetValue<string>(), "discreteId", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return (layer["cellSizeCm"]!.GetValue<int>(), layer["maxRegionIds"]?.GetValue<int>() ?? 256);
            }

            return null;
        }
    }
}
