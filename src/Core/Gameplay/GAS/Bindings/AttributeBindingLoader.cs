using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public sealed class AttributeBindingLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AttributeSinkRegistry _sinks;
        private readonly AttributeBindingRegistry _registry;

        public AttributeBindingLoader(ConfigPipeline pipeline, AttributeSinkRegistry sinks, AttributeBindingRegistry registry)
        {
            _pipeline = pipeline;
            _sinks = sinks;
            _registry = registry;
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/attribute_bindings.json")
        {
            _registry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var fragments = _pipeline.CollectFragmentsWithSources(entry.RelativePath);
            ValidateRawIds(fragments, entry.RelativePath);
            var merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);

            var sorted = new List<(string Id, JsonObject Node)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
                sorted.Add((merged[i].Id, merged[i].Node));
            sorted.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));

            var options = StrictJsonOptions.CreateCamelCase();
            var compiled = new List<(int SinkId, int Order, AttributeBindingEntry Entry)>(sorted.Count);

            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                var cfg = obj.Deserialize<AttributeBindingConfig>(options);
                if (cfg == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize attribute binding '{id}' from {relativePath}.");
                }

                if (!string.Equals(cfg.Id, id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Attribute binding id mismatch in {relativePath}: '{id}' vs '{cfg.Id}'.");
                }

                string attribute = RequireCanonicalField(obj, cfg.Attribute, "attribute", id, relativePath);
                string sink = RequireCanonicalField(obj, cfg.Sink, "sink", id, relativePath);
                string modeText = RequireCanonicalField(obj, cfg.Mode, "mode", id, relativePath);
                string resetPolicyText = RequireCanonicalField(obj, cfg.ResetPolicy, "resetPolicy", id, relativePath);
                int channel = RequireChannel(obj, cfg.Channel, id, relativePath);
                float scale = RequireScale(obj, cfg.Scale, id, relativePath);

                int attributeId = AttributeRegistry.Register(attribute);
                int sinkId = _sinks.GetId(sink);
                if (sinkId < 0)
                {
                    throw new InvalidOperationException($"Attribute binding '{id}' in {relativePath}: unknown sink '{sink}'.");
                }

                var mode = ParseMode(modeText, id, relativePath);
                var reset = ParseResetPolicy(resetPolicyText, id, relativePath);

                compiled.Add((sinkId, i, new AttributeBindingEntry(attributeId, sinkId, (byte)channel, mode, reset, scale)));
            }

            compiled.Sort((a, b) =>
            {
                int c = a.SinkId.CompareTo(b.SinkId);
                if (c != 0) return c;
                return a.Order.CompareTo(b.Order);
            });

            var entries = new AttributeBindingEntry[compiled.Count];
            for (int i = 0; i < compiled.Count; i++) entries[i] = compiled[i].Entry;

            var groups = BuildGroups(compiled);
            _registry.Set(entries, groups);
        }

        private static AttributeBindingGroup[] BuildGroups(List<(int SinkId, int Order, AttributeBindingEntry Entry)> compiled)
        {
            if (compiled.Count == 0) return Array.Empty<AttributeBindingGroup>();

            var groups = new List<AttributeBindingGroup>(16);
            int start = 0;
            int currentSink = compiled[0].SinkId;

            for (int i = 1; i < compiled.Count; i++)
            {
                int sink = compiled[i].SinkId;
                if (sink != currentSink)
                {
                    groups.Add(new AttributeBindingGroup(currentSink, start, i - start));
                    start = i;
                    currentSink = sink;
                }
            }

            groups.Add(new AttributeBindingGroup(currentSink, start, compiled.Count - start));
            return groups.ToArray();
        }

        private static AttributeBindingMode ParseMode(string mode, string ownerId, string relativePath)
        {
            return mode switch
            {
                "Add" => AttributeBindingMode.Add,
                "Override" => AttributeBindingMode.Override,
                _ => throw new InvalidOperationException($"Attribute binding '{ownerId}' in {relativePath}: unsupported mode '{mode}'. Allowed: Add, Override."),
            };
        }

        private static AttributeBindingResetPolicy ParseResetPolicy(string policy, string ownerId, string relativePath)
        {
            return policy switch
            {
                "None" => AttributeBindingResetPolicy.None,
                "ResetToZeroPerLogicFrame" => AttributeBindingResetPolicy.ResetToZeroPerLogicFrame,
                _ => throw new InvalidOperationException($"Attribute binding '{ownerId}' in {relativePath}: unsupported resetPolicy '{policy}'."),
            };
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

        private static string RequireCanonicalField(
            JsonObject obj,
            string value,
            string fieldName,
            string ownerId,
            string relativePath)
        {
            if (!obj.ContainsKey(fieldName))
            {
                throw new InvalidOperationException(
                    $"Attribute binding '{ownerId}' in {relativePath}: {fieldName} requires an explicit semantic string.");
            }

            return RequireCanonicalString(value, $"Attribute binding '{ownerId}' in {relativePath}: {fieldName}");
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

        private static int RequireChannel(JsonObject obj, int channel, string ownerId, string relativePath)
        {
            if (!obj.ContainsKey("channel"))
            {
                throw new InvalidOperationException(
                    $"Attribute binding '{ownerId}' in {relativePath}: channel requires an explicit integer field.");
            }

            if (channel < 0 || channel > 255)
            {
                throw new InvalidOperationException($"Attribute binding '{ownerId}' in {relativePath}: channel out of range (0..255).");
            }

            return channel;
        }

        private static float RequireScale(JsonObject obj, float scale, string ownerId, string relativePath)
        {
            if (!obj.ContainsKey("scale"))
            {
                throw new InvalidOperationException(
                    $"Attribute binding '{ownerId}' in {relativePath}: scale requires an explicit finite number.");
            }

            if (!float.IsFinite(scale))
            {
                throw new InvalidOperationException(
                    $"Attribute binding '{ownerId}' in {relativePath}: scale must be finite.");
            }

            return scale;
        }
    }
}
