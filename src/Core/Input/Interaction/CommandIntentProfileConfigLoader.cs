using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/command_intent_profiles.json</c> (RFC-0065 INT-1). Follows the
    /// <c>FilterProfileConfigLoader</c> mounting pattern: catalog-declared DeepObject merge through
    /// the shared <see cref="ConfigPipeline"/>, structural validation fails fast. Duplicate rule
    /// priorities within a profile are rejected here (DEC-14 explicit total order); tag/stance/order
    /// key resolution happens at registry install.
    /// </summary>
    public sealed class CommandIntentProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public CommandIntentProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged command intent profile config from the pipeline.</summary>
        public CommandIntentProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/command_intent_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            RejectRenamedAuthoringFields(mergedObject, relativePath);

            var config = mergedObject.Deserialize<CommandIntentProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        private static void RejectRenamedAuthoringFields(JsonObject root, string relativePath)
        {
            if (root["profiles"] is not JsonArray profiles)
            {
                return;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] is not JsonObject profile)
                {
                    continue;
                }

                if (profile["rules"] is not JsonArray rules)
                {
                    continue;
                }

                for (int r = 0; r < rules.Count; r++)
                {
                    if (rules[r] is not JsonObject rule)
                    {
                        continue;
                    }

                    string rulePath = $"{relativePath}.profiles[{i}].rules[{r}]";
                    if (rule["actor"] is JsonObject actor && actor.ContainsKey("hasAbilityWithTag"))
                    {
                        throw new InvalidOperationException(
                            $"{rulePath}.actor field 'hasAbilityWithTag' was renamed to 'hasAbilityWithCategory' (ability classification, not gameplay tags).");
                    }

                    if (rule["route"] is JsonObject route &&
                        route["slot"] is JsonValue slotValue &&
                        slotValue.TryGetValue(out string slot) &&
                        slot.StartsWith("byAbilityTag:", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{rulePath}.route.slot '{slot}' uses removed prefix 'byAbilityTag:'; rename to 'byAbilityCategory:'.");
                    }
                }
            }
        }
        /// <summary>Structural fail-fast validation; id resolution happens at registry install.</summary>
        public static void Validate(CommandIntentProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Command intent config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                CommandIntentProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Command intent config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates command intent profile '{profile.Id}'.");
                }

                CommandIntentGroupPolicyDefinition groupPolicy = profile.GroupPolicy
                    ?? throw new InvalidOperationException($"{path}.groupPolicy must be an object.");
                RequireTrimmedNonEmpty(groupPolicy.Kind, $"{path}.groupPolicy.kind");

                if (profile.Rules == null || profile.Rules.Count == 0)
                {
                    throw new InvalidOperationException($"{path}.rules must declare at least one rule.");
                }

                var priorities = new HashSet<int>();
                for (int r = 0; r < profile.Rules.Count; r++)
                {
                    CommandIntentRuleDefinition rule = profile.Rules[r]
                        ?? throw new InvalidOperationException($"{path}.rules[{r}] must be an object.");
                    string rulePath = $"{path}.rules[{r}]";
                    if (!priorities.Add(rule.Priority))
                    {
                        // DEC-14: intra-profile priorities form an explicit total order.
                        throw new InvalidOperationException(
                            $"{rulePath}.priority {rule.Priority} duplicates another rule in profile '{profile.Id}'.");
                    }

                    CommandIntentRouteDefinition route = rule.Route
                        ?? throw new InvalidOperationException($"{rulePath}.route must be an object.");
                    RequireTrimmedNonEmpty(route.OrderTypeKey, $"{rulePath}.route.orderTypeKey");

                    ValidatePredicateStrings(rule.Actor?.AllTags, $"{rulePath}.actor.allTags");
                    ValidatePredicateStrings(rule.Actor?.AnyTags, $"{rulePath}.actor.anyTags");
                    ValidatePredicateStrings(rule.Target?.AllTags, $"{rulePath}.target.allTags");
                    ValidatePredicateStrings(rule.Target?.AnyTags, $"{rulePath}.target.anyTags");
                    ValidatePredicateStrings(rule.Target?.Stance, $"{rulePath}.target.stance");
                }
            }
        }

        private static void ValidatePredicateStrings(List<string> values, string path)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                RequireTrimmedNonEmpty(values[i], $"{path}[{i}]");
            }
        }

        private static void RequireTrimmedNonEmpty(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }
    }
}
