using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class AbilityFormSetConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AbilityFormSetRegistry _registry;

        public AbilityFormSetConfigLoader(ConfigPipeline pipeline, AbilityFormSetRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/ability_form_sets.json")
        {
            _registry.Clear();
            AbilityFormSetIdRegistry.Clear();

            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                int formSetId = AbilityFormSetIdRegistry.Register(merged[i].Id);
                var definition = Compile(merged[i].Node, merged[i].Id);
                _registry.Register(formSetId, in definition);
            }
        }

        public static AbilityFormSetDefinition Compile(JsonObject node, string formSetName)
        {
            if (node["routes"] is not JsonArray routesNode || routesNode.Count == 0)
            {
                throw new InvalidOperationException($"Ability form set '{formSetName}' requires at least one route.");
            }

            var routes = new List<AbilityFormRouteDefinition>(routesNode.Count);
            for (int routeIndex = 0; routeIndex < routesNode.Count; routeIndex++)
            {
                if (routesNode[routeIndex] is not JsonObject routeNode)
                {
                    continue;
                }

                var requiredAll = CompileTagMask(routeNode["requiredAll"] as JsonArray);
                var blockedAny = CompileTagMask(routeNode["blockedAny"] as JsonArray);
                int priority = routeNode["priority"]?.GetValue<int>() ?? 0;

                if (routeNode["slotOverrides"] is not JsonArray slotOverridesNode || slotOverridesNode.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] requires at least one slotOverrides entry.");
                }

                var slotOverrides = CompileSlotOverrides(slotOverridesNode, formSetName, routeIndex);
                routes.Add(new AbilityFormRouteDefinition(requiredAll, blockedAny, priority, slotOverrides));
            }

            return new AbilityFormSetDefinition(routes.ToArray());
        }

        private static GameplayTagContainer CompileTagMask(JsonArray? tagsNode)
        {
            var tags = default(GameplayTagContainer);
            if (tagsNode == null)
            {
                return tags;
            }

            for (int i = 0; i < tagsNode.Count; i++)
            {
                string? tagName = tagsNode[i]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                tags.AddTag(TagRegistry.Register(tagName));
            }

            return tags;
        }

        private static AbilityFormSlotOverride[] CompileSlotOverrides(JsonArray slotOverridesNode, string formSetName, int routeIndex)
        {
            var overrides = new List<AbilityFormSlotOverride>(slotOverridesNode.Count);
            var occupied = new bool[AbilityStateBuffer.CAPACITY];

            for (int slotOverrideIndex = 0; slotOverrideIndex < slotOverridesNode.Count; slotOverrideIndex++)
            {
                if (slotOverridesNode[slotOverrideIndex] is not JsonObject slotOverrideNode)
                {
                    continue;
                }

                if (slotOverrideNode["slotIndex"] is not JsonNode slotIndexNode)
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] slotOverrides[{slotOverrideIndex}] requires slotIndex.");
                }

                int slotIndex = slotIndexNode.GetValue<int>();
                if ((uint)slotIndex >= AbilityStateBuffer.CAPACITY)
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] slotOverrides[{slotOverrideIndex}] slotIndex {slotIndex} is out of range 0..{AbilityStateBuffer.CAPACITY - 1}.");
                }

                if (occupied[slotIndex])
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] defines duplicate slotIndex {slotIndex}.");
                }

                string abilityName = slotOverrideNode["abilityId"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(abilityName))
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] slotOverrides[{slotOverrideIndex}] requires abilityId.");
                }

                int abilityId = AbilityIdRegistry.GetId(abilityName);
                if (abilityId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Ability form set '{formSetName}' route[{routeIndex}] slotOverrides[{slotOverrideIndex}] references unknown ability '{abilityName}'.");
                }

                occupied[slotIndex] = true;
                overrides.Add(new AbilityFormSlotOverride(slotIndex, abilityId));
            }

            return overrides.ToArray();
        }
    }
}
