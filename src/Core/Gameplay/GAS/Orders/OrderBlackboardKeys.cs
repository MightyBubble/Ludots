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
        
        // ========== Rally Point (160-169) ==========

        /// <summary>
        /// Rally target kind (BlackboardIntBuffer). Values from <see cref="RallyTargetKind"/>.
        /// </summary>
        public const int Rally_TargetKind = 160;

        /// <summary>
        /// Rally world position for point / cached hex resolution (BlackboardSpatialBuffer).
        /// </summary>
        public const int Rally_TargetPosition = 161;

        /// <summary>
        /// Rally target entity for garrison / entity-target orders (BlackboardEntityBuffer).
        /// </summary>
        public const int Rally_TargetEntity = 162;

        /// <summary>
        /// Rally hex axial Q (BlackboardIntBuffer).
        /// </summary>
        public const int Rally_HexQ = 163;

        /// <summary>
        /// Rally hex axial R (BlackboardIntBuffer).
        /// </summary>
        public const int Rally_HexR = 164;

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

        private static readonly OrderBlackboardKeyDefinition[] Builtins =
        {
            new("Cast.SlotIndex", Cast_SlotIndex),
            new("Cast.TargetEntity", Cast_TargetEntity),
            new("Cast.TargetPosition", Cast_TargetPosition),
            new("Cast.AbilityId", Cast_AbilityId),
            new("Attack.TargetEntity", Attack_TargetEntity),
            new("Attack.MovePosition", Attack_MovePosition),
            new("Attack.IsAttackMove", Attack_IsAttackMove),
            new("Stop.Type", Stop_Type),
            new("Hold.Active", Hold_Active),
            new("Rally.TargetKind", Rally_TargetKind),
            new("Rally.TargetPosition", Rally_TargetPosition),
            new("Rally.TargetEntity", Rally_TargetEntity),
            new("Rally.HexQ", Rally_HexQ),
            new("Rally.HexR", Rally_HexR),
            new("Patrol.Waypoints", Patrol_Waypoints),
            new("Patrol.CurrentIndex", Patrol_CurrentIndex),
            new("Patrol.Direction", Patrol_Direction),
            new("Generic.TargetEntity", Generic_TargetEntity),
            new("Generic.TargetPosition", Generic_TargetPosition),
            new("Generic.IntParam", Generic_IntParam),
            new("Generic.FloatParam", Generic_FloatParam),
        };

        public static ReadOnlySpan<OrderBlackboardKeyDefinition> BuiltinDefinitions => Builtins;
    }
}
