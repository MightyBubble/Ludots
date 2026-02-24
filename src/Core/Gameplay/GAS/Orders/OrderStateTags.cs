namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// OrderState Tag ID constants.
    /// Tag IDs 100-127 are reserved for Order-related tags.
    /// TagRegistry.Register() automatically skips this range during dynamic allocation
    /// (see TagRegistry.ReservedRangeStart / ReservedRangeEnd).
    /// </summary>
    public static class OrderStateTags
    {
        // Order.Active.* (100-109): Current executing order type
        public const int Active_CastAbility = 100;
        public const int Active_AttackTarget = 102;
        public const int Active_Stop = 103;
        public const int Active_Reserved3 = 101;
        public const int Active_Reserved4 = 104;
        public const int Active_Reserved5 = 105;
        public const int Active_Reserved6 = 106;
        public const int Active_Reserved7 = 107;
        public const int Active_Reserved8 = 108;
        public const int Active_Reserved9 = 109;
        
        // Order.State.* (110-119): Generic order states
        public const int State_HasActive = 110;
        public const int State_HasQueued = 111;
        public const int State_Channeling = 112;
        public const int State_Casting = 113;
        public const int State_Interruptible = 114;
        public const int State_Reserved5 = 115;
        public const int State_Reserved6 = 116;
        public const int State_Reserved7 = 117;
        public const int State_Reserved8 = 118;
        public const int State_Reserved9 = 119;
        
        // Reserved range (120-127) for future use
        public const int Reserved_Start = 120;
        public const int Reserved_End = 127;
        
        /// <summary>
        /// Check if a tag ID is in the Order.Active.* range.
        /// </summary>
        public static bool IsActiveOrderTag(int tagId) => tagId >= 100 && tagId <= 109;
        
        /// <summary>
        /// Check if a tag ID is in the Order.State.* range.
        /// </summary>
        public static bool IsOrderStateTag(int tagId) => tagId >= 110 && tagId <= 119;
        
        /// <summary>
        /// Check if a tag ID is reserved for Order system.
        /// </summary>
        public static bool IsOrderTag(int tagId) => tagId >= 100 && tagId <= 127;
    }
}
