using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Attributes
{
    public sealed class InputActionAttributeBindingLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly InputActionAttributeBindingRegistry _registry;
        private readonly JsonSerializerOptions _options = CreateOptions();

        public InputActionAttributeBindingLoader(
            ConfigPipeline pipeline,
            InputActionAttributeBindingRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/action_attribute_bindings.json")
        {
            _registry.Clear();

            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<ConfigFragment> fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            ValidateRawIds(fragments, entry.RelativePath);
            List<MergedConfigEntry> merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            var sorted = new List<(string Id, JsonObject Node)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                sorted.Add((merged[i].Id, merged[i].Node));
            }

            sorted.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));

            var entries = new InputActionAttributeBindingEntry[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                var cfg = obj.Deserialize<InputActionAttributeBindingConfig>(_options);
                if (cfg == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize input action attribute binding '{id}' from {relativePath}.");
                }

                if (!string.Equals(cfg.Id, id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Input action attribute binding id mismatch in {relativePath}: '{id}' vs '{cfg.Id}'.");
                }

                string action = RequireCanonicalField(obj, cfg.Action, "action", id, relativePath);
                string attribute = RequireCanonicalField(obj, cfg.Attribute, "attribute", id, relativePath);
                RequireExplicit(obj, "valueKind", id, relativePath);
                RequireExplicit(obj, "sourceChannel", id, relativePath);
                RequireExplicit(obj, "target", id, relativePath);
                RequireExplicit(obj, "scale", id, relativePath);
                RequireExplicit(obj, "zeroWhenUiCaptured", id, relativePath);
                RequireExplicit(obj, "suppressOnUiWheelCaptured", id, relativePath);
                RequireExplicit(obj, "preserveValueUntilSnapshot", id, relativePath);

                ValidateDefinedEnum(id, relativePath, nameof(cfg.ValueKind), cfg.ValueKind);
                ValidateDefinedEnum(id, relativePath, nameof(cfg.Target), cfg.Target);
                ValidateSourceChannel(id, relativePath, cfg.ValueKind, cfg.SourceChannel);
                if (!float.IsFinite(cfg.Scale))
                {
                    throw new InvalidOperationException(
                        $"Input action attribute binding '{id}' in {relativePath}: scale must be finite.");
                }

                entries[i] = new InputActionAttributeBindingEntry(
                    action,
                    AttributeRegistry.Register(attribute),
                    cfg.ValueKind,
                    (byte)cfg.SourceChannel,
                    cfg.Target,
                    cfg.Scale,
                    cfg.ZeroWhenUiCaptured,
                    cfg.SuppressOnUiWheelCaptured,
                    cfg.PreserveValueUntilSnapshot);
            }

            _registry.Set(entries);
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }

        private static void ValidateRawIds(IReadOnlyList<ConfigFragment> fragments, string relativePath)
        {
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                if (fragments[fragmentIndex].Node is not JsonArray arr)
                {
                    continue;
                }

                for (int entryIndex = 0; entryIndex < arr.Count; entryIndex++)
                {
                    if (arr[entryIndex] is not JsonObject obj)
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} entry at index {entryIndex} must be a JSON object.");
                    }

                    if (!obj.TryGetPropertyValue("id", out JsonNode? idNode) ||
                        idNode is not JsonValue idValue ||
                        !idValue.TryGetValue<string>(out string? id))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath} entry at index {entryIndex} must declare exact string field 'id'.");
                    }

                    RequireCanonicalString(id, $"{relativePath} entry id");
                }
            }
        }

        private static void RequireExplicit(JsonObject obj, string fieldName, string ownerId, string relativePath)
        {
            if (!obj.ContainsKey(fieldName))
            {
                throw new InvalidOperationException(
                    $"Input action attribute binding '{ownerId}' in {relativePath}: {fieldName} must be explicit.");
            }
        }

        private static string RequireCanonicalField(
            JsonObject obj,
            string value,
            string fieldName,
            string ownerId,
            string relativePath)
        {
            RequireExplicit(obj, fieldName, ownerId, relativePath);
            return RequireCanonicalString(
                value,
                $"Input action attribute binding '{ownerId}' in {relativePath}: {fieldName}");
        }

        private static string RequireCanonicalString(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} must be a non-empty semantic string.");
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context} must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static void ValidateSourceChannel(
            string ownerId,
            string relativePath,
            InputActionAttributeValueKind kind,
            int sourceChannel)
        {
            int max = kind == InputActionAttributeValueKind.Axis2D ? 1 : 0;
            if (sourceChannel < 0 || sourceChannel > max)
            {
                throw new InvalidOperationException(
                    $"Input action attribute binding '{ownerId}' in {relativePath}: sourceChannel must be 0..{max} for {kind}.");
            }
        }

        private static void ValidateDefinedEnum<TEnum>(string ownerId, string relativePath, string propertyName, TEnum value)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                throw new InvalidOperationException(
                    $"Input action attribute binding '{ownerId}' in {relativePath}: {propertyName} declares unsupported value '{value}'.");
            }
        }

        private sealed class InputActionAttributeBindingConfig
        {
            public string Id { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string Attribute { get; set; } = string.Empty;
            public InputActionAttributeValueKind ValueKind { get; set; }
            public int SourceChannel { get; set; }
            public InputActionAttributeTargetKind Target { get; set; }
            public float Scale { get; set; }
            public bool ZeroWhenUiCaptured { get; set; }
            public bool SuppressOnUiWheelCaptured { get; set; }
            public bool PreserveValueUntilSnapshot { get; set; }
        }
    }
}
