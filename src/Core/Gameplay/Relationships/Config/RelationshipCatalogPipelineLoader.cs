using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    public sealed class RelationshipCatalogPipelineLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public RelationshipCatalogPipelineLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public RelationshipCatalogConfig Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "Relationships/catalog.json")
        {
            var fragments = _pipeline.CollectFragmentsWithSources(relativePath);
            if (report != null)
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    report.RecordFragment(relativePath, fragments[i].SourceUri);
                }
            }

            var types = new Dictionary<string, RelationshipTypeConfig>(StringComparer.OrdinalIgnoreCase);
            var typeOrder = new List<string>();
            var metrics = new Dictionary<string, RelationshipMetricConfig>(StringComparer.OrdinalIgnoreCase);
            var metricOrder = new List<string>();
            var flags = new Dictionary<string, RelationshipFlagConfig>(StringComparer.OrdinalIgnoreCase);
            var flagOrder = new List<string>();
            var bands = new Dictionary<string, RelationshipBandConfig>(StringComparer.OrdinalIgnoreCase);
            var bandOrder = new List<string>();
            var reasons = new Dictionary<string, RelationshipReasonConfig>(StringComparer.OrdinalIgnoreCase);
            var reasonOrder = new List<string>();
            var callbacks = new Dictionary<string, RelationshipCallbackConfig>(StringComparer.OrdinalIgnoreCase);
            var callbackOrder = new List<string>();
            var synergies = new Dictionary<string, RelationshipSynergyConfig>(StringComparer.OrdinalIgnoreCase);
            var synergyOrder = new List<string>();

            for (int i = 0; i < fragments.Count; i++)
            {
                RelationshipCatalogConfig? fragment = fragments[i].Node.Deserialize<RelationshipCatalogConfig>(_options);
                if (fragment == null)
                {
                    continue;
                }

                MergeById(fragment.Types, types, typeOrder, static item => item.Id);
                MergeById(fragment.Metrics, metrics, metricOrder, static item => item.Id);
                MergeById(fragment.Flags, flags, flagOrder, static item => item.Id);
                MergeById(fragment.Bands, bands, bandOrder, static item => item.Id);
                MergeById(fragment.Reasons, reasons, reasonOrder, static item => item.Id);
                MergeById(fragment.Callbacks, callbacks, callbackOrder, static item => item.Id);
                MergeById(fragment.Synergies, synergies, synergyOrder, static item => item.Id);
            }

            return new RelationshipCatalogConfig
            {
                Types = Materialize(typeOrder, types),
                Metrics = Materialize(metricOrder, metrics),
                Flags = Materialize(flagOrder, flags),
                Bands = Materialize(bandOrder, bands),
                Reasons = Materialize(reasonOrder, reasons),
                Callbacks = Materialize(callbackOrder, callbacks),
                Synergies = Materialize(synergyOrder, synergies),
            };
        }

        private static void MergeById<T>(
            List<T>? incoming,
            Dictionary<string, T> byId,
            List<string> order,
            Func<T, string> idSelector)
            where T : class
        {
            if (incoming == null)
            {
                return;
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                T? item = incoming[i];
                if (item == null)
                {
                    continue;
                }

                string id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!byId.ContainsKey(id))
                {
                    order.Add(id);
                }

                byId[id] = item;
            }
        }

        private static List<T> Materialize<T>(List<string> order, Dictionary<string, T> byId)
            where T : class
        {
            var result = new List<T>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                if (byId.TryGetValue(order[i], out T? value))
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }
}
