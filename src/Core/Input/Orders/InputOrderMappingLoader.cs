using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Loader for input-order mapping configurations.
    /// </summary>
    public sealed class InputOrderMappingLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = StrictJsonOptions.CreateCamelCase();

        public InputOrderMappingLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Load configuration from ConfigPipeline.
        /// </summary>
        public InputOrderMappingConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/input_order_mappings.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<InputOrderMappingConfig>(JsonOptions);
            config = config ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>
        /// Load configuration from a file path (for user overrides/preferences).
        /// </summary>
        public static InputOrderMappingConfig LoadFromFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return new InputOrderMappingConfig();

            var content = System.IO.File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(content, JsonOptions);
            config ??= new InputOrderMappingConfig();
            Validate(config, filePath);
            return config;
        }

        /// <summary>
        /// Load configuration from a stream (for VFS access).
        /// </summary>
        public static InputOrderMappingConfig LoadFromStream(System.IO.Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var config = JsonSerializer.Deserialize<InputOrderMappingConfig>(stream, JsonOptions);
            config = config ?? throw new InvalidOperationException("Deserialized null from input_order_mappings stream.");
            Validate(config, "input_order_mappings stream");
            return config;
        }

        /// <summary>
        /// Save configuration to JSON.
        /// </summary>
        public static string SaveToJson(InputOrderMappingConfig config)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(config, options);
        }

        /// <summary>
        /// Save configuration to a file.
        /// </summary>
        public static void SaveToFile(string filePath, InputOrderMappingConfig config)
        {
            var json = SaveToJson(config);
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            System.IO.File.WriteAllText(filePath, json);
        }

        public static void Validate(InputOrderMappingConfig config, string source)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.Mappings == null)
            {
                throw new InvalidOperationException($"Input order mapping config '{source}' must explicitly define mappings.");
            }

            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Mappings.Count; i++)
            {
                InputOrderMapping mapping = config.Mappings[i] ??
                    throw new InvalidOperationException($"Input order mapping '{source}' mappings[{i}] must be an object.");

                string path = $"{source}.mappings[{i}]";
                if (string.IsNullOrWhiteSpace(mapping.ActionId))
                {
                    throw new InvalidOperationException($"{path}.actionId must be a non-empty string.");
                }

                if (!string.Equals(mapping.ActionId, mapping.ActionId.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.actionId must not contain leading or trailing whitespace.");
                }

                if (!actionIds.Add(mapping.ActionId))
                {
                    throw new InvalidOperationException(
                        $"{path}.actionId duplicates input action '{mapping.ActionId}'.");
                }

                if (string.IsNullOrWhiteSpace(mapping.OrderTypeKey))
                {
                    if (mapping.ActorOrderRouting == null || mapping.ActorOrderRouting.Candidates.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"{path} must define orderTypeKey or actorOrderRouting.candidates.");
                    }
                }
                else if (!string.Equals(mapping.OrderTypeKey, mapping.OrderTypeKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.orderTypeKey must not contain leading or trailing whitespace.");
                }

                if (mapping.ActorOrderRouting != null)
                {
                    if (mapping.IsSkillMapping)
                    {
                        throw new InvalidOperationException($"{path}.actorOrderRouting is only valid when isSkillMapping is false.");
                    }

                    if (mapping.TargetType == OrderTargetType.Entities)
                    {
                        throw new InvalidOperationException($"{path}.actorOrderRouting does not support TargetType=Entities.");
                    }

                    ValidateActorOrderRouting(mapping.ActorOrderRouting, path);
                }

                ValidateOptionalCollectionKey(mapping.ActorCollectionKey, $"{path}.actorCollectionKey");
                ValidateOptionalCollectionKey(mapping.TargetCollectionKey, $"{path}.targetCollectionKey");
                if (RequiresActorCollection(mapping) && string.IsNullOrWhiteSpace(mapping.ActorCollectionKey))
                {
                    throw new InvalidOperationException(
                        $"{path}.actorCollectionKey must be configured explicitly when actor collection fan-out or routing is used.");
                }

                if (RequiresTargetCollection(mapping) && string.IsNullOrWhiteSpace(mapping.TargetCollectionKey))
                {
                    throw new InvalidOperationException(
                        $"{path}.targetCollectionKey must be configured explicitly when targetType requires collection target data.");
                }

                if (mapping.ActorOrderRouting == null || mapping.ActorOrderRouting.Candidates.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(mapping.OrderTypeKey))
                    {
                        throw new InvalidOperationException($"{path}.orderTypeKey must be a non-empty string.");
                    }
                }

                if (mapping.HeldPolicy == HeldPolicy.StartEnd && mapping.Trigger != InputTriggerType.Held)
                {
                    throw new InvalidOperationException(
                        $"{path}.heldPolicy StartEnd requires trigger Held.");
                }

                if (mapping.ArgsTemplate == null)
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_ORDER_ARGS_TEMPLATE_REQUIRED: {path}.argsTemplate must be an object.");
                }

                if (mapping.IsSkillMapping &&
                    (mapping.ArgsTemplate.I0 is not int priority || priority < 0))
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_ORDER_SKILL_PRIORITY_REQUIRED: {path}.argsTemplate.i0 must define a non-negative skill priority.");
                }

                if (mapping.AutoTargetPolicy != AutoTargetPolicy.None && mapping.AutoTargetRangeCm <= 0)
                {
                    throw new InvalidOperationException(
                        $"{path}.autoTargetRangeCm must be positive when autoTargetPolicy is {mapping.AutoTargetPolicy}.");
                }

                if (mapping.CursorTargetPolicy != AutoTargetPolicy.None && mapping.CursorTargetRangeCm <= 0)
                {
                    throw new InvalidOperationException(
                        $"{path}.cursorTargetRangeCm must be positive when cursorTargetPolicy is {mapping.CursorTargetPolicy}.");
                }

                if (mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                    mapping.CursorTargetPolicy != AutoTargetPolicy.None)
                {
                    throw new InvalidOperationException(
                        $"{path} must not declare both autoTargetPolicy and cursorTargetPolicy; configured target source must be explicit.");
                }

                if (mapping.AutoTargetPolicy != AutoTargetPolicy.None &&
                    mapping.TargetType != OrderTargetType.Entity &&
                    mapping.TargetType != OrderTargetType.Position)
                {
                    throw new InvalidOperationException(
                        $"{path}.autoTargetPolicy requires targetType Entity or Position.");
                }

                if (mapping.CursorTargetPolicy != AutoTargetPolicy.None &&
                    mapping.TargetType != OrderTargetType.Position &&
                    mapping.TargetType != OrderTargetType.Direction)
                {
                    throw new InvalidOperationException(
                        $"{path}.cursorTargetPolicy requires targetType Position or Direction.");
                }
            }

            ValidateGroupMoveTargetLayout(config.GroupMoveTargetLayout, source);
        }

        private static void ValidateOptionalCollectionKey(string key, string path)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }

        private static bool RequiresActorCollection(InputOrderMapping mapping)
        {
            return mapping.ActorOrderRouting != null;
        }

        private static bool RequiresTargetCollection(InputOrderMapping mapping)
        {
            return mapping.TargetType == OrderTargetType.Entities ||
                   (mapping.TargetType == OrderTargetType.Entity &&
                    !mapping.IsSkillMapping &&
                    mapping.RequireTarget &&
                    mapping.AutoTargetPolicy == AutoTargetPolicy.None &&
                    mapping.CursorTargetPolicy == AutoTargetPolicy.None);
        }

        private static void ValidateGroupMoveTargetLayout(GroupMoveTargetLayoutSettings settings, string source)
        {
            if (settings.Mode != GroupMoveTargetLayoutMode.Grid)
            {
                return;
            }

            if (settings.OrderTypeKeys == null || settings.OrderTypeKeys.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{source}.groupMoveTargetLayout.orderTypeKeys must be a non-empty array when mode is Grid.");
            }

            if (settings.SpacingCm <= 0)
            {
                throw new InvalidOperationException(
                    $"{source}.groupMoveTargetLayout.spacingCm must be greater than zero when mode is Grid.");
            }

            for (int i = 0; i < settings.OrderTypeKeys.Count; i++)
            {
                string key = settings.OrderTypeKeys[i];
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException(
                        $"{source}.groupMoveTargetLayout.orderTypeKeys[{i}] must be a non-empty string.");
                }

                if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{source}.groupMoveTargetLayout.orderTypeKeys[{i}] must not contain leading or trailing whitespace.");
                }
            }
        }

        private static void ValidateActorOrderRouting(ActorOrderRoutingSettings routing, string path)
        {
            if (routing.Candidates == null || routing.Candidates.Count == 0)
            {
                throw new InvalidOperationException($"{path}.actorOrderRouting.candidates must be a non-empty array.");
            }

            for (int i = 0; i < routing.Candidates.Count; i++)
            {
                ActorOrderRoutingCandidate candidate = routing.Candidates[i] ??
                    throw new InvalidOperationException($"{path}.actorOrderRouting.candidates[{i}] must be an object.");
                string candidatePath = $"{path}.actorOrderRouting.candidates[{i}]";
                if (string.IsNullOrWhiteSpace(candidate.OrderTypeKey))
                {
                    throw new InvalidOperationException($"{candidatePath}.orderTypeKey must be a non-empty string.");
                }

                if (!string.Equals(candidate.OrderTypeKey, candidate.OrderTypeKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{candidatePath}.orderTypeKey must not contain leading or trailing whitespace.");
                }

                candidate.Match ??= new ActorOrderRoutingMatch();
            }
        }

    }
}
