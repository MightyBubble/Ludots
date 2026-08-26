using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Tools.FieldEditor
{
    public static class HistoryStore
    {
        public const int SupportedSchemaVersion = 1;

        public static string HistoryPath(string assetPath) =>
            Path.ChangeExtension(assetPath, ".field-editor-history.json");

        public static void Push(string assetPath, CellsDocument snapshot)
        {
            HistoryState state = Load(assetPath, snapshot.LayerKey);
            state.Undo.Add(CreateSnapshot(
                assetPath,
                snapshot,
                state.ActiveBrushKey));
            state.Redo.Clear();
            Save(assetPath, state);
        }

        internal static JsonObject CaptureSnapshot(
            string assetPath,
            CellsDocument document)
        {
            HistoryState state = Load(assetPath, document.LayerKey);
            return CreateSnapshot(assetPath, document, state.ActiveBrushKey);
        }

        internal static void PushSnapshot(
            string assetPath,
            string layerKey,
            JsonObject snapshot)
        {
            HistoryState state = Load(assetPath, layerKey);
            state.Undo.Add(snapshot);
            state.Redo.Clear();
            Save(assetPath, state);
        }

        public static CellsDocument? Undo(string assetPath, CellsDocument current)
        {
            HistoryState state = Load(assetPath, current.LayerKey);
            if (state.Undo.Count == 0)
            {
                return null;
            }

            HistoryEntry previousEntry = SnapshotAt(
                state.Undo,
                state.Undo.Count - 1,
                HistoryPath(assetPath));
            CellsDocument previous = CellsDocument.LoadSnapshot(
                (JsonObject)previousEntry.Cells.DeepClone(),
                current.LayerKey,
                current.Field.Grid.ChunkSizeCells,
                HistoryPath(assetPath));

            state.Redo.Add(CreateSnapshot(assetPath, current, state.ActiveBrushKey));
            state.Undo.RemoveAt(state.Undo.Count - 1);
            current.RestoreFrom(previous);
            FieldEditorMetadataStore.RestoreColors(
                assetPath,
                current,
                previousEntry.RegionColors);
            RestoreActiveBrush(state, current, previousEntry.ActiveBrushKey);
            Save(assetPath, state);
            return current;
        }

        public static CellsDocument? Redo(string assetPath, CellsDocument current)
        {
            HistoryState state = Load(assetPath, current.LayerKey);
            if (state.Redo.Count == 0)
            {
                return null;
            }

            HistoryEntry nextEntry = SnapshotAt(
                state.Redo,
                state.Redo.Count - 1,
                HistoryPath(assetPath));
            CellsDocument next = CellsDocument.LoadSnapshot(
                (JsonObject)nextEntry.Cells.DeepClone(),
                current.LayerKey,
                current.Field.Grid.ChunkSizeCells,
                HistoryPath(assetPath));

            state.Undo.Add(CreateSnapshot(assetPath, current, state.ActiveBrushKey));
            state.Redo.RemoveAt(state.Redo.Count - 1);
            current.RestoreFrom(next);
            FieldEditorMetadataStore.RestoreColors(
                assetPath,
                current,
                nextEntry.RegionColors);
            RestoreActiveBrush(state, current, nextEntry.ActiveBrushKey);
            Save(assetPath, state);
            return current;
        }

        public static string? GetActiveBrushKey(string assetPath, string layerKey)
        {
            return Load(assetPath, layerKey).ActiveBrushKey;
        }

        public static void SetActiveBrushKey(
            string assetPath,
            CellsDocument document,
            string regionKey)
        {
            document.RegionIndex(regionKey);
            HistoryState state = Load(assetPath, document.LayerKey);
            state.ActiveBrushKey = regionKey;
            Save(assetPath, state);
        }

        public static void RemoveRegionKey(
            string assetPath,
            string layerKey,
            string regionKey)
        {
            HistoryState state = Load(assetPath, layerKey);
            if (string.Equals(state.ActiveBrushKey, regionKey, StringComparison.Ordinal))
            {
                state.ActiveBrushKey = null;
                Save(assetPath, state);
            }
        }

        public static void RenameRegionKey(
            string assetPath,
            string layerKey,
            string from,
            string to)
        {
            HistoryState state = Load(assetPath, layerKey);
            if (string.Equals(state.ActiveBrushKey, from, StringComparison.Ordinal))
            {
                state.ActiveBrushKey = to;
                Save(assetPath, state);
            }
        }

        private static HistoryState Load(string assetPath, string layerKey)
        {
            string historyPath = HistoryPath(assetPath);
            if (!File.Exists(historyPath))
            {
                return new HistoryState(layerKey);
            }

            JsonObject root = ParseObject(historyPath);
            RequireOnlyProperties(
                root,
                historyPath,
                "schemaVersion",
                "layer",
                "activeBrushKey",
                "undo",
                "redo");

            int schemaVersion = root["schemaVersion"]?.GetValue<int>()
                ?? throw new InvalidOperationException(
                    $"'{historyPath}' is missing schemaVersion.");
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"'{historyPath}' schemaVersion must be {SupportedSchemaVersion}.");
            }

            string? storedLayer = root["layer"]?.GetValue<string>();
            if (!string.Equals(storedLayer, layerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{historyPath}' declares layer '{storedLayer}' but was opened for '{layerKey}'.");
            }

            JsonArray undo = root["undo"] as JsonArray
                ?? throw new InvalidOperationException($"'{historyPath}' requires an 'undo' array.");
            JsonArray redo = root["redo"] as JsonArray
                ?? throw new InvalidOperationException($"'{historyPath}' requires a 'redo' array.");
            ValidateSnapshotArray(undo, historyPath, "undo");
            ValidateSnapshotArray(redo, historyPath, "redo");

            string? activeBrushKey = root["activeBrushKey"]?.GetValue<string>();
            if (activeBrushKey != null && string.IsNullOrWhiteSpace(activeBrushKey))
            {
                throw new InvalidOperationException(
                    $"'{historyPath}' activeBrushKey must be null or a non-blank region key.");
            }

            return new HistoryState(layerKey, activeBrushKey, undo, redo);
        }

        private static void Save(string assetPath, HistoryState state)
        {
            var root = new JsonObject
            {
                ["schemaVersion"] = SupportedSchemaVersion,
                ["layer"] = state.LayerKey,
                ["activeBrushKey"] = state.ActiveBrushKey,
                ["undo"] = state.Undo.DeepClone(),
                ["redo"] = state.Redo.DeepClone(),
            };
            EditorSidecarJson.WriteAtomic(HistoryPath(assetPath), root);
        }

        private static JsonObject CreateSnapshot(
            string assetPath,
            CellsDocument document,
            string? activeBrushKey)
        {
            return new JsonObject
            {
                ["cells"] = document.ToSnapshotJson(),
                ["activeBrushKey"] = activeBrushKey,
                ["regionColors"] =
                    FieldEditorMetadataStore.CaptureColors(assetPath, document),
            };
        }

        private static HistoryEntry SnapshotAt(
            JsonArray snapshots,
            int index,
            string path)
        {
            JsonObject snapshot = snapshots[index] as JsonObject
                ?? throw new InvalidOperationException(
                    $"'{path}' history entry {index} must be a cells snapshot object.");
            RequireOnlyProperties(
                snapshot,
                $"{path} history entry {index}",
                "cells",
                "activeBrushKey",
                "regionColors");
            JsonObject cells = snapshot["cells"] as JsonObject
                ?? throw new InvalidOperationException(
                    $"'{path}' history entry {index} requires a 'cells' object.");
            JsonObject regionColors = snapshot["regionColors"] as JsonObject
                ?? throw new InvalidOperationException(
                    $"'{path}' history entry {index} requires a 'regionColors' object.");
            string? activeBrushKey = snapshot["activeBrushKey"]?.GetValue<string>();
            if (activeBrushKey != null && string.IsNullOrWhiteSpace(activeBrushKey))
            {
                throw new InvalidOperationException(
                    $"'{path}' history entry {index} activeBrushKey must be null or non-blank.");
            }

            return new HistoryEntry(cells, regionColors, activeBrushKey);
        }

        private static void ValidateSnapshotArray(
            JsonArray snapshots,
            string path,
            string stackName)
        {
            for (int index = 0; index < snapshots.Count; index++)
            {
                SnapshotAt(snapshots, index, $"{path} {stackName}");
            }
        }

        private static void RestoreActiveBrush(
            HistoryState state,
            CellsDocument document,
            string? activeBrushKey)
        {
            if (activeBrushKey != null)
            {
                document.RegionIndex(activeBrushKey);
            }

            state.ActiveBrushKey = activeBrushKey;
        }

        private static JsonObject ParseObject(string path)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidOperationException($"'{path}' must be a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"'{path}' is not valid JSON: {ex.Message}", ex);
            }
        }

        private static void RequireOnlyProperties(
            JsonObject root,
            string path,
            params string[] allowed)
        {
            var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach ((string propertyName, _) in root)
            {
                if (!allowedNames.Contains(propertyName))
                {
                    throw new InvalidOperationException(
                        $"'{path}' contains unknown property '{propertyName}'.");
                }
            }
        }

        private sealed class HistoryState
        {
            public HistoryState(string layerKey)
                : this(layerKey, null, new JsonArray(), new JsonArray())
            {
            }

            public HistoryState(
                string layerKey,
                string? activeBrushKey,
                JsonArray undo,
                JsonArray redo)
            {
                LayerKey = layerKey;
                ActiveBrushKey = activeBrushKey;
                Undo = undo;
                Redo = redo;
            }

            public string LayerKey { get; }
            public string? ActiveBrushKey { get; set; }
            public JsonArray Undo { get; }
            public JsonArray Redo { get; }
        }

        private readonly record struct HistoryEntry(
            JsonObject Cells,
            JsonObject RegionColors,
            string? ActiveBrushKey);
    }

    public static class FieldEditorMetadataStore
    {
        public const int SupportedSchemaVersion = 1;

        public static string MetadataPath(string assetPath) =>
            Path.ChangeExtension(assetPath, ".field-editor-meta.json");

        internal static JsonObject CaptureColors(
            string assetPath,
            CellsDocument document)
        {
            MetadataState state = Load(assetPath, document.LayerKey);
            ValidateRegionKeys(state, document);
            return ToColorsJson(state);
        }

        internal static void RestoreColors(
            string assetPath,
            CellsDocument document,
            JsonObject regionColors)
        {
            MetadataState state = ParseColors(
                regionColors,
                document.LayerKey,
                HistoryStore.HistoryPath(assetPath));
            ValidateRegionKeys(state, document);
            Save(assetPath, state);
        }

        public static IReadOnlyDictionary<string, string> GetColors(
            string assetPath,
            CellsDocument document)
        {
            MetadataState state = Load(assetPath, document.LayerKey);
            ValidateRegionKeys(state, document);
            return state.RegionColors;
        }

        public static string SetColor(
            string assetPath,
            CellsDocument document,
            string regionKey,
            string color)
        {
            document.RegionIndex(regionKey);
            string normalized = NormalizeColor(color);
            MetadataState state = Load(assetPath, document.LayerKey);
            ValidateRegionKeys(state, document);
            state.RegionColors[regionKey] = normalized;
            Save(assetPath, state);
            return normalized;
        }

        public static void RemoveRegion(
            string assetPath,
            string layerKey,
            string regionKey)
        {
            MetadataState state = Load(assetPath, layerKey);
            if (state.RegionColors.Remove(regionKey))
            {
                Save(assetPath, state);
            }
        }

        public static void RenameRegion(
            string assetPath,
            string layerKey,
            string from,
            string to)
        {
            MetadataState state = Load(assetPath, layerKey);
            if (!state.RegionColors.Remove(from, out string? color))
            {
                return;
            }

            if (state.RegionColors.ContainsKey(to))
            {
                throw new InvalidOperationException(
                    $"Editor metadata already contains a color for region '{to}'.");
            }

            state.RegionColors.Add(to, color);
            Save(assetPath, state);
        }

        private static MetadataState Load(string assetPath, string layerKey)
        {
            string metadataPath = MetadataPath(assetPath);
            if (!File.Exists(metadataPath))
            {
                return new MetadataState(layerKey);
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject
                    ?? throw new InvalidOperationException(
                        $"'{metadataPath}' must be a JSON object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"'{metadataPath}' is not valid JSON: {ex.Message}",
                    ex);
            }

            RequireOnlyProperties(
                root,
                metadataPath,
                "schemaVersion",
                "layer",
                "regionColors");

            int schemaVersion = root["schemaVersion"]?.GetValue<int>()
                ?? throw new InvalidOperationException(
                    $"'{metadataPath}' is missing schemaVersion.");
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"'{metadataPath}' schemaVersion must be {SupportedSchemaVersion}.");
            }

            string? storedLayer = root["layer"]?.GetValue<string>();
            if (!string.Equals(storedLayer, layerKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{metadataPath}' declares layer '{storedLayer}' but was opened for '{layerKey}'.");
            }

            JsonObject colors = root["regionColors"] as JsonObject
                ?? throw new InvalidOperationException(
                    $"'{metadataPath}' requires a 'regionColors' object.");
            return ParseColors(colors, layerKey, metadataPath);
        }

        private static MetadataState ParseColors(
            JsonObject colors,
            string layerKey,
            string context)
        {
            var state = new MetadataState(layerKey);
            foreach ((string regionKey, JsonNode? colorNode) in colors)
            {
                string color = colorNode?.GetValue<string>()
                    ?? throw new InvalidOperationException(
                        $"'{context}' color for '{regionKey}' must be a string.");
                string normalized = NormalizeColor(color);
                if (!string.Equals(color, normalized, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{context}' color for '{regionKey}' must use canonical #RRGGBB form.");
                }

                state.RegionColors.Add(regionKey, color);
            }

            return state;
        }

        private static void Save(string assetPath, MetadataState state)
        {
            var root = new JsonObject
            {
                ["schemaVersion"] = SupportedSchemaVersion,
                ["layer"] = state.LayerKey,
                ["regionColors"] = ToColorsJson(state),
            };
            EditorSidecarJson.WriteAtomic(MetadataPath(assetPath), root);
        }

        private static JsonObject ToColorsJson(MetadataState state)
        {
            var colors = new JsonObject();
            foreach ((string regionKey, string color) in state.RegionColors)
            {
                colors[regionKey] = color;
            }

            return colors;
        }

        private static void ValidateRegionKeys(
            MetadataState state,
            CellsDocument document)
        {
            foreach (string regionKey in state.RegionColors.Keys)
            {
                if (!document.Regions.ContainsKey(regionKey))
                {
                    throw new InvalidOperationException(
                        $"Editor metadata references unknown region '{regionKey}'.");
                }
            }
        }

        private static string NormalizeColor(string color)
        {
            if (color.Length != 7 || color[0] != '#')
            {
                throw new InvalidOperationException(
                    $"Color '{color}' must use #RRGGBB form.");
            }

            for (int index = 1; index < color.Length; index++)
            {
                char value = color[index];
                if (!((value >= '0' && value <= '9') ||
                      (value >= 'a' && value <= 'f') ||
                      (value >= 'A' && value <= 'F')))
                {
                    throw new InvalidOperationException(
                        $"Color '{color}' must use #RRGGBB form.");
                }
            }

            return color.ToUpperInvariant();
        }

        private static void RequireOnlyProperties(
            JsonObject root,
            string path,
            params string[] allowed)
        {
            var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach ((string propertyName, _) in root)
            {
                if (!allowedNames.Contains(propertyName))
                {
                    throw new InvalidOperationException(
                        $"'{path}' contains unknown property '{propertyName}'.");
                }
            }
        }

        private sealed class MetadataState
        {
            public MetadataState(string layerKey)
            {
                LayerKey = layerKey;
            }

            public string LayerKey { get; }
            public SortedDictionary<string, string> RegionColors { get; } =
                new(StringComparer.Ordinal);
        }
    }

    internal static class EditorSidecarJson
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public static void WriteAtomic(string path, JsonObject root)
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
}
