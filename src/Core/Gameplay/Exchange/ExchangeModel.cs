using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Items;

namespace Ludots.Core.Gameplay.Exchange
{
    public enum ExchangeInputKind : byte
    {
        None = 0,
        ItemStack = 1,
        AttributeCost = 2
    }

    public enum ExchangeOutputKind : byte
    {
        None = 0,
        CreateItem = 1,
        MoveItem = 2,
        EffectRequest = 3
    }

    public enum ExchangeExecutionStatus : byte
    {
        Success = 0,
        MissingOperation = 1,
        MissingActor = 2,
        MissingContainer = 3,
        InsufficientInput = 4,
        MissingSourceItem = 5,
        OutputBlocked = 6,
        ExecutionFailed = 7,
        RelationshipDenied = 8
    }

    public enum ExchangeRelationshipMetricComparison : byte
    {
        None = 0,
        GreaterOrEqual = 1,
        LessOrEqual = 2,
        RangeInclusive = 3
    }

    public readonly struct ExchangeExecutionContext
    {
        public ExchangeExecutionContext(Entity source, Entity target = default, Entity context = default, ScopeKey scope = default)
        {
            Roles = new RoleResolverContext(
                source: source,
                target: target,
                context: context,
                actor: source,
                subject: target,
                viewer: source,
                explicitScopeHost: context);
            Scope = scope;
        }

        public ExchangeExecutionContext(in RoleResolverContext roles, ScopeKey scope = default)
        {
            Roles = roles;
            Scope = scope;
        }

        public RoleResolverContext Roles { get; }

        public ScopeKey Scope { get; }

        public Entity Source => Roles.Source;

        public Entity Target => Roles.Target;

        public Entity Context => Roles.Context;

        public Entity Resolve(RoleSlot slot)
        {
            RoleResolverContext roles = Roles;
            return RoleResolver.Resolve(slot, in roles);
        }
    }

    public readonly struct ExchangeOperationKey
    {
        public ExchangeOperationKey(int operationId, ScopeKey scope = default)
        {
            OperationId = operationId;
            Scope = scope;
        }

        public int OperationId { get; }

        public ScopeKey Scope { get; }

        public bool HasScope => Scope.Kind == ScopeKind.Named && Scope.ScopeKeyId > 0;
    }

    public readonly struct ExchangeExecutionResult
    {
        public ExchangeExecutionResult(ExchangeExecutionStatus status, int operationId = 0, int detailIndex = -1)
        {
            Status = status;
            OperationId = operationId;
            DetailIndex = detailIndex;
        }

        public ExchangeExecutionStatus Status { get; }

        public int OperationId { get; }

        public int DetailIndex { get; }

        public bool Succeeded => Status == ExchangeExecutionStatus.Success;
    }

    public readonly struct ExchangeRelationshipRequirement
    {
        public ExchangeRelationshipRequirement(
            RoleSlot source,
            RoleSlot target,
            int typeId,
            int metricId,
            short minimumMetric,
            short maximumMetric,
            int flagId,
            bool requiredFlagValue)
        {
            Source = source;
            Target = target;
            TypeId = typeId;
            MetricId = metricId;
            MetricComparison = ExchangeRelationshipMetricComparison.RangeInclusive;
            MinimumMetric = minimumMetric;
            MaximumMetric = maximumMetric;
            FlagId = flagId;
            HasFlagRequirement = flagId >= 0;
            RequiredFlagValue = requiredFlagValue;
        }

        public ExchangeRelationshipRequirement(
            RoleSlot source,
            RoleSlot target,
            int typeId,
            int metricId,
            short threshold,
            ExchangeRelationshipMetricComparison metricComparison,
            int flagId,
            bool requiredFlagValue)
        {
            Source = source;
            Target = target;
            TypeId = typeId;
            MetricId = metricId;
            MetricComparison = metricComparison;
            MinimumMetric = threshold;
            MaximumMetric = threshold;
            FlagId = flagId;
            HasFlagRequirement = flagId >= 0;
            RequiredFlagValue = requiredFlagValue;
        }

        public ExchangeRelationshipRequirement(
            RoleSlot source,
            RoleSlot target,
            int typeId,
            int metricId,
            short? minimumMetric,
            short? maximumMetric,
            int flagId,
            bool requiredFlagValue)
        {
            Source = source;
            Target = target;
            TypeId = typeId;
            MetricId = metricId;
            if (minimumMetric.HasValue && maximumMetric.HasValue)
            {
                MetricComparison = ExchangeRelationshipMetricComparison.RangeInclusive;
                MinimumMetric = minimumMetric.Value;
                MaximumMetric = maximumMetric.Value;
            }
            else if (minimumMetric.HasValue)
            {
                MetricComparison = ExchangeRelationshipMetricComparison.GreaterOrEqual;
                MinimumMetric = minimumMetric.Value;
                MaximumMetric = minimumMetric.Value;
            }
            else if (maximumMetric.HasValue)
            {
                MetricComparison = ExchangeRelationshipMetricComparison.LessOrEqual;
                MinimumMetric = maximumMetric.Value;
                MaximumMetric = maximumMetric.Value;
            }
            else
            {
                MetricComparison = ExchangeRelationshipMetricComparison.None;
                MinimumMetric = 0;
                MaximumMetric = 0;
            }

            FlagId = flagId;
            HasFlagRequirement = flagId >= 0;
            RequiredFlagValue = requiredFlagValue;
        }

        public RoleSlot Source { get; }

        public RoleSlot Target { get; }

        public int TypeId { get; }

        public int MetricId { get; }

        public ExchangeRelationshipMetricComparison MetricComparison { get; }

        public short MinimumMetric { get; }

        public short MaximumMetric { get; }

        public int FlagId { get; }

        public bool HasFlagRequirement { get; }

        public bool RequiredFlagValue { get; }
    }

