using System.Text.Json.Serialization;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public enum SameTypePolicy
    {
        Queue = 0,
        Replace = 1,
        Ignore = 2
    }

    public enum QueueFullPolicy
    {
        DropOldest = 0,
        RejectNew = 1
    }

    public enum OrderPayloadKind : byte
    {
        None = 0,
        CastAbility = 1,
        MoveToWorldCm = 2,
        Stop = 3,
        TargetEntity = 4
    }

    public sealed class OrderTypeConfig
    {
        public string Key { get; set; } = string.Empty;
        public int OrderTypeId { get; set; }
        public string Label { get; set; } = string.Empty;
        public int MaxQueueSize { get; set; } = 3;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SameTypePolicy SameTypePolicy { get; set; } = SameTypePolicy.Queue;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public QueueFullPolicy QueueFullPolicy { get; set; } = QueueFullPolicy.DropOldest;

        public int Priority { get; set; } = 100;
        public int BufferWindowMs { get; set; } = 500;
        public int PendingBufferWindowMs { get; set; } = 400;
        public bool CanInterruptSelf { get; set; }
        public int QueuedModeMaxSize { get; set; } = 16;
        public bool AllowQueuedMode { get; set; } = true;
        public bool ClearQueueOnActivate { get; set; } = true;
        public int SpatialBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetPosition;
        public int EntityBlackboardKey { get; set; } = OrderBlackboardKeys.Generic_TargetEntity;
        public int IntArg0BlackboardKey { get; private set; } = -1;
        public OrderPayloadKind PayloadKind { get; set; } = OrderPayloadKind.None;
        public int ValidationGraphId { get; set; }
        public bool InstantComplete { get; set; }
        public BlackboardStoredTargetKeys PersistentStoredTargetKeys { get; set; }

        public OrderTypeConfig UseCastAbilityPayload(
            int abilitySlotBlackboardKey = OrderBlackboardKeys.Cast_SlotIndex)
        {
            CompileRuntimePayload(OrderPayloadKind.CastAbility, abilitySlotBlackboardKey);
            return this;
        }

        public OrderTypeConfig UseMoveToWorldCmPayload()
        {
            CompileRuntimePayload(OrderPayloadKind.MoveToWorldCm, -1);
            return this;
        }

        public OrderTypeConfig UseStopPayload()
        {
            CompileRuntimePayload(OrderPayloadKind.Stop, -1);
            return this;
        }

        public OrderTypeConfig UseTargetEntityPayload()
        {
            CompileRuntimePayload(OrderPayloadKind.TargetEntity, -1);
            return this;
        }

        public OrderTypeConfig UseNoPayload()
        {
            CompileRuntimePayload(OrderPayloadKind.None, -1);
            return this;
        }

        internal void CompileRuntimePayload(OrderPayloadKind payloadKind, int intArg0BlackboardKey)
        {
            if (payloadKind == OrderPayloadKind.CastAbility)
            {
                if (intArg0BlackboardKey < 0)
                {
                    throw new System.InvalidOperationException(
                        "OrderTypeConfig CastAbility payload requires a compiled ability slot blackboard key.");
                }
            }
            else if (intArg0BlackboardKey >= 0)
            {
                throw new System.InvalidOperationException(
                    $"OrderTypeConfig payloadKind {payloadKind} must not compile an IntArg0 blackboard key.");
            }

            PayloadKind = payloadKind;
            IntArg0BlackboardKey = intArg0BlackboardKey;
        }
    }
}
