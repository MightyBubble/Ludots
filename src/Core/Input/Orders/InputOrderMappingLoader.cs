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

            ValidateAuthoredContract(mergedObject, relativePath);
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
            JsonNode node = JsonNode.Parse(content) ??
                throw new InvalidOperationException($"Failed to parse '{filePath}'.");
            ValidateAuthoredContract(node, filePath);
            var config = node.Deserialize<InputOrderMappingConfig>(JsonOptions);
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
            JsonNode node = JsonNode.Parse(stream) ??
                throw new InvalidOperationException("Deserialized null from input_order_mappings stream.");
            ValidateAuthoredContract(node, "input_order_mappings stream");
            var config = node.Deserialize<InputOrderMappingConfig>(JsonOptions);
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

        private static void ValidateAuthoredContract(JsonNode rootNode, string source)
        {
            if (rootNode is not JsonObject root)
            {
                return;
            }

            if (root.ContainsKey("groupMoveTargetLayout"))
            {
                throw new InvalidOperationException(
                    $"{source}.groupMoveTargetLayout is retired; use targetLayoutProfiles plus mapping.targetLayoutProfileId.");
            }

            if (!root.TryGetPropertyValue("mappings", out JsonNode? mappingsNode))
            {
                return;
            }

            if (mappingsNode is not JsonArray mappings)
            {
                return;
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                if (mappings[i] is not JsonObject mapping)
                {
                    continue;
                }

                string path = $"{source}.mappings[{i}]";
                bool hasArgsTemplate = mapping.ContainsKey("argsTemplate");
                bool hasOrderPayload = mapping.ContainsKey("orderPayload");

                if (hasArgsTemplate && hasOrderPayload)
                {
                    throw new InvalidOperationException(
                        $"{path} must not declare both argsTemplate and orderPayload; use the typed orderPayload contract only.");
                }

                if (hasArgsTemplate)
                {
                    throw new InvalidOperationException(
                        $"{path}.argsTemplate is a runtime order ABI field; authored input mappings must use orderPayload.");
                }

                if (mapping.ContainsKey("isSkillMapping"))
                {
                    throw new InvalidOperationException(
                        $"{path}.isSkillMapping is derived from orderPayload.kind and must not be authored.");
                }

                if (mapping.ContainsKey("actorOrderRouting"))
                {
                    throw new InvalidOperationException(
                        $"{path}.actorOrderRouting is retired from authored input mappings; use CommandIntent profiles.");
                }
            }
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

            config.TargetLayoutProfiles ??= new List<TargetLayoutProfileDefinition>();
            ValidateTargetLayoutProfiles(config.TargetLayoutProfiles, source);

            var actionVariants = new Dictionary<string, ActionMappingVariantSet>(StringComparer.Ordinal);
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

                if (string.IsNullOrWhiteSpace(mapping.OrderTypeKey))
                {
                    throw new InvalidOperationException($"{path}.orderTypeKey must be a non-empty string.");
                }
                else if (!string.Equals(mapping.OrderTypeKey, mapping.OrderTypeKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.orderTypeKey must not contain leading or trailing whitespace.");
                }

                mapping.OrderPayload ??= new InputOrderPayloadTemplate();
                ValidateOrderPayload(mapping, path);
                mapping.ApplyDerivedRuntimeFields();

                ValidateAbilityQualifier(mapping, path);
                TrackActionMappingVariant(actionVariants, mapping, path);

                ValidateOptionalCollectionKey(mapping.ActorCollectionKey, $"{path}.actorCollectionKey");
                ValidateOptionalCollectionKey(mapping.TargetCollectionKey, $"{path}.targetCollectionKey");
                ValidateMappingTargetLayoutReference(
                    mapping,
                    config.TargetLayoutProfiles,
                    $"{path}.targetLayoutProfileId");
                if (RequiresTargetCollection(mapping) && string.IsNullOrWhiteSpace(mapping.TargetCollectionKey))
                {
                    throw new InvalidOperationException(
                        $"{path}.targetCollectionKey must be configured explicitly when targetType requires collection target data.");
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
                    (!mapping.TryResolveAbilitySlot(out int priority) || priority < 0))
                {
                    throw new InvalidOperationException(
                        $"LUDOTS_INPUT_ORDER_SKILL_PRIORITY_REQUIRED: {path}.orderPayload.abilitySlot must define a non-negative ability slot.");
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

            foreach (KeyValuePair<string, ActionMappingVariantSet> pair in actionVariants)
            {
                pair.Value.ValidateComplete(pair.Value.FirstPath);
            }
        }

        public static void ResolveAbilityIdKeys(
            InputOrderMappingConfig config,
            Func<string, int> abilityIdResolver,
            string source)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (abilityIdResolver == null) throw new ArgumentNullException(nameof(abilityIdResolver));

            for (int i = 0; i < config.Mappings.Count; i++)
            {
                InputOrderMapping mapping = config.Mappings[i];
                string path = $"{source}.mappings[{i}].abilityIdKey";
                if (string.IsNullOrEmpty(mapping.AbilityIdKey))
                {
                    mapping.AbilityId = 0;
                    continue;
                }

                int abilityId = abilityIdResolver(mapping.AbilityIdKey);
                if (abilityId <= 0)
                {
                    throw new InvalidOperationException(
                        $"{path} references unknown ability '{mapping.AbilityIdKey}'.");
                }

                mapping.AbilityId = abilityId;
            }
        }

        private static void ValidateAbilityQualifier(InputOrderMapping mapping, string path)
        {
            if (string.IsNullOrEmpty(mapping.AbilityIdKey))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(mapping.AbilityIdKey))
            {
                throw new InvalidOperationException($"{path}.abilityIdKey must be a non-empty string.");
            }

            if (!string.Equals(mapping.AbilityIdKey, mapping.AbilityIdKey.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.abilityIdKey must not contain leading or trailing whitespace.");
            }

            if (mapping.OrderPayload == null ||
                mapping.OrderPayload.Kind != InputOrderPayloadKind.CastAbility ||
                !mapping.TryResolveAbilitySlot(out int slot) ||
                slot < 0)
            {
                throw new InvalidOperationException(
                    $"{path}.abilityIdKey is only valid on CastAbility mappings with orderPayload.abilitySlot.");
            }
        }

        private static void TrackActionMappingVariant(
            Dictionary<string, ActionMappingVariantSet> actionVariants,
            InputOrderMapping mapping,
            string path)
        {
            if (actionVariants.TryGetValue(mapping.ActionId, out ActionMappingVariantSet? variants))
            {
                variants.Add(mapping, path);
                return;
            }

            actionVariants.Add(mapping.ActionId, new ActionMappingVariantSet(mapping, path));
        }

        private sealed class ActionMappingVariantSet
        {
            private readonly HashSet<string> _qualifiedAbilityKeys = new(StringComparer.Ordinal);

            public ActionMappingVariantSet(InputOrderMapping first, string path)
            {
                ActionId = first.ActionId;
                FirstPath = path;
                MappingCount = 1;
                if (first.TryResolveAbilitySlot(out int slot))
                {
                    AbilitySlot = slot;
                    HasAbilitySlot = true;
                }

                RecordQualifier(first, path);
            }

            public string ActionId { get; }
            public string FirstPath { get; }
            public int AbilitySlot { get; private set; }
            public bool HasAbilitySlot { get; private set; }
            public int MappingCount { get; private set; }
            public bool HasUnqualifiedMapping { get; private set; }
            public bool HasQualifiedMapping { get; private set; }

            public void Add(InputOrderMapping mapping, string path)
            {
                MappingCount++;
                if (!mapping.IsSkillMapping || !TryRequireSlot(mapping, path, out int slot))
                {
                    throw new InvalidOperationException(
                        $"{path}.actionId duplicates input action '{ActionId}'. Duplicate actions are only valid for ability-qualified CastAbility mappings.");
                }

                if (!HasAbilitySlot)
                {
                    AbilitySlot = slot;
                    HasAbilitySlot = true;
                }
                else if (slot != AbilitySlot)
                {
                    throw new InvalidOperationException(
                        $"{path}.actionId duplicates input action '{ActionId}' but does not use the same orderPayload.abilitySlot as {FirstPath}.");
                }

                RecordQualifier(mapping, path);
            }

            public void ValidateComplete(string path)
            {
                if (MappingCount <= 1)
                {
                    if (HasQualifiedMapping)
                    {
                        throw new InvalidOperationException(
                            $"{path}.abilityIdKey requires an unqualified mapping with the same actionId and orderPayload.abilitySlot.");
                    }

                    return;
                }

                if (!HasQualifiedMapping || !HasUnqualifiedMapping)
                {
                    throw new InvalidOperationException(
                        $"{path}.actionId duplicates input action '{ActionId}'. Duplicate actions require one unqualified mapping and one or more abilityIdKey-qualified mappings.");
                }
            }

            private void RecordQualifier(InputOrderMapping mapping, string path)
            {
                if (!TryRequireSlot(mapping, path, out int slot))
                {
                    if (MappingCount > 1)
                    {
                        throw new InvalidOperationException(
                            $"{path}.actionId duplicates input action '{ActionId}'. Duplicate actions are only valid for CastAbility mappings.");
                    }

                    return;
                }

                if (!HasAbilitySlot)
                {
                    AbilitySlot = slot;
                    HasAbilitySlot = true;
                }

                if (string.IsNullOrEmpty(mapping.AbilityIdKey))
                {
                    if (HasUnqualifiedMapping)
                    {
                        throw new InvalidOperationException(
                            $"{path}.actionId duplicates unqualified input action '{ActionId}'. Use one default mapping plus abilityIdKey-qualified variants.");
                    }

                    HasUnqualifiedMapping = true;
                    return;
                }

                if (!_qualifiedAbilityKeys.Add(mapping.AbilityIdKey))
                {
                    throw new InvalidOperationException(
                        $"{path}.abilityIdKey duplicates ability-qualified mapping '{mapping.AbilityIdKey}' for action '{ActionId}'.");
                }

                HasQualifiedMapping = true;
            }

            private static bool TryRequireSlot(InputOrderMapping mapping, string path, out int slot)
            {
                if (mapping.IsSkillMapping && mapping.TryResolveAbilitySlot(out slot))
                {
                    return true;
                }

                slot = -1;
                return false;
            }
        }

        private static void ValidateOrderPayload(InputOrderMapping mapping, string path)
        {
            InputOrderPayloadTemplate payload = mapping.OrderPayload;
            switch (payload.Kind)
            {
                case InputOrderPayloadKind.None:
                    if (payload.HasAuthoredFields)
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderPayload kind None must not declare payload fields.");
                    }
                    break;

                case InputOrderPayloadKind.CastAbility:
                    if (payload.AbilitySlot is not int abilitySlot || abilitySlot < 0)
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderPayload.abilitySlot must be a non-negative integer when kind is CastAbility.");
                    }
                    break;

                case InputOrderPayloadKind.TargetEntity:
                    RejectAbilitySlot(payload, $"{path}.orderPayload", payload.Kind);
                    if (mapping.TargetType != OrderTargetType.Entity &&
                        mapping.TargetType != OrderTargetType.HoveredEntityOrPosition)
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderPayload kind TargetEntity requires targetType Entity or HoveredEntityOrPosition.");
                    }
                    break;

                case InputOrderPayloadKind.MoveToWorldCm:
                    RejectAbilitySlot(payload, $"{path}.orderPayload", payload.Kind);
                    if (mapping.TargetType != OrderTargetType.Position &&
                        mapping.TargetType != OrderTargetType.HoveredEntityOrPosition)
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderPayload kind MoveToWorldCm requires targetType Position or HoveredEntityOrPosition.");
                    }
                    break;

                case InputOrderPayloadKind.Stop:
                    RejectAbilitySlot(payload, $"{path}.orderPayload", payload.Kind);
                    if (mapping.RequireTarget || mapping.TargetType != OrderTargetType.None)
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderPayload kind Stop must not declare a target requirement.");
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"{path}.orderPayload.kind '{payload.Kind}' is not supported.");
            }
        }

        private static void RejectAbilitySlot(
            InputOrderPayloadTemplate payload,
            string path,
            InputOrderPayloadKind kind)
        {
            if (payload.AbilitySlot.HasValue)
            {
                throw new InvalidOperationException(
                    $"{path}.abilitySlot is only valid when kind is CastAbility; current kind is {kind}.");
            }
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

        private static void ValidateTargetLayoutProfiles(
            List<TargetLayoutProfileDefinition> profiles,
            string source)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                TargetLayoutProfileDefinition profile = profiles[i] ??
                    throw new InvalidOperationException($"{source}.targetLayoutProfiles[{i}] must be an object.");
                string path = $"{source}.targetLayoutProfiles[{i}]";
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id must be a non-empty string.");
                }

                if (!string.Equals(profile.Id, profile.Id.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.id must not contain leading or trailing whitespace.");
                }

                if (!ids.Add(profile.Id))
                {
                    throw new InvalidOperationException($"{path}.id duplicates target layout profile '{profile.Id}'.");
                }

                if (profile.Mode != TargetLayoutMode.Grid)
                {
                    continue;
                }

                if (profile.OrderTypeKeys == null || profile.OrderTypeKeys.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"{path}.orderTypeKeys must be a non-empty array when mode is Grid.");
                }

                if (profile.SpacingCm <= 0)
                {
                    throw new InvalidOperationException(
                        $"{path}.spacingCm must be greater than zero when mode is Grid.");
                }

                for (int keyIndex = 0; keyIndex < profile.OrderTypeKeys.Count; keyIndex++)
                {
                    string key = profile.OrderTypeKeys[keyIndex];
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderTypeKeys[{keyIndex}] must be a non-empty string.");
                    }

                    if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{path}.orderTypeKeys[{keyIndex}] must not contain leading or trailing whitespace.");
                    }
                }
            }
        }

        private static void ValidateMappingTargetLayoutReference(
            InputOrderMapping mapping,
            List<TargetLayoutProfileDefinition> profiles,
            string path)
        {
            mapping.TargetLayoutProfileIndex = -1;
            if (string.IsNullOrEmpty(mapping.TargetLayoutProfileId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(mapping.TargetLayoutProfileId))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(mapping.TargetLayoutProfileId, mapping.TargetLayoutProfileId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }

            int profileIndex = -1;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].Id, mapping.TargetLayoutProfileId, StringComparison.Ordinal))
                {
                    profileIndex = i;
                    break;
                }
            }

            if (profileIndex < 0)
            {
                throw new InvalidOperationException(
                    $"{path} references unknown target layout profile '{mapping.TargetLayoutProfileId}'.");
            }

            if (mapping.TargetType != OrderTargetType.Position)
            {
                throw new InvalidOperationException(
                    $"{path} is only valid for Position target mappings.");
            }

            mapping.TargetLayoutProfileIndex = profileIndex;
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

    }
}