    public readonly struct ExchangeInputDefinition
    {
        public ExchangeInputDefinition(ExchangeInputKind kind, RoleSlot actor, int itemDefinitionId, int quantity)
            : this(kind, actor, itemDefinitionId, attributeId: -1, quantity)
        {
        }

        public ExchangeInputDefinition(ExchangeInputKind kind, RoleSlot actor, int itemDefinitionId, int attributeId, int quantity)
        {
            Kind = kind;
            Actor = actor;
            ItemDefinitionId = itemDefinitionId;
            AttributeId = attributeId;
            Quantity = quantity;
        }

        public ExchangeInputKind Kind { get; }

        public RoleSlot Actor { get; }

        public int ItemDefinitionId { get; }

        public int AttributeId { get; }

        public int Quantity { get; }

        public static ExchangeInputDefinition AttributeCost(RoleSlot actor, int attributeId, int quantity)
        {
            return new ExchangeInputDefinition(ExchangeInputKind.AttributeCost, actor, itemDefinitionId: 0, attributeId, quantity);
        }
    }

    public readonly struct ExchangeOutputDefinition
    {
        public ExchangeOutputDefinition(
            ExchangeOutputKind kind,
            RoleSlot actor,
            ItemContainerPurpose purpose,
            int itemDefinitionId,
            int quantity,
            int charges,
            int durability,
            RoleSlot fromActor,
            ItemContainerPurpose fromPurpose,
            int effectTemplateId,
            RoleSlot effectSource,
            RoleSlot effectTarget,
            RoleSlot effectContext)
        {
            Kind = kind;
            Actor = actor;
            Purpose = purpose;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
            Charges = charges;
            Durability = durability;
            FromActor = fromActor;
            FromPurpose = fromPurpose;
            EffectTemplateId = effectTemplateId;
            EffectSource = effectSource;
            EffectTarget = effectTarget;
            EffectContext = effectContext;
        }

        public ExchangeOutputKind Kind { get; }

        public RoleSlot Actor { get; }

        public ItemContainerPurpose Purpose { get; }

        public int ItemDefinitionId { get; }

        public int Quantity { get; }

        public int Charges { get; }

        public int Durability { get; }

        public RoleSlot FromActor { get; }

        public ItemContainerPurpose FromPurpose { get; }

        public int EffectTemplateId { get; }

        public RoleSlot EffectSource { get; }

        public RoleSlot EffectTarget { get; }

        public RoleSlot EffectContext { get; }
    }

    public sealed class ExchangeOperationDefinition
    {
        public string Id { get; init; } = string.Empty;

        public ExchangeRelationshipRequirement[] RelationshipRequirements { get; init; } = System.Array.Empty<ExchangeRelationshipRequirement>();

        public ExchangeInputDefinition[] Inputs { get; init; } = System.Array.Empty<ExchangeInputDefinition>();

        public ExchangeOutputDefinition[] Outputs { get; init; } = System.Array.Empty<ExchangeOutputDefinition>();
    }
}
