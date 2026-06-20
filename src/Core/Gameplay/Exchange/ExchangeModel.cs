using Arch.Core;
using Ludots.Core.Gameplay.Items;

namespace Ludots.Core.Gameplay.Exchange
{
    public enum ExchangeActorSlot : byte
    {
        None = 0,
        Source = 1,
        Target = 2,
        Context = 3
    }

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
        public ExchangeExecutionContext(Entity source, Entity target = default, Entity context = default, int scopeKey = 0)
        {
            Source = source;
            Target = target;
            Context = context;
            ScopeKey = scopeKey;
        }

        public Entity Source { get; }

        public Entity Target { get; }

        public Entity Context { get; }

        public int ScopeKey { get; }

        public Entity Resolve(ExchangeActorSlot slot)
        {
            return slot switch
            {
                ExchangeActorSlot.Source => Source,
                ExchangeActorSlot.Target => Target,
                ExchangeActorSlot.Context => Context,
                _ => Entity.Null
            };
        }
    }

    public readonly struct ExchangeOperationKey
    {
        public ExchangeOperationKey(int operationId, int scopeKey = 0)
        {
            OperationId = operationId;
            ScopeKey = scopeKey;
        }

        public int OperationId { get; }

        public int ScopeKey { get; }

        public bool HasScope => ScopeKey > 0;
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
        public ExchangeInputDefinition(ExchangeInputKind kind, ExchangeActorSlot actor, int itemDefinitionId, int quantity)
        {
            Kind = kind;
            Actor = actor;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
        }

        public ExchangeInputKind Kind { get; }

        public ExchangeActorSlot Actor { get; }

        public int ItemDefinitionId { get; }

        public int Quantity { get; }
    }

    public readonly struct ExchangeOutputDefinition
    {
        public ExchangeOutputDefinition(
            ExchangeOutputKind kind,
            ExchangeActorSlot actor,
            ItemContainerPurpose purpose,
            int itemDefinitionId,
            int quantity,
            int charges,
            int durability,
            ExchangeActorSlot fromActor,
            ItemContainerPurpose fromPurpose,
            int effectTemplateId,
            ExchangeActorSlot effectSource,
            ExchangeActorSlot effectTarget,
            ExchangeActorSlot effectContext)
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

        public ExchangeActorSlot Actor { get; }

        public ItemContainerPurpose Purpose { get; }

        public int ItemDefinitionId { get; }

        public int Quantity { get; }

        public int Charges { get; }

        public int Durability { get; }

        public ExchangeActorSlot FromActor { get; }

        public ItemContainerPurpose FromPurpose { get; }

        public int EffectTemplateId { get; }

        public ExchangeActorSlot EffectSource { get; }

        public ExchangeActorSlot EffectTarget { get; }

        public ExchangeActorSlot EffectContext { get; }
    }

    public sealed class ExchangeOperationDefinition
    {
        public string Id { get; init; } = string.Empty;

        public ExchangeInputDefinition[] Inputs { get; init; } = System.Array.Empty<ExchangeInputDefinition>();

        public ExchangeOutputDefinition[] Outputs { get; init; } = System.Array.Empty<ExchangeOutputDefinition>();
    }
}
