namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// All registered builtin C# phase handlers.
    /// Each ID maps to a concrete C# function registered in <see cref="BuiltinHandlerRegistry"/>.
    /// Numbering uses ranges for logical grouping.
    /// </summary>
    public enum BuiltinHandlerId : int
    {
        None = 0,

        // ── Generic modifier application ──
        /// <summary>Read ModifierParams from merged ConfigParams, apply attribute modifiers to target.</summary>
        ApplyModifiers = 1,

        // ── Spatial search + dispatch ──
        /// <summary>Read TargetQueryParams + TargetFilterParams, execute spatial query, populate TargetList.</summary>
        SpatialQuery = 10,
        /// <summary>Read TargetDispatchParams, dispatch payload effect to each target in TargetList.</summary>
        DispatchPayload = 11,
        /// <summary>OnPeriod: re-run SpatialQuery + DispatchPayload for periodic search effects.</summary>
        ReResolveAndDispatch = 12,

        // ── Physics ──
        /// <summary>Read ForceParams (_ep.forceX/Y), apply 2D force to target.</summary>
        ApplyForce = 20,

        // ── Entity creation ──
        /// <summary>Read ProjectileParams, create projectile entity via EntityBuilder, attach ImpactEffectRef.</summary>
        CreateProjectile = 30,
        /// <summary>Read UnitCreationParams, create unit entity/entities via EntityBuilder.</summary>
        CreateUnit = 31,

        // ── Displacement ──
        /// <summary>Read DisplacementParams, create displacement state entity.</summary>
        ApplyDisplacement = 40,
    }
}
