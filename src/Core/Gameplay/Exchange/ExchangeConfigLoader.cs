using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Gameplay.Exchange
{
    public sealed class ExchangeConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ConfigPipeline _pipeline;
        private readonly ExchangeOperationRegistry _registry;
        private readonly ItemDefinitionRegistry _items;
        private readonly RelationshipTypeRegistry _relationshipTypes;
        private readonly RelationshipMetricRegistry _relationshipMetrics;
        private readonly RelationshipFlagRegistry _relationshipFlags;

        public ExchangeConfigLoader(
            ConfigPipeline pipeline,
            ExchangeOperationRegistry registry,
            ItemDefinitionRegistry items,
            RelationshipTypeRegistry relationshipTypes,
            RelationshipMetricRegistry relationshipMetrics,
            RelationshipFlagRegistry relationshipFlags)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _relationshipTypes = relationshipTypes ?? throw new ArgumentNullException(nameof(relationshipTypes));
            _relationshipMetrics = relationshipMetrics ?? throw new ArgumentNullException(nameof(relationshipMetrics));
            _relationshipFlags = relationshipFlags ?? throw new ArgumentNullException(nameof(relationshipFlags));
        }

        public void Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "Exchange/operations.json")
        {
            _registry.ClearDefinitions();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var rows = new List<(string Id, OperationConfig Config)>(merged.Count);

            for (int i = 0; i < merged.Count; i++)
            {
                var cfg = merged[i].Node.Deserialize<OperationConfig>(JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize exchange operation '{merged[i].Id}' from {relativePath}.");
                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    cfg.Id = merged[i].Id;
                }

                if (!string.Equals(cfg.Id, merged[i].Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Exchange operation id mismatch in {relativePath}: '{merged[i].Id}' vs '{cfg.Id}'.");
                }

                rows.Add((cfg.Id!, cfg));
            }

            rows.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            for (int i = 0; i < rows.Count; i++)
            {
                OperationConfig cfg = rows[i].Config;
                _registry.Register(rows[i].Id, new ExchangeOperationDefinition
                {
                    Id = rows[i].Id,
                    RelationshipRequirements = CompileRelationshipRequirements(cfg.RelationshipRequirements, rows[i].Id, relativePath),
                    Inputs = CompileInputs(cfg.Inputs, rows[i].Id, relativePath),
                    Outputs = CompileOutputs(cfg.Outputs, rows[i].Id, relativePath)
                });
            }
        }

        public void LoadIds(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "Exchange/operations.json")
        {
            _registry.Clear();

            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var ids = new List<string>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                ids.Add(merged[i].Id);
            }

            ids.Sort(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                _registry.Register(ids[i], new ExchangeOperationDefinition { Id = ids[i] });
            }

            _registry.ClearDefinitions();
        }

        private ExchangeRelationshipRequirement[] CompileRelationshipRequirements(
            RelationshipRequirementConfig[]? configs,
            string ownerId,
            string relativePath)
        {
            if (configs == null || configs.Length == 0)
            {
                return Array.Empty<ExchangeRelationshipRequirement>();
            }

            var output = new ExchangeRelationshipRequirement[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                RelationshipRequirementConfig cfg = configs[i]
                    ?? throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: relationshipRequirements[{i}] must be an object.");
                short? minimum = ToShort(cfg.MinimumMetric, ownerId, relativePath, $"relationshipRequirements[{i}].minimumMetric");
                short? maximum = ToShort(cfg.MaximumMetric, ownerId, relativePath, $"relationshipRequirements[{i}].maximumMetric");
                int metricId = minimum.HasValue || maximum.HasValue
                    ? ResolveRelationshipMetric(cfg.Metric, ownerId, relativePath, $"relationshipRequirements[{i}].metric")
                    : -1;
                int flagId = string.IsNullOrWhiteSpace(cfg.Flag)
                    ? -1
                    : ResolveRelationshipFlag(cfg.Flag, ownerId, relativePath, $"relationshipRequirements[{i}].flag");
                if (metricId < 0 && flagId < 0)
                {
                    throw new InvalidOperationException(
                        $"Exchange operation '{ownerId}' in {relativePath}: relationshipRequirements[{i}] must require a metric bound or a flag.");
                }

                output[i] = new ExchangeRelationshipRequirement(
                    ParseActorSlot(cfg.Source, ownerId, relativePath, $"relationshipRequirements[{i}].source"),
                    ParseActorSlot(cfg.Target, ownerId, relativePath, $"relationshipRequirements[{i}].target"),
                    ResolveRelationshipType(cfg.Type, ownerId, relativePath, $"relationshipRequirements[{i}].type"),
                    metricId,
                    minimum,
                    maximum,
                    flagId,
                    cfg.FlagValue ?? true);
            }

            return output;
        }

        private ExchangeInputDefinition[] CompileInputs(InputConfig[]? configs, string ownerId, string relativePath)
        {
            if (configs == null || configs.Length == 0)
            {
                return Array.Empty<ExchangeInputDefinition>();
            }

            var output = new ExchangeInputDefinition[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                InputConfig cfg = configs[i] ?? throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: inputs[{i}] must be an object.");
                ExchangeInputKind kind = ParseInputKind(cfg.Kind, ownerId, relativePath, i);
                output[i] = kind switch
                {
                    ExchangeInputKind.ItemStack => CompileItemStackInput(cfg, ownerId, relativePath, i),
                    ExchangeInputKind.AttributeCost => CompileAttributeCostInput(cfg, ownerId, relativePath, i),
                    _ => throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: inputs[{i}] uses unsupported kind '{cfg.Kind}'.")
                };
            }

            return output;
        }

        private ExchangeInputDefinition CompileItemStackInput(InputConfig cfg, string ownerId, string relativePath, int index)
        {
            return new ExchangeInputDefinition(
                ExchangeInputKind.ItemStack,
                ParseActorSlot(cfg.Actor, ownerId, relativePath, $"inputs[{index}].actor"),
                ResolveItem(cfg.Item, ownerId, relativePath, $"inputs[{index}].item"),
                RequirePositive(cfg.Quantity, ownerId, relativePath, $"inputs[{index}].quantity"));
        }

        private static ExchangeInputDefinition CompileAttributeCostInput(InputConfig cfg, string ownerId, string relativePath, int index)
        {
            string attribute = RequireString(cfg.Attribute, ownerId, relativePath, $"inputs[{index}].attribute");
            int attributeId = AttributeRegistry.Register(attribute);
            return ExchangeInputDefinition.AttributeCost(
                ParseActorSlot(cfg.Actor, ownerId, relativePath, $"inputs[{index}].actor"),
                attributeId,
                RequirePositive(cfg.Quantity, ownerId, relativePath, $"inputs[{index}].quantity"));
        }

        private int ResolveRelationshipType(string? value, string ownerId, string relativePath, string field)
        {
            string typeId = RequireString(value, ownerId, relativePath, field);
            return _relationshipTypes.GetId(typeId);
        }

        private int ResolveRelationshipMetric(string? value, string ownerId, string relativePath, string field)
        {
            string metricId = RequireString(value, ownerId, relativePath, field);
            return _relationshipMetrics.GetId(metricId);
        }

        private int ResolveRelationshipFlag(string? value, string ownerId, string relativePath, string field)
        {
            string flagId = RequireString(value, ownerId, relativePath, field);
            return _relationshipFlags.GetId(flagId);
        }

        private ExchangeOutputDefinition[] CompileOutputs(OutputConfig[]? configs, string ownerId, string relativePath)
        {
            if (configs == null || configs.Length == 0)
            {
                return Array.Empty<ExchangeOutputDefinition>();
            }

            var output = new ExchangeOutputDefinition[configs.Length];
            for (int i = 0; i < configs.Length; i++)
            {
                OutputConfig cfg = configs[i] ?? throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: outputs[{i}] must be an object.");
                ExchangeOutputKind kind = ParseOutputKind(cfg.Kind, ownerId, relativePath, i);
                output[i] = kind switch
                {
                    ExchangeOutputKind.CreateItem => CompileCreateOutput(cfg, ownerId, relativePath, i),
                    ExchangeOutputKind.MoveItem => CompileMoveOutput(cfg, ownerId, relativePath, i),
                    ExchangeOutputKind.EffectRequest => CompileEffectOutput(cfg, ownerId, relativePath, i),
                    _ => throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: outputs[{i}] uses unsupported kind '{cfg.Kind}'.")
                };
            }

            return output;
        }

        private ExchangeOutputDefinition CompileCreateOutput(OutputConfig cfg, string ownerId, string relativePath, int index)
        {
            return new ExchangeOutputDefinition(
                ExchangeOutputKind.CreateItem,
                ParseActorSlot(cfg.Actor, ownerId, relativePath, $"outputs[{index}].actor"),
                ParsePurpose(cfg.Purpose, ownerId, relativePath, $"outputs[{index}].purpose"),
                ResolveItem(cfg.Item, ownerId, relativePath, $"outputs[{index}].item"),
                RequirePositive(cfg.Quantity, ownerId, relativePath, $"outputs[{index}].quantity"),
                cfg.Charges ?? 0,
                cfg.Durability ?? 0,
                RoleSlot.None,
                ItemContainerPurpose.None,
                0,
                RoleSlot.None,
                RoleSlot.None,
                RoleSlot.None);
        }

        private ExchangeOutputDefinition CompileMoveOutput(OutputConfig cfg, string ownerId, string relativePath, int index)
        {
            return new ExchangeOutputDefinition(
                ExchangeOutputKind.MoveItem,
                ParseActorSlot(cfg.Actor, ownerId, relativePath, $"outputs[{index}].actor"),
                ParsePurpose(cfg.Purpose, ownerId, relativePath, $"outputs[{index}].purpose"),
                ResolveItem(cfg.Item, ownerId, relativePath, $"outputs[{index}].item"),
                cfg.Quantity ?? 1,
                0,
                0,
                ParseActorSlot(cfg.FromActor, ownerId, relativePath, $"outputs[{index}].fromActor"),
                ParseOptionalPurpose(cfg.FromPurpose, ownerId, relativePath, $"outputs[{index}].fromPurpose"),
                0,
                RoleSlot.None,
                RoleSlot.None,
                RoleSlot.None);
        }

        private ExchangeOutputDefinition CompileEffectOutput(OutputConfig cfg, string ownerId, string relativePath, int index)
        {
            string effect = RequireString(cfg.Effect, ownerId, relativePath, $"outputs[{index}].effect");
            int effectId = EffectTemplateIdRegistry.GetId(effect);
            if (effectId <= 0)
            {
                throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: outputs[{index}].effect references missing effect template '{effect}'.");
            }

            return new ExchangeOutputDefinition(
                ExchangeOutputKind.EffectRequest,
                RoleSlot.None,
                ItemContainerPurpose.None,
                0,
                0,
                0,
                0,
                RoleSlot.None,
                ItemContainerPurpose.None,
                effectId,
                ParseActorSlot(cfg.EffectSource, ownerId, relativePath, $"outputs[{index}].effectSource"),
                ParseActorSlot(cfg.EffectTarget, ownerId, relativePath, $"outputs[{index}].effectTarget"),
                ParseActorSlot(cfg.EffectContext, ownerId, relativePath, $"outputs[{index}].effectContext"));
        }

        private int ResolveItem(string? value, string ownerId, string relativePath, string field)
        {
            string itemId = RequireString(value, ownerId, relativePath, field);
            int itemDefinitionId = _items.GetId(itemId);
            if (itemDefinitionId <= 0)
            {
                throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} references missing item definition '{itemId}'.");
            }

            return itemDefinitionId;
        }

        private static ExchangeInputKind ParseInputKind(string? value, string ownerId, string relativePath, int index)
        {
            string raw = RequireString(value, ownerId, relativePath, $"inputs[{index}].kind");
            return raw switch
            {
                "ItemStack" => ExchangeInputKind.ItemStack,
                "AttributeCost" => ExchangeInputKind.AttributeCost,
                _ => throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: unsupported inputs[{index}].kind '{raw}'.")
            };
        }

        private static ExchangeOutputKind ParseOutputKind(string? value, string ownerId, string relativePath, int index)
        {
            string raw = RequireString(value, ownerId, relativePath, $"outputs[{index}].kind");
            return raw switch
            {
                "CreateItem" => ExchangeOutputKind.CreateItem,
                "MoveItem" => ExchangeOutputKind.MoveItem,
                "EffectRequest" => ExchangeOutputKind.EffectRequest,
                _ => throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: unsupported outputs[{index}].kind '{raw}'.")
            };
        }

        private static RoleSlot ParseActorSlot(string? value, string ownerId, string relativePath, string field)
        {
            string raw = RequireString(value, ownerId, relativePath, field);
            return raw switch
            {
                "Source" => RoleSlot.Source,
                "Target" => RoleSlot.Target,
                "Context" => RoleSlot.Context,
                _ => throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} has unsupported actor slot '{raw}'.")
            };
        }

        private static ItemContainerPurpose ParsePurpose(string? value, string ownerId, string relativePath, string field)
        {
            string raw = RequireString(value, ownerId, relativePath, field);
            if (Enum.TryParse(raw, ignoreCase: false, out ItemContainerPurpose purpose) && purpose != ItemContainerPurpose.None)
            {
                return purpose;
            }

            throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} has unsupported item container purpose '{raw}'.");
        }

        private static ItemContainerPurpose ParseOptionalPurpose(string? value, string ownerId, string relativePath, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ItemContainerPurpose.None;
            }

            if (Enum.TryParse(value, ignoreCase: false, out ItemContainerPurpose purpose))
            {
                return purpose;
            }

            throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} has unsupported item container purpose '{value}'.");
        }

        private static string RequireString(string? value, string ownerId, string relativePath, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} is required.");
            }

            return value;
        }

        private static int RequirePositive(int? value, string ownerId, string relativePath, string field)
        {
            int result = value ?? 0;
            if (result <= 0)
            {
                throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} must be > 0.");
            }

            return result;
        }

        private static short? ToShort(int? value, string ownerId, string relativePath, string field)
        {
            if (!value.HasValue)
            {
                return null;
            }

            if (value.Value < short.MinValue || value.Value > short.MaxValue)
            {
                throw new InvalidOperationException($"Exchange operation '{ownerId}' in {relativePath}: {field} must fit in Int16.");
            }

            return (short)value.Value;
        }

        private sealed class OperationConfig
        {
            public string? Id { get; set; }

            public RelationshipRequirementConfig[]? RelationshipRequirements { get; set; }

            public InputConfig[]? Inputs { get; set; }

            public OutputConfig[]? Outputs { get; set; }
        }

        private sealed class RelationshipRequirementConfig
        {
            public string? Source { get; set; }

            public string? Target { get; set; }

            public string? Type { get; set; }

            public string? Metric { get; set; }

            public int? MinimumMetric { get; set; }

            public int? MaximumMetric { get; set; }

            public string? Flag { get; set; }

            public bool? FlagValue { get; set; }
        }

        private sealed class InputConfig
        {
            public string? Kind { get; set; }

            public string? Actor { get; set; }

            public string? Item { get; set; }

            public string? Attribute { get; set; }

            public int? Quantity { get; set; }
        }

        private sealed class OutputConfig
        {
            public string? Kind { get; set; }

            public string? Actor { get; set; }

            public string? Purpose { get; set; }

            public string? Item { get; set; }

            public int? Quantity { get; set; }

            public int? Charges { get; set; }

            public int? Durability { get; set; }

            public string? FromActor { get; set; }

            public string? FromPurpose { get; set; }

            public string? Effect { get; set; }

            public string? EffectSource { get; set; }

            public string? EffectTarget { get; set; }

            public string? EffectContext { get; set; }
        }
    }
}
