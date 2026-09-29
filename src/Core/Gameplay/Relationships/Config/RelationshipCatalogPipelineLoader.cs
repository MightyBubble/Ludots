using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    public sealed class RelationshipCatalogPipelineLoader
    {
        private readonly ConfigPipeline _pipeline;

        public static JsonSerializerOptions SerializerOptions { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
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
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            // knowledgeGrants 是迷雾的投影配置，物理上住在
            // Relationships/projection.json（catalog 声明同路径，DeepObject 合并）；
            // catalog.json 只承载关系词汇。
            var projectionEntry = new ConfigCatalogEntry("Relationships/projection.json", ConfigMergePolicy.DeepObject);
            var projectionFragments = _pipeline.CollectFragmentsWithSources(in projectionEntry);
            var fragments = _pipeline.CollectFragmentsWithSources(in entry);
            if (report != null)
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    report.RecordFragment(entry.RelativePath, fragments[i].SourceUri);
                }
            }

            var types = new Dictionary<string, RelationshipTypeConfig>(StringComparer.OrdinalIgnoreCase);
            var typeOrder = new List<string>();
            var metrics = new Dictionary<string, RelationshipMetricConfig>(StringComparer.OrdinalIgnoreCase);
            var metricOrder = new List<string>();
            var flags = new Dictionary<string, RelationshipFlagConfig>(StringComparer.OrdinalIgnoreCase);
            var flagOrder = new List<string>();
            var knowledgeGrants = new Dictionary<string, RelationshipKnowledgeGrantConfig>(StringComparer.OrdinalIgnoreCase);
            var knowledgeGrantOrder = new List<string>();

            for (int i = 0; i < fragments.Count; i++)
            {
                RelationshipCatalogConfig? fragment = fragments[i].Node.Deserialize<RelationshipCatalogConfig>(SerializerOptions);
                if (fragment == null)
                {
                    continue;
                }

                if (fragment.KnowledgeGrants.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Relationship catalog fragment '{fragments[i].SourceUri}' declares knowledgeGrants; they belong in Relationships/projection.json.");
                }

                string source = fragments[i].SourceUri;
                MergeById(fragment.Types, types, typeOrder, static item => item.Id, source, "types");
                MergeById(fragment.Metrics, metrics, metricOrder, static item => item.Id, source, "metrics");
                MergeById(fragment.Flags, flags, flagOrder, static item => item.Id, source, "flags");
            }

            for (int i = 0; i < projectionFragments.Count; i++)
            {
                RelationshipCatalogConfig? fragment = projectionFragments[i].Node.Deserialize<RelationshipCatalogConfig>(SerializerOptions);
                if (fragment == null)
                {
                    continue;
                }

                if (fragment.Types.Count > 0 || fragment.Metrics.Count > 0 || fragment.Flags.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Relationship projection fragment '{projectionFragments[i].SourceUri}' declares relationship vocabulary; types, metrics and flags belong in Relationships/catalog.json.");
                }

                MergeById(fragment.KnowledgeGrants, knowledgeGrants, knowledgeGrantOrder, static item => item.Id, projectionFragments[i].SourceUri, "knowledgeGrants");
            }

            return new RelationshipCatalogConfig
            {
                Types = Materialize(typeOrder, types),
                Metrics = Materialize(metricOrder, metrics),
                Flags = Materialize(flagOrder, flags),
                KnowledgeGrants = Materialize(knowledgeGrantOrder, knowledgeGrants),
            };
        }

        private static void MergeById<T>(
            List<T>? incoming,
            Dictionary<string, T> byId,
            List<string> order,
            Func<T, string> idSelector,
            string source,
            string section)
            where T : class
        {
            if (incoming == null)
            {
                return;
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                T item = incoming[i]
                    ?? throw new InvalidOperationException($"Relationship catalog fragment '{source}' {section}[{i}] is null.");
                string id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException($"Relationship catalog fragment '{source}' {section}[{i}] requires an id.");
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
