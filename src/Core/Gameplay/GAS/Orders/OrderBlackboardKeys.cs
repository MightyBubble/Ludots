namespace Ludots.Core.Gameplay.GAS.Orders
{
    public readonly struct OrderBlackboardKeyDefinition
    {
        public OrderBlackboardKeyDefinition(string key, int id)
        {
            Key = key;
            Id = id;
        }

        public string Key { get; }
        public int Id { get; }
    }

    /// <summary>
    /// Blackboard key constants for Order-related data.
    /// Order execution systems read/write these keys to get/set parameters.
    /// </summary>
    public static class OrderBlackboardKeys
    {
        // ========== Cast Ability Order (110-119) ==========
        
        /// <summary>
        /// Ability slot index (BlackboardIntBuffer).
        /// </summary>
        public const int Cast_SlotIndex = 110;
        
        /// <summary>
        /// Target entity for the ability (BlackboardEntityBuffer).
        /// The primary target of the ability.
        /// </summary>
        public const int Cast_TargetEntity = 111;
        
        /// <summary>
        /// Target position for the ability (BlackboardSpatialBuffer).
        /// For ground-targeted abilities.
        /// </summary>
        public const int Cast_TargetPosition = 112;
        
        /// <summary>
        /// Ability ID override (BlackboardIntBuffer).
        /// If set, use this ability ID instead of the slot's default.
        /// </summary>
        public const int Cast_AbilityId = 113;

        /// <summary>
        /// Facing angle for the ability in positive degrees (BlackboardFloatBuffer).
        /// Used by facing-sector queries when a cast has an explicit authored aim direction.
        /// </summary>
        public const int Cast_Facing = 114;
        
        // ========== Generic/Shared (200-255) ==========
        
        /// <summary>
        /// Generic target entity (BlackboardEntityBuffer).
        /// For orders that just need a single target.
        /// </summary>
        public const int Generic_TargetEntity = 200;
        
        /// <summary>
        /// Generic target position (BlackboardSpatialBuffer).
        /// For orders that just need a position.
        /// </summary>
        public const int Generic_TargetPosition = 201;
        
        /// <summary>
        /// Generic integer parameter (BlackboardIntBuffer).
        /// </summary>
        public const int Generic_IntParam = 202;
        
        /// <summary>
        /// Generic float parameter (BlackboardFloatBuffer).
        /// </summary>
        public const int Generic_FloatParam = 203;

        private static readonly OrderBlackboardKeyDefinition[] Builtins =
        {
            new("Cast.SlotIndex", Cast_SlotIndex),
            new("Cast.TargetEntity", Cast_TargetEntity),
            new("Cast.TargetPosition", Cast_TargetPosition),
            new("Cast.AbilityId", Cast_AbilityId),
            new("Cast.Facing", Cast_Facing),
            new("Generic.TargetEntity", Generic_TargetEntity),
            new("Generic.TargetPosition", Generic_TargetPosition),
            new("Generic.IntParam", Generic_IntParam),
            new("Generic.FloatParam", Generic_FloatParam),
        };

        public static ReadOnlySpan<OrderBlackboardKeyDefinition> BuiltinDefinitions => Builtins;
    }
}
