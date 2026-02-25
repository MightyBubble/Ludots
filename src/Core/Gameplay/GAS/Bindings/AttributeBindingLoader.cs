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

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "Id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            var sorted = new List<(string Id, JsonObject Node)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
                sorted.Add((merged[i].Id, merged[i].Node));
            sorted.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            var compiled = new List<(int SinkId, int Order, AttributeBindingEntry Entry)>(sorted.Count);

            for (int i = 0; i < sorted.Count; i++)
            {
                var (id, obj) = sorted[i];
                var cfg = obj.Deserialize<AttributeBindingConfig>(options);
                if (cfg == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize attribute binding '{id}' from {relativePath}.");
                }

                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    cfg.Id = id;
                }

                if (!string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Attribute binding id mismatch in {relativePath}: '{id}' vs '{cfg.Id}'.");
                }

                if (string.IsNullOrWhiteSpace(cfg.Attribute))
                {
                    throw new InvalidOperationException($"Attribute binding '{id}' in {relativePath} missing attribute.");
                }
                if (string.IsNullOrWhiteSpace(cfg.Sink))
                {
                    throw new InvalidOperationException($"Attribute binding '{id}' in {relativePath} missing sink.");
                }

                int attributeId = AttributeRegistry.Register(cfg.Attribute);
                int sinkId = _sinks.GetId(cfg.Sink);
                if (sinkId < 0)
                {
                    throw new InvalidOperationException($"Attribute binding '{id}' in {relativePath}: unknown sink '{cfg.Sink}'.");
                }

                var mode = ParseMode(cfg.Mode, id, relativePath);
                var reset = ParseResetPolicy(cfg.ResetPolicy, id, relativePath);
                int channel = cfg.Channel;
                if (channel < 0 || channel > 255)
                {
                    throw new InvalidOperationException($"Attribute binding '{id}' in {relativePath}: channel out of range (0..255).");
                }

                compiled.Add((sinkId, i, new AttributeBindingEntry(attributeId, sinkId, (byte)channel, mode, reset, cfg.Scale)));
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
            if (string.IsNullOrWhiteSpace(mode)) return AttributeBindingMode.Add;
            if (string.Equals(mode, "Add", StringComparison.OrdinalIgnoreCase)) return AttributeBindingMode.Add;
            if (string.Equals(mode, "Override", StringComparison.OrdinalIgnoreCase)) return AttributeBindingMode.Override;
            throw new InvalidOperationException($"Attribute binding '{ownerId}' in {relativePath}: unsupported mode '{mode}'. Allowed: Add, Override.");
        }

        private static AttributeBindingResetPolicy ParseResetPolicy(string policy, string ownerId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(policy)) return AttributeBindingResetPolicy.None;
            if (string.Equals(policy, "None", StringComparison.OrdinalIgnoreCase)) return AttributeBindingResetPolicy.None;
            if (string.Equals(policy, "ResetToZeroPerLogicFrame", StringComparison.OrdinalIgnoreCase)) return AttributeBindingResetPolicy.ResetToZeroPerLogicFrame;
            throw new InvalidOperationException($"Attribute binding '{ownerId}' in {relativePath}: unsupported resetPolicy '{policy}'.");
        }
    }
}
