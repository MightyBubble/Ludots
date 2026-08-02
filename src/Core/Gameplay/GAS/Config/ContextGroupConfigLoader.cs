using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class ContextGroupConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly ContextGroupRegistry _registry;
        private readonly IReadOnlyGraphScorer? _graphScorer;

        public ContextGroupConfigLoader(
            ConfigPipeline pipeline,
            ContextGroupRegistry registry,
            IReadOnlyGraphScorer? graphScorer = null)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _graphScorer = graphScorer;
        }

        public void Load(
            ConfigCatalog catalog = default!,
            ConfigConflictReport report = default!,
            string relativePath = "GAS/context_groups.json")
        {
            _registry.Clear();
            ContextGroupIdRegistry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                int groupId = ContextGroupIdRegistry.Register(merged[i].Id);
                int rootAbilityId = ResolveAbilityId(node["rootAbilityId"]?.GetValue<string>(), merged[i].Id, "rootAbilityId");
                var definition = Compile(node, merged[i].Id, _graphScorer);
                _registry.Register(groupId, rootAbilityId, in definition);
            }

            ContextGroupIdRegistry.Freeze();
        }

        public static ContextGroupDefinition Compile(JsonObject node, string groupName)
            => Compile(node, groupName, graphScorer: null);

        private static ContextGroupDefinition Compile(
            JsonObject node,
            string groupName,
            IReadOnlyGraphScorer? graphScorer)
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
                    throw new InvalidOperationException($"Context group '{groupName}' candidates[{i}] must be an object.");
                }

                int abilityId = ResolveAbilityId(candidateNode["abilityId"]?.GetValue<string>(), groupName, $"candidates[{i}].abilityId");
                int preconditionGraphId = ResolveGraphId(candidateNode["preconditionGraph"]?.GetValue<string>());
                int scoreGraphId = ResolveGraphId(candidateNode["scoreGraph"]?.GetValue<string>());
                ValidateGraphReference(graphScorer, preconditionGraphId, isScoreGraph: false, groupName, i, "preconditionGraph");
                ValidateGraphReference(graphScorer, scoreGraphId, isScoreGraph: true, groupName, i, "scoreGraph");
                if (candidateNode["requiresTarget"] is not JsonNode requiresTargetNode)
                {
                    throw new InvalidOperationException(
                        $"Context group '{groupName}' requires candidates[{i}].requiresTarget.");
                }

                bool requiresTarget = requiresTargetNode.GetValue<bool>();
                float basePriority = RequireFloat(candidateNode, "basePriority", groupName, i);
                int maxDistanceCm = requiresTarget
                    ? RequireInt(candidateNode, "maxDistanceCm", groupName, i)
                    : ReadOptionalNonNegativeInt(candidateNode, "maxDistanceCm", groupName, i);
                float distanceWeight = requiresTarget
                    ? RequireFloat(candidateNode, "distanceWeight", groupName, i)
                    : ReadOptionalFloat(candidateNode, "distanceWeight", groupName, i);
                int maxAngleDeg = requiresTarget
                    ? RequireInt(candidateNode, "maxAngleDeg", groupName, i)
                    : ReadOptionalNonNegativeInt(candidateNode, "maxAngleDeg", groupName, i);
                float angleWeight = requiresTarget
                    ? RequireFloat(candidateNode, "angleWeight", groupName, i)
                    : ReadOptionalFloat(candidateNode, "angleWeight", groupName, i);
                float hoveredBiasScore = requiresTarget
                    ? RequireFloat(candidateNode, "hoveredBiasScore", groupName, i)
                    : ReadOptionalFloat(candidateNode, "hoveredBiasScore", groupName, i);

                if (maxDistanceCm < 0)
                {
                    throw new InvalidOperationException($"Context group '{groupName}' candidates[{i}].maxDistanceCm must be non-negative.");
                }

                if (maxAngleDeg < 0)
                {
                    throw new InvalidOperationException($"Context group '{groupName}' candidates[{i}].maxAngleDeg must be non-negative.");
                }

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

        private static void ValidateGraphReference(
            IReadOnlyGraphScorer? graphScorer,
            int graphId,
            bool isScoreGraph,
            string groupName,
            int candidateIndex,
            string fieldName)
        {
            if (graphId <= 0)
            {
                return;
            }

            string path = $"Context group '{groupName}' candidates[{candidateIndex}].{fieldName}";
            if (graphScorer == null)
            {
                throw new InvalidOperationException(
                    $"{path}: graph references require IReadOnlyGraphScorer.");
            }

            try
            {
                if (isScoreGraph)
                {
                    graphScorer.RequireScoreGraph(graphId, path);
                }
                else
                {
                    graphScorer.RequireValidationGraph(graphId, path);
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{path}: {ex.Message}", ex);
            }
        }

        private static float RequireFloat(JsonObject obj, string fieldName, string groupName, int candidateIndex)
        {
            if (obj[fieldName] is not JsonNode node)
            {
                throw new InvalidOperationException($"Context group '{groupName}' requires candidates[{candidateIndex}].{fieldName}.");
            }

            return node.GetValue<float>();
        }

        private static int RequireInt(JsonObject obj, string fieldName, string groupName, int candidateIndex)
        {
            if (obj[fieldName] is not JsonNode node)
            {
                throw new InvalidOperationException($"Context group '{groupName}' requires candidates[{candidateIndex}].{fieldName}.");
            }

            return node.GetValue<int>();
        }

        private static float ReadOptionalFloat(JsonObject obj, string fieldName, string groupName, int candidateIndex)
        {
            if (obj[fieldName] is not JsonNode node)
            {
                return 0f;
            }

            return node.GetValue<float>();
        }

        private static int ReadOptionalNonNegativeInt(JsonObject obj, string fieldName, string groupName, int candidateIndex)
        {
            if (obj[fieldName] is not JsonNode node)
            {
                return 0;
            }

            int value = node.GetValue<int>();
            if (value < 0)
            {
                throw new InvalidOperationException($"Context group '{groupName}' candidates[{candidateIndex}].{fieldName} must be non-negative.");
            }

            return value;
        }
    }
}
