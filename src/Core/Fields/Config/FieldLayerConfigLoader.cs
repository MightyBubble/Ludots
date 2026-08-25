using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Fields.Config
{
    public sealed class FieldLayerConfigLoader
    {
        public const string DefaultRelativePath = "Fields/layers.json";

        private readonly ConfigPipeline _pipeline;
        private readonly FieldLayerRegistry _registry;

        public FieldLayerConfigLoader(ConfigPipeline pipeline, FieldLayerRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = DefaultRelativePath)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = StrictJsonOptions.CreateCamelCase();

            for (int i = 0; i < merged.Count; i++)
            {
                MergedConfigEntry item = merged[i];
                FieldLayerConfig cfg;
                try
                {
                    cfg = item.Node.Deserialize<FieldLayerConfig>(options)
                        ?? throw new InvalidOperationException($"Failed to deserialize field layer '{item.Id}' from {relativePath}.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Field layer '{item.Id}' in {relativePath}: {ex.Message}", ex);
                }

                if (!string.Equals(cfg.Id, item.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Field layer id mismatch in {relativePath}: field 'id' merged as '{item.Id}' but payload contains '{cfg.Id}'.");
                }

                string id = RequireCanonicalId(cfg.Id, relativePath);
                FieldLayerKind kind = RequireKind(cfg.Kind, id, relativePath);
                int cellSizeCm = RequirePositive(cfg.CellSizeCm, id, relativePath, "cellSizeCm");
                int chunkSizeCells = RequirePowerOfTwo(cfg.ChunkSizeCells, id, relativePath);
                FieldLayerDefaultValue defaultValue = RequireDefault(cfg.Default, kind, id, relativePath);
                bool persistent = cfg.Persistent ?? true;
                string writerDomain = RequireNonEmpty(cfg.WriterDomain, id, relativePath, "writerDomain");
                int maxRegionIds = RequireMaxRegionIds(cfg.MaxRegionIds, kind, id, relativePath);

                _registry.Register(id, kind, cellSizeCm, chunkSizeCells, defaultValue, persistent, writerDomain, maxRegionIds);
            }

            _registry.Freeze();
        }

        private static string RequireCanonicalId(string value, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{relativePath}: field layer id is required.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{relativePath}: field layer id '{value}' must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static FieldLayerKind RequireKind(string value, string id, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field 'kind' is required.");
            }

            if (string.Equals(value, "discreteId", StringComparison.OrdinalIgnoreCase))
            {
                return FieldLayerKind.DiscreteId;
            }

            if (string.Equals(value, "scalar32", StringComparison.OrdinalIgnoreCase))
            {
                return FieldLayerKind.Scalar32;
            }

            if (string.Equals(value, "vector2", StringComparison.OrdinalIgnoreCase))
            {
                return FieldLayerKind.Vector2;
            }

            if (string.Equals(value, "vector3", StringComparison.OrdinalIgnoreCase))
            {
                return FieldLayerKind.Vector3;
            }

            throw new InvalidOperationException(
                $"Field layer '{id}' in {relativePath}: field 'kind' value '{value}' is not supported; expected discreteId, scalar32, vector2 or vector3.");
        }

        private static int RequirePositive(int? value, string id, string relativePath, string fieldPath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field '{fieldPath}' is required.");
            }

            if (value.Value <= 0)
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field '{fieldPath}' must be > 0.");
            }

            return value.Value;
        }

        private static int RequirePowerOfTwo(int? value, string id, string relativePath)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field 'chunkSizeCells' is required.");
            }

            int resolved = value.Value;
            if (resolved <= 0 || (resolved & (resolved - 1)) != 0)
            {
                throw new InvalidOperationException(
                    $"Field layer '{id}' in {relativePath}: field 'chunkSizeCells' must be a positive power of two.");
            }

            return resolved;
        }

        private static string RequireNonEmpty(string? value, string id, string relativePath, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field '{fieldPath}' is required.");
            }

            return value!;
        }

        private static int RequireMaxRegionIds(int? value, FieldLayerKind kind, string id, string relativePath)
        {
            if (kind != FieldLayerKind.DiscreteId)
            {
                if (value.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Field layer '{id}' in {relativePath}: field 'maxRegionIds' is only valid for kind 'discreteId'.");
                }

                return 0;
            }

            int resolved = value ?? 256;
            if (resolved <= 0)
            {
                throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: field 'maxRegionIds' must be > 0.");
            }

            return resolved;
        }

        private static FieldLayerDefaultValue RequireDefault(JsonNode? node, FieldLayerKind kind, string id, string relativePath)
        {
            if (node == null)
            {
                return kind switch
                {
                    FieldLayerKind.Scalar32 => FieldLayerDefaultValue.Scalar32(0f),
                    FieldLayerKind.Vector2 => FieldLayerDefaultValue.Vector2Value(default),
                    FieldLayerKind.Vector3 => FieldLayerDefaultValue.Vector3Value(default),
                    _ => FieldLayerDefaultValue.None,
                };
            }

            switch (kind)
            {
                case FieldLayerKind.Scalar32:
                    if (!TryReadNumber(node, out double scalar))
                    {
                        throw new InvalidOperationException(
                            $"Field layer '{id}' in {relativePath}: field 'default' for kind 'scalar32' must be a number.");
                    }

                    return FieldLayerDefaultValue.Scalar32((float)scalar);

                case FieldLayerKind.Vector2:
                case FieldLayerKind.Vector3:
                    return RequireVectorDefault(node, kind, id, relativePath);

                case FieldLayerKind.DiscreteId:
                    if (TryReadNumber(node, out double zero) && zero == 0d)
                    {
                        return FieldLayerDefaultValue.None;
                    }

                    throw new InvalidOperationException(
                        $"Field layer '{id}' in {relativePath}: field 'default' for kind 'discreteId' must be omitted or the number 0; region key strings are not allowed.");

                default:
                    throw new InvalidOperationException($"Field layer '{id}' in {relativePath}: unsupported kind.");
            }
        }

        private static FieldLayerDefaultValue RequireVectorDefault(JsonNode node, FieldLayerKind kind, string id, string relativePath)
        {
            string kindName = kind == FieldLayerKind.Vector2 ? "vector2" : "vector3";
            if (node is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Field layer '{id}' in {relativePath}: field 'default' for kind '{kindName}' must be an array.");
            }

            int expected = kind == FieldLayerKind.Vector2 ? 2 : 3;
            if (array.Count != expected)
            {
                throw new InvalidOperationException(
                    $"Field layer '{id}' in {relativePath}: field 'default' for kind '{kindName}' must have exactly {expected} elements.");
            }

            var components = new double[expected];
            for (int i = 0; i < expected; i++)
            {
                if (array[i] == null || !TryReadNumber(array[i]!, out components[i]))
                {
                    throw new InvalidOperationException(
                        $"Field layer '{id}' in {relativePath}: field 'default' element [{i}] must be a number.");
                }
            }

            if (kind == FieldLayerKind.Vector2)
            {
                return FieldLayerDefaultValue.Vector2Value(new Vector2((float)components[0], (float)components[1]));
            }

            return FieldLayerDefaultValue.Vector3Value(new Vector3((float)components[0], (float)components[1], (float)components[2]));
        }

        private static bool TryReadNumber(JsonNode node, out double value)
        {
            if (node is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.Number)
            {
                if (jsonValue.TryGetValue<double>(out value))
                {
                    return true;
                }

                if (jsonValue.TryGetValue<int>(out int integer))
                {
                    value = integer;
                    return true;
                }
            }

            value = 0d;
            return false;
        }
    }
}
