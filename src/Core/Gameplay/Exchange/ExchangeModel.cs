using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Items;

namespace Ludots.Core.Gameplay.Exchange
{
    public enum ExchangeInputKind : byte
    {
        None = 0,
        ItemStack = 1
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
        ExecutionFailed = 7
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

    public readonly struct ExchangeInputDefinition
    {
        public ExchangeInputDefinition(ExchangeInputKind kind, RoleSlot actor, int itemDefinitionId, int quantity)
        {
            Kind = kind;
            Actor = actor;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
        }

        public ExchangeInputKind Kind { get; }

        public RoleSlot Actor { get; }

        public int ItemDefinitionId { get; }

        public int Quantity { get; }
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

        public ExchangeInputDefinition[] Inputs { get; init; } = System.Array.Empty<ExchangeInputDefinition>();

        public ExchangeOutputDefinition[] Outputs { get; init; } = System.Array.Empty<ExchangeOutputDefinition>();
    }
}
