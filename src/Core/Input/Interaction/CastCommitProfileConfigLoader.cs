using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/cast_commit_profiles.json</c> (RFC-0065 CTX-7, §5.5). Follows the
    /// <c>FilterProfileConfigLoader</c> mounting pattern: catalog-declared DeepObject merge through
    /// the shared <see cref="ConfigPipeline"/>. DEC-13 guard: the profile schema is a closed key
    /// whitelist — any unrecognized key (in particular FSM state-table keys) fails the load, so no
    /// state-machine schema can ever ride in on this file. Op kinds and payload value sources
    /// resolve at registry install.
    /// </summary>
    public sealed class CastCommitProfileConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly string[] AllowedProfileKeys = { "id", "onActivate", "frameActions" };
        private static readonly string[] AllowedOpKeys = { "op", "payload", "contextProfileId" };

        public CastCommitProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load, schema-guard, and validate the merged cast commit profile config.</summary>
        public CastCommitProfilesConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/cast_commit_profiles.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            ValidateSchemaKeys(mergedObject, relativePath);
            var config = mergedObject.Deserialize<CastCommitProfilesConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>
        /// DEC-13 schema guard over the raw JSON: profile and op objects only carry whitelisted keys.
        /// The typed deserializer would silently drop unknown keys, so FSM-shaped schemas (state
        /// tables, transition tables) must be rejected here, before deserialization.
        /// </summary>
        public static void ValidateSchemaKeys(JsonObject root, string source)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (root["profiles"] is not JsonArray profiles)
            {
                throw new InvalidOperationException($"Cast commit config '{source}' must explicitly define profiles.");
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] is not JsonObject profile)
                {
                    throw new InvalidOperationException($"{source}.profiles[{i}] must be an object.");
                }

                string path = $"{source}.profiles[{i}]";
                RequireWhitelistedKeys(profile, AllowedProfileKeys, path);
                if (profile["onActivate"] is JsonArray onActivate)
                {
                    ValidateOpArraySchema(onActivate, $"{path}.onActivate");
                }

                if (profile["frameActions"] is JsonObject frameActions)
                {
                    foreach (KeyValuePair<string, JsonNode> action in frameActions)
                    {
                        if (action.Value is not JsonArray actionOps)
                        {
                            throw new InvalidOperationException(
                                $"{path}.frameActions['{action.Key}'] must be an array of ops.");
                        }

                        ValidateOpArraySchema(actionOps, $"{path}.frameActions['{action.Key}']");
                    }
                }
            }
        }

        /// <summary>Structural fail-fast validation; op kind and value source resolution happen at install.</summary>
        public static void Validate(CastCommitProfilesConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Profiles == null)
            {
                throw new InvalidOperationException($"Cast commit config '{source}' must explicitly define profiles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                CastCommitProfileDefinition profile = config.Profiles[i]
                    ?? throw new InvalidOperationException($"Cast commit config '{source}' profiles[{i}] must be an object.");
                string path = $"{source}.profiles[{i}]";
                RequireTrimmedNonEmpty(profile.Id, $"{path}.id");
                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates cast commit profile '{profile.Id}'.");
                }

                if (profile.OnActivate == null || profile.OnActivate.Count == 0)
                {
                    throw new InvalidOperationException($"{path}.onActivate must declare at least one op.");
                }

                ValidateOps(profile.OnActivate, $"{path}.onActivate");
                if (profile.FrameActions != null)
                {
                    foreach (KeyValuePair<string, List<CastCommitOpDefinition>> action in profile.FrameActions)
                    {
                        RequireTrimmedNonEmpty(action.Key, $"{path}.frameActions key");
                        if (action.Value == null || action.Value.Count == 0)
                        {
                            throw new InvalidOperationException(
                                $"{path}.frameActions['{action.Key}'] must declare at least one op.");
                        }

                        ValidateOps(action.Value, $"{path}.frameActions['{action.Key}']");
                    }
                }
            }
        }

        private static void ValidateOpArraySchema(JsonArray ops, string path)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i] is not JsonObject op)
                {
                    throw new InvalidOperationException($"{path}[{i}] must be an object.");
                }

                RequireWhitelistedKeys(op, AllowedOpKeys, $"{path}[{i}]");
            }
        }

        private static void RequireWhitelistedKeys(JsonObject node, string[] allowedKeys, string path)
        {
            foreach (KeyValuePair<string, JsonNode> property in node)
            {
                bool allowed = false;
                for (int i = 0; i < allowedKeys.Length; i++)
                {
                    if (string.Equals(property.Key, allowedKeys[i], StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException(
                        $"{path} declares unsupported key '{property.Key}' (RFC-0065 DEC-13: the cast commit " +
                        $"schema is op sequences only — no state-machine tables; allowed keys: {string.Join(", ", allowedKeys)}).");
                }
            }
        }

        private static void ValidateOps(List<CastCommitOpDefinition> ops, string path)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                CastCommitOpDefinition op = ops[i]
                    ?? throw new InvalidOperationException($"{path}[{i}] must be an object.");
                RequireTrimmedNonEmpty(op.Op, $"{path}[{i}].op");
                if (op.ContextProfileId != null)
                {
                    RequireTrimmedNonEmpty(op.ContextProfileId, $"{path}[{i}].contextProfileId");
                }

                if (op.Payload != null)
                {
                    foreach (KeyValuePair<string, string> entry in op.Payload)
                    {
                        RequireTrimmedNonEmpty(entry.Key, $"{path}[{i}].payload key");
                        RequireTrimmedNonEmpty(entry.Value, $"{path}[{i}].payload['{entry.Key}']");
                    }
                }
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
