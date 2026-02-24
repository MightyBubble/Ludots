namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Blackboard key constants for Order-related data.
    /// Order execution systems read/write these keys to get/set parameters.
    /// </summary>
    public static class OrderBlackboardKeys
    {
        // ========== Cast Ability Order (110-119) ==========
        
        /// <summary>
        /// Ability slot index (BlackboardIntBuffer).
        /// Which ability slot to cast (0=Q, 1=W, 2=E, 3=R).
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
        
        // ========== Attack Order (120-129) ==========
        
        /// <summary>
        /// Primary attack target (BlackboardEntityBuffer).
        /// The entity being attacked.
        /// </summary>
        public const int Attack_TargetEntity = 120;
        
        /// <summary>
        /// Attack position (BlackboardSpatialBuffer).
        /// For attack-move commands (attack while moving to position).
        /// </summary>
        public const int Attack_MovePosition = 121;
        
        /// <summary>
        /// Attack-move flag (BlackboardIntBuffer).
        /// If non-zero, this is an attack-move command.
        /// </summary>
        public const int Attack_IsAttackMove = 122;
        
        // ========== Stop Order (130-139) ==========
        
        /// <summary>
        /// Stop type (BlackboardIntBuffer).
        /// 0 = stop current only, 1 = stop and clear queue.
        /// </summary>
        public const int Stop_Type = 130;
        
        // ========== Hold Position Order (140-149) ==========
        
        /// <summary>
        /// Hold position flag (BlackboardIntBuffer).
        /// If non-zero, entity should not auto-move.
        /// </summary>
        public const int Hold_Active = 140;
        
        // ========== Patrol Order (150-159) ==========
        
        /// <summary>
        /// Patrol waypoints (BlackboardSpatialBuffer).
        /// Points to patrol between.
        /// </summary>
        public const int Patrol_Waypoints = 150;
        
        /// <summary>
        /// Current patrol index (BlackboardIntBuffer).
        /// Which patrol point we're heading to.
        /// </summary>
        public const int Patrol_CurrentIndex = 151;
        
        /// <summary>
        /// Patrol direction (BlackboardIntBuffer).
        /// 1 = forward through points, -1 = backward.
        /// </summary>
        public const int Patrol_Direction = 152;
        
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
    }
}
