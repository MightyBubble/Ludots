using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class TagRuleSetLoader
    {
        private readonly ConfigPipeline _pipeline;

        public TagRuleSetLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public List<(int TagId, TagRuleSet RuleSet)> Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/tag_rules.json")
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

            var errors = new List<string>();
            var results = new List<(int TagId, TagRuleSet RuleSet)>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                try
                {
                    int tagId = TagRegistry.Register(merged[i].Id);
                    var ruleSet = CompileRuleSet(merged[i].Node, merged[i].Id, relativePath);
                    results.Add((tagId, ruleSet));
                }
                catch (Exception ex)
                {
                    errors.Add($"TagRuleSet '{merged[i].Id}': {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"[TagRuleSetLoader] {errors.Count} tag rule compilation error(s) in '{relativePath}'.",
                    errors.ConvertAll(e => (Exception)new InvalidOperationException(e)));
            }

            return results;
        }

        public static unsafe TagRuleSet CompileRuleSet(JsonObject obj, string id, string path)
        {
            var ruleSet = default(TagRuleSet);

            FillTagList(obj["requiredAll"], ref ruleSet.RequiredCount, ruleSet.RequiredTags, TagRuleSet.MAX_REQUIRED_TAGS, "requiredAll", id, path);
            FillTagList(obj["blockedAny"], ref ruleSet.BlockedCount, ruleSet.BlockedTags, TagRuleSet.MAX_BLOCKED_TAGS, "blockedAny", id, path);
            FillTagList(obj["attached"], ref ruleSet.AttachedCount, ruleSet.AttachedTags, TagRuleSet.MAX_ATTACHED_TAGS, "attached", id, path);
            FillTagList(obj["removed"], ref ruleSet.RemovedCount, ruleSet.RemovedTags, TagRuleSet.MAX_REMOVED_TAGS, "removed", id, path);
            FillTagList(obj["disabledIfAny"], ref ruleSet.DisabledIfCount, ruleSet.DisabledIfTags, TagRuleSet.MAX_DISABLED_IF_TAGS, "disabledIfAny", id, path);
            FillTagList(obj["removeIfAny"], ref ruleSet.RemoveIfCount, ruleSet.RemoveIfTags, TagRuleSet.MAX_REMOVE_IF_TAGS, "removeIfAny", id, path);

            return ruleSet;
        }

        private static unsafe void FillTagList(JsonNode node, ref int count, int* dst, int max, string field, string id, string path)
        {
            count = 0;
            if (node is null) return;
            if (node is not JsonArray arr)
            {
                throw new InvalidOperationException($"TagRuleSet '{id}' field '{field}' must be an array in '{path}'.");
            }

            for (int i = 0; i < arr.Count; i++)
            {
                if (count >= max)
                {
                    throw new InvalidOperationException($"TagRuleSet '{id}' field '{field}' exceeded max {max} in '{path}'.");
                }

                string tag = arr[i]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                int tagId = TagRegistry.Register(tag);
                if (tagId <= 0) throw new InvalidOperationException($"TagRuleSet '{id}' field '{field}' contains invalid tag '{tag}' in '{path}'.");
                dst[count++] = tagId;
            }
        }
    }
}
