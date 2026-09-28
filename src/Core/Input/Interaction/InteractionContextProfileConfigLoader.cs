using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/interaction_context_profiles.json</c> (RFC-0065 CTX-6, §5.3). Catalog-
    /// declared DeepObject merge through the shared <see cref="ConfigPipeline"/> (a mod fragment's
    /// profiles array replaces the root's, matching the filter profile family); structural
    /// validation fails fast. Referenced filter/intent ids resolve at registry install, not here.
    /// The engine-reserved steady-state profile installs programmatically in GameEngine, not here.
    /// </summary>
    public sealed class InteractionContextProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public InteractionContextProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged interaction context profile config from the pipeline.</summary>
        public InteractionContextProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/interaction_context_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            RejectRetiredProfileFields(mergedObject, relativePath);

            var config = mergedObject.Deserialize<InteractionContextProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>
        /// Retired and undeclared profile fields fail closed. The deserializer ignores unknown
        /// properties, so a collection field invented beside <c>activeCollectionKey</c> would
        /// otherwise sit in the file and never reach the entity.
        /// </summary>
        public static void RejectRetiredProfileFields(JsonObject root, string relativePath)
        {
            if (!root.TryGetPropertyValue("profiles", out JsonNode? profilesNode) ||
                profilesNode is not JsonArray profiles)
            {
                return;
            }

            for (int index = 0; index < profiles.Count; index++)
            {
                if (profiles[index] is not JsonObject profile)
                {
                    continue;
                }

                foreach (KeyValuePair<string, JsonNode?> property in profile)
                {
                    if (string.Equals(property.Key, "continuousQuery", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath}.profiles[{index}] declares retired field '{property.Key}'; use onActivated/onDeactivated graph slots.");
                    }

                    if (string.Equals(property.Key, "whileActive", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath}.profiles[{index}] declares retired field '{property.Key}'; use onActivated/onDeactivated graph slots (whileActive was a per-tick period field; the slots are instant window-boundary hooks).");
                    }

                    if (string.Equals(property.Key, "activeEntityViewKey", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath}.profiles[{index}] declares retired field '{property.Key}'; it has no runtime consumer.");
                    }

                    if (IsArbitraryCollectionField(property.Key))
                    {
                        throw new InvalidOperationException(
                            $"{relativePath}.profiles[{index}] declares collection field '{property.Key}'. " +
                            "Collection keys are declared as collectionKey on graph nodes. The only profile field is activeCollectionKey, " +
                            "and a context whose submit graph declares collectionKey must omit it — that key is persisted on the entity interaction instance.");
                    }
                }
            }
        }

        /// <summary>Structural fail-fast validation; id resolution happens at profile registry install time.</summary>
        public static void Validate(InteractionContextProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Interaction context config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InteractionContextProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Interaction context config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates interaction context profile '{profile.Id}'.");
                }

                // activeCollectionKey is optional. A context whose submit graphs declare
                // collectionKey must omit it; install copies that graph key onto the entity.
                RequireTrimmedWhenPresent(profile.ActiveCollectionKey, $"{path}.activeCollectionKey");
                RequireTrimmedWhenPresent(profile.FilterProfileId, $"{path}.filterProfileId");
                RequireTrimmedWhenPresent(profile.InputContextId, $"{path}.inputContextId");
                RequireTrimmedWhenPresent(profile.CommandIntentId, $"{path}.commandIntentId");
                ValidateBindings(profile.Bindings, path);
                ValidateTriggers(profile.Triggers, path);
                ValidateLifecycleGraphSlots(profile.OnActivated, $"{path}.onActivated");
                ValidateLifecycleGraphSlots(profile.OnDeactivated, $"{path}.onDeactivated");
            }
        }

        private static void ValidateBindings(List<string>? bindings, string path)
        {
            if (bindings == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                string binding = bindings[i]
                    ?? throw new InvalidOperationException($"{path}.bindings[{i}] must be a string.");
                RequireTrimmedNonEmpty(binding, $"{path}.bindings[{i}]");
                if (!seen.Add(binding))
                {
                    throw new InvalidOperationException($"{path}.bindings[{i}] duplicates semantic action '{binding}'.");
                }
            }
        }

        private static void ValidateTriggers(List<InteractionContextTriggerMount>? triggers, string path)
        {
            if (triggers == null)
            {
                return;
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                InteractionContextTriggerMount mount = triggers[i]
                    ?? throw new InvalidOperationException($"{path}.triggers[{i}] must be an object.");
                string mountPath = $"{path}.triggers[{i}]";
                RequireTrimmedNonEmpty(mount.Trigger, $"{mountPath}.trigger");
                RequireTrimmedWhenPresent(mount.Event, $"{mountPath}.event");
                if (mount.Filters?.InstanceId != null)
                {
                    RequireTrimmedWhenPresent(mount.Filters.InstanceId, $"{mountPath}.filters.instanceId");
                }
            }
        }

        private static void ValidateLifecycleGraphSlots(List<string>? graphs, string path)
        {
            if (graphs == null)
            {
                return;
            }

            for (int i = 0; i < graphs.Count; i++)
            {
                string graph = graphs[i]
                    ?? throw new InvalidOperationException($"{path}[{i}] must be a string.");
                RequireTrimmedNonEmpty(graph, $"{path}[{i}]");
            }
        }

        private static bool IsArbitraryCollectionField(string propertyName)
        {
            if (string.Equals(propertyName, "activeCollectionKey", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return propertyName.Contains("collection", StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireTrimmedWhenPresent(string value, string path)
        {
            // Omitted JSON fields deserialize to "" via property defaults; treat blank as absent.
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
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
