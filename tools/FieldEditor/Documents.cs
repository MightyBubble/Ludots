using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Fields;

namespace Ludots.Tools.FieldEditor
{
    public sealed class CellsDocument
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        private ChunkedField2D<int> _field;

        public CellsDocument(string layerKey, int chunkSizeCells = 16)
        {
            if (string.IsNullOrWhiteSpace(layerKey))
            {
                throw new ArgumentException("Layer key is required.", nameof(layerKey));
            }

            LayerKey = layerKey;
            _field = CreateField(chunkSizeCells);
        }

        public string LayerKey { get; }
        public SortedDictionary<string, string> Regions { get; } = new(StringComparer.Ordinal);
        public ChunkedField2D<int> Field => _field;
        public int CellCount => _field.NonDefaultCount;

        public static string AssetPath(string modRoot, string layerKey) =>
            Path.Combine(modRoot, "assets", "Fields", "cells", $"{layerKey}.json");

        public static string CoreAssetPath(string coreRoot, string layerKey) =>
            Path.Combine(coreRoot, "Fields", "cells", $"{layerKey}.json");

        public static CellsDocument LoadOrNew(
            string assetPath,
            string layerKey,
            int chunkSizeCells = 16)
        {
            if (!File.Exists(assetPath))
            {
                return new CellsDocument(layerKey, chunkSizeCells);
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(assetPath)) as JsonObject
                    ?? throw new InvalidOperationException($"'{assetPath}' is not a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"'{assetPath}' is not valid JSON: {ex.Message}", ex);
            }

            return LoadJson(root, layerKey, chunkSizeCells, assetPath);
        }

        internal static CellsDocument LoadSnapshot(
            JsonObject root,
            string layerKey,
            int chunkSizeCells,
            string context) =>
            LoadJson(root, layerKey, chunkSizeCells, context);

        public string AddRegion(string key)
        {
            RequireCanonicalKey(JsonValue.Create(key), "<new-region>");
            if (Regions.ContainsKey(key))
            {
                throw new InvalidOperationException($"Region '{key}' already exists.");
            }

            string[] oldKeys = Regions.Keys.ToArray();
            Regions.Add(key, key);
            ReindexField(oldKeys, static existing => existing);
            return key;
        }

        public void RemoveRegion(string key)
        {
            string[] oldKeys = Regions.Keys.ToArray();
            if (!Regions.Remove(key))
            {
                throw new InvalidOperationException($"Region '{key}' does not exist.");
            }

            ReindexField(
                oldKeys,
                existing => string.Equals(existing, key, StringComparison.Ordinal) ? null : existing);
        }

        public void RenameRegion(string from, string to)
        {
            RequireCanonicalKey(JsonValue.Create(to), "<rename>");
            if (!Regions.ContainsKey(from))
            {
                throw new InvalidOperationException($"Region '{from}' does not exist.");
            }

            if (Regions.ContainsKey(to))
            {
                throw new InvalidOperationException($"Region '{to}' already exists.");
            }

            string[] oldKeys = Regions.Keys.ToArray();
            Regions.Remove(from);
            Regions.Add(to, to);
            ReindexField(
                oldKeys,
                existing => string.Equals(existing, from, StringComparison.Ordinal) ? to : existing);
        }

        public void PaintRect(string regionKey, int x0, int y0, int x1, int y1)
        {
            FillRect(x0, y0, x1, y1, RegionIndex(regionKey));
        }

        public void EraseRect(int x0, int y0, int x1, int y1)
        {
            FillRect(x0, y0, x1, y1, 0);
        }

        public void PaintCell(string regionKey, int x, int y)
        {
            _field.Set(new FieldCell2D(x, y), RegionIndex(regionKey));
        }

        public bool TryGetCellKey(int x, int y, out string? regionKey)
        {
            int regionId = _field.Get(new FieldCell2D(x, y));
            if (regionId == 0)
            {
                regionKey = null;
                return false;
            }

            regionKey = RegionKeyAt(this, regionId, "<field>", x, y);
            return true;
        }

