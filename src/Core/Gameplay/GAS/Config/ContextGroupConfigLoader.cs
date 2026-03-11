using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class ContextGroupConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly ContextGroupRegistry _registry;

        public ContextGroupConfigLoader(ConfigPipeline pipeline, ContextGroupRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/context_groups.json")
        {
            _registry.Clear();

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                int groupId = ContextGroupIdRegistry.Register(merged[i].Id);
                int rootAbilityId = ResolveAbilityId(node["rootAbilityId"]?.GetValue<string>(), merged[i].Id, "rootAbilityId");
                var definition = Compile(node, merged[i].Id);
                _registry.Register(groupId, rootAbilityId, in definition);
            }
        }

        public static ContextGroupDefinition Compile(JsonObject node, string groupName)
        {
            if (node["searchRadiusCm"] is not JsonNode searchNode)
            {
                throw new InvalidOperationException($"Context group '{groupName}' requires searchRadiusCm.");
            }

            int searchRadiusCm = searchNode.GetValue<int>();
            if (searchRadiusCm < 0)
            {
                throw new InvalidOperationException($"Context group '{groupName}' searchRadiusCm must be non-negative.");
            }

            if (node["candidates"] is not JsonArray candidatesNode || candidatesNode.Count == 0)
            {
                throw new InvalidOperationException($"Context group '{groupName}' requires at least one candidate.");
            }

            var candidates = new List<ContextGroupCandidate>(candidatesNode.Count);
            for (int i = 0; i < candidatesNode.Count; i++)
            {
                if (candidatesNode[i] is not JsonObject candidateNode)
                {
                    continue;
                }

                int abilityId = ResolveAbilityId(candidateNode["abilityId"]?.GetValue<string>(), groupName, $"candidates[{i}].abilityId");
                int preconditionGraphId = ResolveGraphId(candidateNode["preconditionGraph"]?.GetValue<string>());
                int scoreGraphId = ResolveGraphId(candidateNode["scoreGraph"]?.GetValue<string>());
                float basePriority = candidateNode["basePriority"]?.GetValue<float>() ?? 0f;
                int maxDistanceCm = candidateNode["maxDistanceCm"]?.GetValue<int>() ?? 0;
                float distanceWeight = candidateNode["distanceWeight"]?.GetValue<float>() ?? 0f;
                int maxAngleDeg = candidateNode["maxAngleDeg"]?.GetValue<int>() ?? 0;
                float angleWeight = candidateNode["angleWeight"]?.GetValue<float>() ?? 0f;
                float hoveredBiasScore = candidateNode["hoveredBiasScore"]?.GetValue<float>() ?? 0f;
                bool requiresTarget = candidateNode["requiresTarget"]?.GetValue<bool>() ?? true;

                candidates.Add(new ContextGroupCandidate(
                    abilityId,
                    preconditionGraphId,
                    scoreGraphId,
                    basePriority,
                    maxDistanceCm,
                    distanceWeight,
                    maxAngleDeg,
                    angleWeight,
                    hoveredBiasScore,
                    requiresTarget));
            }

            return new ContextGroupDefinition(searchRadiusCm, candidates.ToArray());
        }

        private static int ResolveAbilityId(string? abilityName, string groupName, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(abilityName))
            {
                throw new InvalidOperationException($"Context group '{groupName}' requires {fieldName}.");
            }

            int abilityId = AbilityIdRegistry.GetId(abilityName);
            if (abilityId <= 0)
            {
                throw new InvalidOperationException($"Context group '{groupName}' field '{fieldName}' references unknown ability '{abilityName}'.");
            }

            return abilityId;
        }

        private static int ResolveGraphId(string? graphName)
        {
            if (string.IsNullOrWhiteSpace(graphName))
            {
                return 0;
            }

            int graphId = GraphIdRegistry.GetId(graphName);
            if (graphId <= 0)
            {
                throw new InvalidOperationException($"Unknown graph '{graphName}'.");
            }

            return graphId;
        }
    }
}
