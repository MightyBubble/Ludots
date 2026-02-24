using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Reserved config parameter keys owned by the effect system.
    /// All keys use the "_ep." prefix to avoid collision with user-defined keys.
    /// Resolved to int IDs at initialization via <see cref="ConfigKeyRegistry"/>.
    /// Grouped by parameter component.
    /// </summary>
    public static class EffectParamKeys
    {
        // ── DurationParams ──
        public static int DurationTicks;
        public static int PeriodTicks;

        // ── ForceParams ──
        /// <summary>Force value for X axis (float, caller-supplied).</summary>
        public static int ForceXAttribute;
        /// <summary>Force value for Y axis (float, caller-supplied).</summary>
        public static int ForceYAttribute;
        /// <summary>Target attribute ID for X force (resolved from attribute name at load time).</summary>
        public static int ForceXTargetAttrId;
        /// <summary>Target attribute ID for Y force (resolved from attribute name at load time).</summary>
        public static int ForceYTargetAttrId;

        // ── TargetQueryParams ──
        public static int QueryRadius;
        public static int QueryInnerRadius;
        public static int QueryHalfAngle;
        public static int QueryHalfWidth;
        public static int QueryHalfHeight;
        public static int QueryLength;
        public static int QueryRotation;
        public static int QueryMaxTargets;

        // ── TargetDispatchParams ──
        public static int PayloadEffectId;

        // ── ProjectileParams ──
        public static int ProjectileSpeed;
        public static int ProjectileRange;
        public static int ProjectileArcHeight;
        public static int ImpactEffectId;

        // ── UnitCreationParams ──
        public static int UnitTypeId;
        public static int UnitCount;
        public static int UnitOffsetRadius;
        public static int OnSpawnEffectId;

        /// <summary>
        /// Register all _ep.* keys with the ConfigKeyRegistry.
        /// Must be called once during GasController initialization,
        /// before any EffectTemplate loading.
        /// </summary>
        public static void Initialize()
        {
            // DurationParams
            DurationTicks = ConfigKeyRegistry.Register("_ep.durationTicks");
            PeriodTicks = ConfigKeyRegistry.Register("_ep.periodTicks");

            // ForceParams
            ForceXAttribute = ConfigKeyRegistry.Register("_ep.forceXAttribute");
            ForceYAttribute = ConfigKeyRegistry.Register("_ep.forceYAttribute");
            ForceXTargetAttrId = ConfigKeyRegistry.Register("_ep.forceXTargetAttrId");
            ForceYTargetAttrId = ConfigKeyRegistry.Register("_ep.forceYTargetAttrId");

            // TargetQueryParams
            QueryRadius = ConfigKeyRegistry.Register("_ep.queryRadius");
            QueryInnerRadius = ConfigKeyRegistry.Register("_ep.queryInnerRadius");
            QueryHalfAngle = ConfigKeyRegistry.Register("_ep.queryHalfAngle");
            QueryHalfWidth = ConfigKeyRegistry.Register("_ep.queryHalfWidth");
            QueryHalfHeight = ConfigKeyRegistry.Register("_ep.queryHalfHeight");
            QueryLength = ConfigKeyRegistry.Register("_ep.queryLength");
            QueryRotation = ConfigKeyRegistry.Register("_ep.queryRotation");
            QueryMaxTargets = ConfigKeyRegistry.Register("_ep.queryMaxTargets");

            // TargetDispatchParams
            PayloadEffectId = ConfigKeyRegistry.Register("_ep.payloadEffectId");

            // ProjectileParams
            ProjectileSpeed = ConfigKeyRegistry.Register("_ep.projectileSpeed");
            ProjectileRange = ConfigKeyRegistry.Register("_ep.projectileRange");
            ProjectileArcHeight = ConfigKeyRegistry.Register("_ep.projectileArcHeight");
            ImpactEffectId = ConfigKeyRegistry.Register("_ep.impactEffectId");

            // UnitCreationParams
            UnitTypeId = ConfigKeyRegistry.Register("_ep.unitTypeId");
            UnitCount = ConfigKeyRegistry.Register("_ep.unitCount");
            UnitOffsetRadius = ConfigKeyRegistry.Register("_ep.unitOffsetRadius");
            OnSpawnEffectId = ConfigKeyRegistry.Register("_ep.onSpawnEffectId");
        }
    }
}
