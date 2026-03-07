using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Concrete implementations of builtin phase handlers.
    /// Registered once at startup and executed through <see cref="Systems.EffectPhaseExecutor"/>.
    /// </summary>
    public static class BuiltinHandlers
    {
        public static void RegisterAll(BuiltinHandlerRegistry registry)
        {
            registry.Register(BuiltinHandlerId.ApplyModifiers, HandleApplyModifiers);
            registry.Register(BuiltinHandlerId.ApplyForce, HandleApplyForce);
            registry.Register(BuiltinHandlerId.SpatialQuery, HandleSpatialQuery);
            registry.Register(BuiltinHandlerId.DispatchPayload, HandleDispatchPayload);
            registry.Register(BuiltinHandlerId.ReResolveAndDispatch, HandleReResolveAndDispatch);
            registry.Register(BuiltinHandlerId.CreateProjectile, HandleCreateProjectile);
            registry.Register(BuiltinHandlerId.CreateUnit, HandleCreateUnit);
            registry.Register(BuiltinHandlerId.ApplyDisplacement, HandleApplyDisplacement);
        }

        public static void HandleApplyModifiers(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;
            if (!world.Has<AttributeBuffer>(context.Target)) return;

            ref var attrBuffer = ref world.Get<AttributeBuffer>(context.Target);
            var modifiers = templateData.Modifiers;
            EffectModifierOps.Apply(in modifiers, ref attrBuffer);
        }

        public static void HandleApplyForce(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
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

        public static void HandleSpatialQuery(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpatialQueries == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = TargetResolverFanOutHelper.ResolveTargets(
                world,
                in context,
                in templateData.TargetQuery,
                runtime.SpatialQueries,
                runtime.ResolverBuffer);

            runtime.SetResolvedCandidateCount(candidateCount);
        }

        public static void HandleDispatchPayload(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.FanOutBudget == null || runtime.FanOutCommands == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = runtime.ResolvedCandidateCount;
            if (candidateCount <= 0)
            {
                return;
            }

            int dropped = 0;
            TargetResolverFanOutHelper.ValidateAndCollect(
                world,
                in context,
                in templateData.TargetQuery,
                in templateData.TargetFilter,
                in templateData.TargetDispatch,
                runtime.ResolverBuffer,
                candidateCount,
                runtime.FanOutBudget,
                runtime.FanOutCommands,
                ref dropped);

            runtime.AddDropped(dropped);
            runtime.ClearResolvedCandidates();
        }

        public static void HandleReResolveAndDispatch(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpatialQueries == null || runtime.FanOutBudget == null || runtime.FanOutCommands == null || runtime.ResolverBuffer == null)
            {
                return;
            }

            int candidateCount = TargetResolverFanOutHelper.ResolveTargets(
                world,
                in context,
                in templateData.TargetQuery,
                runtime.SpatialQueries,
                runtime.ResolverBuffer);
            if (candidateCount <= 0)
            {
                runtime.ClearResolvedCandidates();
                return;
            }

            int dropped = 0;
            TargetResolverFanOutHelper.ValidateAndCollect(
                world,
                in context,
                in templateData.TargetQuery,
                in templateData.TargetFilter,
                in templateData.TargetDispatch,
                runtime.ResolverBuffer,
                candidateCount,
                runtime.FanOutBudget,
                runtime.FanOutCommands,
                ref dropped);

            runtime.AddDropped(dropped);
            runtime.ClearResolvedCandidates();
        }

        public static void HandleCreateProjectile(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            ref readonly var proj = ref templateData.Projectile;
            if (proj.Speed <= 0) return;

            Entity projectile = EntityCreationHelper.CreateProjectile(world,
                new ProjectileState
                {
                    Speed = Fix64.FromInt(proj.Speed),
                    Range = proj.Range,
                    ArcHeight = proj.ArcHeight,
                    ImpactEffectTemplateId = proj.ImpactEffectTemplateId,
                    Source = context.Source,
                    Target = context.Target,
                });
            if (world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source))
            {
                var pos = world.Get<WorldPositionCm>(context.Source);
                world.Add(projectile, pos);
                world.Add(projectile, new PreviousWorldPositionCm { Value = pos.Value });
            }
        }

        public static void HandleCreateUnit(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Source)) return;

            ref readonly var unit = ref templateData.UnitCreation;
            if (unit.Count <= 0) return;

            for (int i = 0; i < unit.Count; i++)
            {
                EntityCreationHelper.CreateSpawnedUnit(world,
                    new SpawnedUnitState
                    {
                        UnitTypeId = unit.UnitTypeId,
                        OffsetRadius = unit.OffsetRadius,
                        OnSpawnEffectTemplateId = unit.OnSpawnEffectTemplateId,
                        Spawner = context.Source,
                    });
            }
        }

        public static void HandleApplyDisplacement(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            if (!world.IsAlive(context.Target)) return;

            ref readonly var disp = ref templateData.Displacement;
            if (disp.TotalDurationTicks <= 0 || disp.TotalDistanceCm <= 0) return;

            Entity directionTargetEntity = context.TargetContext;
            Fix64Vec2 targetPointCm = default;
            bool hasTargetPoint = false;

            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                hasTargetPoint = true;
            }
            else if (world.IsAlive(context.Source) && world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var sourceExec = ref world.Get<AbilityExecInstance>(context.Source);
                if (sourceExec.HasTargetPos != 0)
                {
                    targetPointCm = sourceExec.TargetPosCm;
                    hasTargetPoint = true;
                }
            }

            EntityCreationHelper.CreateDisplacement(world,
                new DisplacementState
                {
                    TargetEntity = context.Target,
                    SourceEntity = context.Source,
                    DirectionTargetEntity = directionTargetEntity,
                    DirectionMode = disp.DirectionMode,
                    FixedDirectionRad = Fix64.FromInt(disp.FixedDirectionDeg) * Fix64.Deg2Rad,
                    TargetPointCm = targetPointCm,
                    HasTargetPoint = hasTargetPoint,
                    TotalDistanceCm = disp.TotalDistanceCm,
                    RemainingDistanceCm = Fix64.FromInt(disp.TotalDistanceCm),
                    TotalDurationTicks = disp.TotalDurationTicks,
                    RemainingTicks = disp.TotalDurationTicks,
                    OverrideNavigation = disp.OverrideNavigation,
                });
        }
    }

    public struct ProjectileState
    {
        public Fix64 Speed;
        public int Range;
        public int ArcHeight;
        public int ImpactEffectTemplateId;
        public Entity Source;
        public Entity Target;
        public Fix64 TraveledCm;
    }

    public struct SpawnedUnitState
    {
        public int UnitTypeId;
        public int OffsetRadius;
        public int OnSpawnEffectTemplateId;
        public Entity Spawner;
    }

    public static class EntityCreationHelper
    {
        public static Entity CreateProjectile(World world, in ProjectileState state)
        {
            return world.Create(state);
        }

        public static Entity CreateSpawnedUnit(World world, in SpawnedUnitState state)
        {
            return world.Create(state);
        }

        public static Entity CreateDisplacement(World world, in DisplacementState state)
        {
            return world.Create(state);
        }
    }
}
