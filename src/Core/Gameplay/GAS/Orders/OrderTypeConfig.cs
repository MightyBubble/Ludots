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

    public sealed class OrderTypeConfig
    {
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
        public int IntArg0BlackboardKey { get; set; } = -1;
        public int ValidationGraphId { get; set; }
    }
}
