using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Concrete implementations of all builtin phase handlers.
    /// Each method matches the <see cref="BuiltinHandlerFn"/> delegate signature.
    /// Registered once at startup via <see cref="RegisterAll"/>.
    /// </summary>
    public static class BuiltinHandlers
    {
        /// <summary>
        /// Register all builtin handlers into the given registry.
        /// Call once during GAS initialization.
        /// </summary>
        public static void RegisterAll(BuiltinHandlerRegistry registry)
        {
            registry.Register(BuiltinHandlerId.ApplyModifiers, HandleApplyModifiers);
            registry.Register(BuiltinHandlerId.ApplyForce, HandleApplyForce);
            registry.Register(BuiltinHandlerId.SpatialQuery, HandleSpatialQuery);
            registry.Register(BuiltinHandlerId.DispatchPayload, HandleDispatchPayload);
            registry.Register(BuiltinHandlerId.ReResolveAndDispatch, HandleReResolveAndDispatch);
            registry.Register(BuiltinHandlerId.CreateProjectile, HandleCreateProjectile);
            registry.Register(BuiltinHandlerId.CreateUnit, HandleCreateUnit);
        }

        // ══════════════════════════════════════════════════════════════
        //  1. ApplyModifiers — read template modifiers, apply to target
        // ══════════════════════════════════════════════════════════════

        public static void HandleApplyModifiers(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;
            if (!world.Has<AttributeBuffer>(context.Target)) return;

            ref var attrBuffer = ref world.Get<AttributeBuffer>(context.Target);
            var modifiers = templateData.Modifiers;
            EffectModifierOps.Apply(in modifiers, ref attrBuffer);
        }

        // ══════════════════════════════════════════════════════════════
        //  2. ApplyForce — read force X/Y from merged params, apply
        // ══════════════════════════════════════════════════════════════

        public static void HandleApplyForce(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;
            if (!world.Has<AttributeBuffer>(context.Target)) return;

            mergedParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx);
            mergedParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy);

            ref var attrBuffer = ref world.Get<AttributeBuffer>(context.Target);
            if (templateData.PresetAttribute0 > 0)
                attrBuffer.SetCurrent(templateData.PresetAttribute0, attrBuffer.GetCurrent(templateData.PresetAttribute0) + fx);
            if (templateData.PresetAttribute1 > 0)
                attrBuffer.SetCurrent(templateData.PresetAttribute1, attrBuffer.GetCurrent(templateData.PresetAttribute1) + fy);
        }

        // ══════════════════════════════════════════════════════════════
        //  3. SpatialQuery — execute spatial query, populate EffectContext target list
        // ══════════════════════════════════════════════════════════════

        public static void HandleSpatialQuery(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            // SpatialQuery requires ISpatialQueryService which is not available via this delegate.
            // The existing TargetResolverFanOutHelper.ResolveTargets handles this flow.
            // This handler serves as the explicit registration point; actual spatial query
            // execution is delegated through the EffectApplicationSystem which has the service reference.
            //
            // In the future, ISpatialQueryService could be injected via a closure or context object.
            // For now, this handler is a no-op marker — the application system checks for 
            // HasTargetResolver and calls TargetResolverFanOutHelper directly.
        }

        // ══════════════════════════════════════════════════════════════
        //  4. DispatchPayload — dispatch payload effect to each resolved target
        // ══════════════════════════════════════════════════════════════

        public static void HandleDispatchPayload(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            // Similar to SpatialQuery — payload dispatch requires EffectRequestQueue
            // which is not available via the handler delegate.
            // The actual dispatch is handled by EffectApplicationSystem's fan-out stage.
            // This handler serves as the explicit registration point for the preset type system.
        }

        // ══════════════════════════════════════════════════════════════
        //  5. ReResolveAndDispatch — periodic: re-query + dispatch
        // ══════════════════════════════════════════════════════════════

        public static void HandleReResolveAndDispatch(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            // Periodic re-resolve is handled by EffectLifetimeSystem's ProcessPeriodFanOut.
            // This handler is the explicit registration point for the preset type system.
        }

        // ══════════════════════════════════════════════════════════════
        //  6. CreateProjectile — read ProjectileParams, create entity
        // ══════════════════════════════════════════════════════════════

        public static void HandleCreateProjectile(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            ref readonly var proj = ref templateData.Projectile;
            if (proj.Speed <= 0) return;

            // Create a projectile entity with the configured parameters.
            // The projectile carries its ImpactEffectTemplateId for dispatch on collision.
            Entity projectile = world.Create(
                new ProjectileState
                {
                    Speed = proj.Speed,
                    Range = proj.Range,
                    ArcHeight = proj.ArcHeight,
                    ImpactEffectTemplateId = proj.ImpactEffectTemplateId,
                    Source = context.Source,
                    Target = context.Target,
                }
            );
            if (world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source))
            {
                var pos = world.Get<WorldPositionCm>(context.Source);
                world.Add(projectile, pos);
                world.Add(projectile, new PreviousWorldPositionCm { Value = pos.Value });
            }
            _ = projectile; // Created; movement system picks it up
        }

        // ══════════════════════════════════════════════════════════════
        //  7. CreateUnit — read UnitCreationParams, create entity/entities
        // ══════════════════════════════════════════════════════════════

        public static void HandleCreateUnit(
            World world, Entity effectEntity,
            ref EffectContext context, in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            ref readonly var unit = ref templateData.UnitCreation;
            if (unit.Count <= 0) return;

            for (int i = 0; i < unit.Count; i++)
            {
                var spawned = world.Create(
                    new SpawnedUnitState
                    {
                        UnitTypeId = unit.UnitTypeId,
                        OffsetRadius = unit.OffsetRadius,
                        OnSpawnEffectTemplateId = unit.OnSpawnEffectTemplateId,
                        Spawner = context.Source,
                    }
                );
                _ = spawned; // Created; spawn system picks it up
            }
        }
    }

    // ── Marker components for projectile and unit creation ──

    /// <summary>
    /// Component placed on newly created projectile entities by the CreateProjectile handler.
    /// The projectile movement system reads this to drive flight, and on impact dispatches the ImpactEffectTemplateId.
    /// </summary>
    public struct ProjectileState
    {
        public int Speed;
        public int Range;
        public int ArcHeight;
        public int ImpactEffectTemplateId;
        public Entity Source;
        public Entity Target;
        public int TraveledCm;
    }

    /// <summary>
    /// Component placed on newly created unit entities by the CreateUnit handler.
    /// The spawn system reads this to finalize unit initialization.
    /// </summary>
    public struct SpawnedUnitState
    {
        public int UnitTypeId;
        public int OffsetRadius;
        public int OnSpawnEffectTemplateId;
        public Entity Spawner;
    }
}