        public IEnumerable<(FieldCell2D Cell, string RegionKey)> EnumerateCells()
        {
            string[] keys = Regions.Keys.ToArray();
            foreach ((FieldCell2D cell, int regionId) in EnumerateField(_field))
            {
                if (regionId < 1 || regionId > keys.Length)
                {
                    throw new InvalidOperationException(
                        $"Cell ({cell.X},{cell.Y}) references unknown region id {regionId}.");
                }

                yield return (cell, keys[regionId - 1]);
            }
        }

        public int GetRegionCellCount(string key)
        {
            int regionId = RegionIndex(key);
            int count = 0;
            foreach ((_, int value) in EnumerateField(_field))
            {
                if (value == regionId)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetBounds(out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = int.MaxValue;
            minY = int.MaxValue;
            maxX = int.MinValue;
            maxY = int.MinValue;
            foreach ((FieldCell2D cell, _) in EnumerateField(_field))
            {
                minX = Math.Min(minX, cell.X);
                minY = Math.Min(minY, cell.Y);
                maxX = Math.Max(maxX, cell.X);
                maxY = Math.Max(maxY, cell.Y);
            }

            return minX != int.MaxValue;
        }

        public CellsDocument CloneSnapshot()
        {
            var clone = new CellsDocument(LayerKey, _field.Grid.ChunkSizeCells);
            foreach (string key in Regions.Keys)
            {
                clone.Regions.Add(key, key);
            }

            clone._field = CopyField(_field);
            return clone;
        }

        public void RestoreFrom(CellsDocument other)
        {
            if (!string.Equals(LayerKey, other.LayerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot restore layer '{other.LayerKey}' into layer '{LayerKey}'.");
            }

            Regions.Clear();
            foreach (string key in other.Regions.Keys)
            {
                Regions.Add(key, key);
            }

            _field = CopyField(other._field);
        }

        public void Validate(int maxRegionIds)
        {
            if (Regions.Count > maxRegionIds)
            {
                throw new InvalidOperationException(
                    $"Layer '{LayerKey}' holds {Regions.Count} regions but the catalog caps it at {maxRegionIds}.");
            }

            foreach ((FieldCell2D cell, int regionId) in EnumerateField(_field))
            {
                if (regionId < 1 || regionId > Regions.Count)
                {
                    throw new InvalidOperationException(
                        $"Cell ({cell.X},{cell.Y}) references unknown region id {regionId}.");
                }
            }
        }

        public void Save(string assetPath, int maxRegionIds)
        {
            Validate(maxRegionIds);
            WriteJsonAtomic(assetPath, ToSnapshotJson());
        }

        public int RegionIndex(string key)
        {
            RequireRegion(key);
            return Regions.Keys.TakeWhile(
                existing => !string.Equals(existing, key, StringComparison.Ordinal)).Count() + 1;
        }

        internal JsonObject ToSnapshotJson()
        {
            List<FieldCellRectStroke> rects = FieldRectCodec.CoalesceFromField(_field);
            return new JsonObject
            {
                ["layer"] = LayerKey,
                ["regions"] = new JsonArray(
                    Regions.Keys.Select(key => JsonValue.Create(key)).ToArray()),
                ["rects"] = new JsonArray(rects
                    .OrderBy(stroke => stroke.Y0)
                    .ThenBy(stroke => stroke.X0)
                    .ThenBy(stroke => stroke.RegionId)
                    .Select(stroke => (JsonNode)new JsonArray(
                        stroke.X0,
                        stroke.Y0,
                        stroke.X1,
                        stroke.Y1,
                        stroke.RegionId))
                    .ToArray()),
            };
        }

        private static CellsDocument LoadJson(
            JsonObject root,
            string layerKey,
            int chunkSizeCells,
            string context)
        {
            RequireOnlyProperties(
                root,
                context,
                "layer",
                "regions",
                "rects",
                "points");

            string? layer = root["layer"]?.GetValue<string>();
            if (!string.Equals(layer, layerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{context}' declares layer '{layer}' but was opened for '{layerKey}'.");
            }

            var document = new CellsDocument(layerKey, chunkSizeCells);
            JsonArray regions = root["regions"] as JsonArray
                ?? throw new InvalidOperationException($"'{context}' requires a 'regions' array.");
            foreach (JsonNode? keyNode in regions)
            {
                string key = RequireCanonicalKey(keyNode, context);
                if (!document.Regions.TryAdd(key, key))
                {
                    throw new InvalidOperationException(
                        $"'{context}' contains duplicate region key '{key}'.");
                }
            }

            if (root["cells"] != null)
            {
                throw new InvalidOperationException(
                    $"'{context}': 'cells' is not accepted; author rectangles via 'rects'.");
            }

            JsonArray rects = root["rects"] as JsonArray
                ?? throw new InvalidOperationException(
                    $"'{context}' requires 'rects'.");
            foreach (JsonNode? entry in rects)
            {
                JsonArray rect = RequireArray(
                    entry,
                    5,
                    context,
                    "rect",
                    "[x0, y0, x1, y1, regionIndex]");
                int x0 = rect[0]!.GetValue<int>();
                int y0 = rect[1]!.GetValue<int>();
                int x1 = rect[2]!.GetValue<int>();
                int y1 = rect[3]!.GetValue<int>();
                int regionId = rect[4]!.GetValue<int>();
                RegionKeyAt(document, regionId, context, x0, y0);
                document.FillRect(x0, y0, x1, y1, regionId);
            }

            if (root["points"] is JsonArray points)
            {
                foreach (JsonNode? entry in points)
                {
                    JsonArray point = RequireArray(
                        entry,
                        3,
                        context,
                        "point",
                        "[x, y, regionIndex]");
                    int x = point[0]!.GetValue<int>();
                    int y = point[1]!.GetValue<int>();
                    int regionId = point[2]!.GetValue<int>();
                    RegionKeyAt(document, regionId, context, x, y);
                    document._field.Set(new FieldCell2D(x, y), regionId);
                }
            }
            else if (root["points"] != null)
            {
                throw new InvalidOperationException($"'{context}': 'points' must be an array.");
            }

            return document;
        }

        private void FillRect(int x0, int y0, int x1, int y1, int regionId)
        {
            if (x1 < x0 || y1 < y0)
            {
                throw new InvalidOperationException("Rectangle ends must not precede their starts.");
            }

            _field.FillRect(x0, y0, x1, y1, regionId);
        }

        private void ReindexField(string[] oldKeys, Func<string, string?> keyTransform)
        {
            string[] newKeys = Regions.Keys.ToArray();
            var idByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < newKeys.Length; index++)
            {
                idByKey.Add(newKeys[index], index + 1);
            }

            ChunkedField2D<int> reindexed = CreateField(_field.Grid.ChunkSizeCells);
            foreach ((FieldCell2D cell, int oldRegionId) in EnumerateField(_field))
            {
                if (oldRegionId < 1 || oldRegionId > oldKeys.Length)
                {
                    throw new InvalidOperationException(
                        $"Cell ({cell.X},{cell.Y}) references unknown region id {oldRegionId}.");
                }

                string? transformed = keyTransform(oldKeys[oldRegionId - 1]);
                if (transformed != null)
                {
                    reindexed.Set(cell, idByKey[transformed]);
                }
            }

            _field = reindexed;
        }

        private void RequireRegion(string key)
        {
            if (!Regions.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Region '{key}' does not exist; add it first (regions-add).");
            }
        }

        private static string RegionKeyAt(
            CellsDocument document,
            int regionId,
            string context,
            int x,
            int y)
        {
            if (regionId < 1 || regionId > document.Regions.Count)
            {
                throw new InvalidOperationException(
                    $"'{context}': cell ({x},{y}) references region index {regionId} outside 'regions'.");
            }

            return document.Regions.Keys.ElementAt(regionId - 1);
        }

        private static ChunkedField2D<int> CopyField(ChunkedField2D<int> source)
        {
            ChunkedField2D<int> copy = CreateField(source.Grid.ChunkSizeCells);
            foreach ((FieldCell2D cell, int value) in EnumerateField(source))
            {
                copy.Set(cell, value);
            }

            return copy;
        }

        private static IEnumerable<(FieldCell2D Cell, int Value)> EnumerateField(
            ChunkedField2D<int> field)
        {
            int cellsPerChunk = field.Grid.ChunkSizeCells * field.Grid.ChunkSizeCells;
            for (int chunkIndex = 0; chunkIndex < field.ChunkCount; chunkIndex++)
            {
                FieldChunk2D<int> chunk = field.GetChunkAt(chunkIndex);
                for (int local = 0; local < cellsPerChunk; local++)
                {
                    int value = chunk.Get(local);
                    if (value != 0)
                    {
                        yield return (
                            field.Grid.CellFromChunkLocal(
                                chunk.ChunkX,
                                chunk.ChunkY,
                                local),
                            value);
                    }
                }
            }
        }

        private static ChunkedField2D<int> CreateField(int chunkSizeCells) =>
            new(
                new FieldGridSpec2D(cellSizeCm: 1, chunkSizeCells),
                defaultValue: 0);

        private static JsonArray RequireArray(
            JsonNode? node,
            int count,
            string context,
            string entryName,
            string shape)
        {
            if (node is not JsonArray array || array.Count != count)
            {
                throw new InvalidOperationException(
                    $"'{context}': each {entryName} must be {shape}.");
            }

            return array;
        }

        private static string RequireCanonicalKey(JsonNode? node, string context)
        {
            string key = node?.GetValue<string>() ?? throw new InvalidOperationException(
                $"'{context}': region keys must be non-blank strings.");
            if (string.IsNullOrWhiteSpace(key) ||
                !string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{context}': region key '{key}' must be non-blank and carry no surrounding whitespace.");
            }

            return key;
        }

        private static void RequireOnlyProperties(
            JsonObject root,
            string context,
            params string[] allowed)
        {
            var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach ((string propertyName, _) in root)
            {
                if (!allowedNames.Contains(propertyName))
                {
                    throw new InvalidOperationException(
                        $"'{context}' contains unknown property '{propertyName}'.");
                }
            }
        }

        private static void WriteJsonAtomic(string path, JsonObject root)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, root.ToJsonString(JsonOptions) + "\n");
            File.Move(tempPath, path, overwrite: true);
        }
    }

    public sealed class CatalogDocument
    {
        public static string AssetPath(string modRoot) =>
            Path.Combine(modRoot, "assets", "Fields", "layers.json");

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

        public static void AppendLayer(
            JsonArray layers,
            string id,
            int cellSizeCm,
            int chunkSizeCells,
            int maxRegionIds,
            string writerDomain)
        {
            foreach (JsonNode? existing in layers)
            {
                if (string.Equals(existing!["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Layer '{id}' already exists in Fields/layers.json.");
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
            File.WriteAllText(
                tempPath,
                layers.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.Move(tempPath, assetPath, overwrite: true);
        }

        public static (int CellSizeCm, int ChunkSizeCells, int MaxRegionIds)?
            TryGetDiscreteLayer(JsonArray layers, string id)
        {
            foreach (JsonNode? layer in layers)
            {
                if (!string.Equals(layer!["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(
                    layer["kind"]?.GetValue<string>(),
                    "discreteId",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return (
                    layer["cellSizeCm"]!.GetValue<int>(),
                    layer["chunkSizeCells"]!.GetValue<int>(),
                    layer["maxRegionIds"]?.GetValue<int>() ?? 256);
            }

            return null;
        }
    }
}
