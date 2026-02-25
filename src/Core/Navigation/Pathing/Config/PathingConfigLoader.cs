using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public sealed class PathingConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public PathingConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public PathingConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Navigation/pathing.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null) throw new InvalidOperationException("PathingConfig not found in any source.");

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            opts.Converters.Add(new JsonStringEnumConverter());
            var cfg = mergedObject.Deserialize<PathingConfig>(opts);
            if (cfg == null) throw new InvalidOperationException("Failed to deserialize PathingConfig.");
            Validate(cfg);
            return cfg;
        }

        private static void Validate(PathingConfig cfg)
        {
            if (cfg.AgentTypes == null || cfg.AgentTypes.Count == 0) throw new InvalidOperationException("PathingConfig.agentTypes is empty.");
            for (int i = 0; i < cfg.AgentTypes.Count; i++)
            {
                var a = cfg.AgentTypes[i];
                if (a == null) throw new InvalidOperationException("PathingConfig.agentTypes contains null.");
                if (string.IsNullOrWhiteSpace(a.Id)) throw new InvalidOperationException("PathingConfig.agentTypes.id is required.");
                if (string.IsNullOrWhiteSpace(a.ProfileId)) throw new InvalidOperationException($"PathingConfig.agentTypes[{a.Id}].profileId is required.");
                if (a.NavMesh == null) a.NavMesh = new PathingNavMeshConfig();
                if (a.NodeGraph == null) a.NodeGraph = new PathingNodeGraphConfig();
                if (a.Selection == null) a.Selection = new PathingSelectionConfig();
                if (a.NavMesh.AreaCosts == null) a.NavMesh.AreaCosts = new();
                if (a.NodeGraph.RequiredTagsAll == null) a.NodeGraph.RequiredTagsAll = new();
                if (a.NodeGraph.ForbiddenTagsAny == null) a.NodeGraph.ForbiddenTagsAny = new();
                if (a.NodeGraph.TagCostRules == null) a.NodeGraph.TagCostRules = new();
            }
        }
    }
}
