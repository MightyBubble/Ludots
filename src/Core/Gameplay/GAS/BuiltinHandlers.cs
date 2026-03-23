using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
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
                in mergedParams,
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
                in mergedParams,
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
                in mergedParams,
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
                in mergedParams,
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

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpawnRequests == null)
            {
                throw new InvalidOperationException("CreateProjectile requires RuntimeEntitySpawnQueue in BuiltinHandlerExecutionContext.");
            }

            ref readonly var proj = ref templateData.Projectile;
            if (proj.Speed <= 0) return;

            bool hasLaunchOrigin = world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source);
            Fix64Vec2 launchOrigin = hasLaunchOrigin
                ? world.Get<WorldPositionCm>(context.Source).Value
                : Fix64Vec2.Zero;
            bool hasTargetPoint = TryResolveProjectileTargetPoint(world, in context, in mergedParams, out var targetPointCm);
            bool hasDirection = TryResolveProjectileDirection(
                world,
                context.Source,
                context.Target,
                hasLaunchOrigin,
                launchOrigin,
                hasTargetPoint,
                targetPointCm,
                out var direction);
            int spawnCount = proj.SpawnCount <= 0 ? 1 : proj.SpawnCount;
            Fix64 totalSpreadDeg = Fix64.FromInt(proj.SpreadAngleDeg);
            Fix64 startSpreadDeg = spawnCount <= 1 ? Fix64.Zero : -totalSpreadDeg / Fix64.FromInt(2);
            Fix64 spreadStepDeg = spawnCount <= 1 ? Fix64.Zero : totalSpreadDeg / Fix64.FromInt(spawnCount - 1);

            for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
            {
                var spawnedDirection = direction;
                bool spawnedHasDirection = hasDirection;
                if (spawnedHasDirection && proj.SpreadAngleDeg != 0)
                {
                    Fix64 spreadDeg = startSpreadDeg + (spreadStepDeg * Fix64.FromInt(spawnIndex));
                    spawnedDirection = RotateDirection(spawnedDirection, spreadDeg * Fix64.Deg2Rad);
                }

                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Assembly,
                    Source = context.Source,
                    TargetContext = context.TargetContext,
                    Projectile = new ProjectileState
                    {
                        Speed = Fix64.FromInt(proj.Speed),
                        Range = proj.Range,
                        ArcHeight = proj.ArcHeight,
                        ImpactEffectTemplateId = proj.ImpactEffectTemplateId,
                        HitEffectTemplateId = proj.HitEffectTemplateId,
                        PresentationEffectTemplateId = proj.PresentationEffectTemplateId,
                        TravelMode = proj.TravelMode,
                        ImpactPolicy = proj.ImpactPolicy,
                        CollisionHalfWidthCm = proj.CollisionHalfWidthCm,
                        CollisionRelationFilter = proj.CollisionRelationFilter,
                        CollisionExcludeSource = (byte)(proj.CollisionExcludeSource ? 1 : 0),
                        MaxHitCount = proj.MaxHitCount,
                        TurnRateDegPerSecond = proj.TurnRateDegPerSecond,
                        ReturnMode = proj.ReturnMode,
                        ResetHitHistoryOnReturn = (byte)(proj.ResetHitHistoryOnReturn ? 1 : 0),
                        FlightPhase = ProjectileFlightPhase.Outbound,
                        Source = context.Source,
                        Target = context.Target,
                        LaunchOriginCm = launchOrigin,
                        HasLaunchOrigin = (byte)(hasLaunchOrigin ? 1 : 0),
                        TargetPointCm = targetPointCm,
                        HasTargetPoint = (byte)(hasTargetPoint ? 1 : 0),
                        Direction = spawnedDirection,
                        HasDirection = (byte)(spawnedHasDirection ? 1 : 0),
                    },
                    HasProjectileState = 1,
                };

                if (hasLaunchOrigin)
                {
                    request.WorldPositionCm = launchOrigin;
                    request.HasWorldPosition = 1;
                }

                if (!runtime.SpawnRequests.TryEnqueue(request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue capacity exceeded while handling CreateProjectile.");
                }
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

            var runtime = BuiltinHandlerRuntimeScope.Current;
            if (runtime?.SpawnRequests == null)
            {
                throw new InvalidOperationException("CreateUnit requires RuntimeEntitySpawnQueue in BuiltinHandlerExecutionContext.");
            }

            ref readonly var unit = ref templateData.UnitCreation;
            if (unit.Count <= 0) return;

            var origin = ResolveCreateUnitOrigin(world, in context, in mergedParams);

            for (int i = 0; i < unit.Count; i++)
            {
                ComputeUnitCreationPlacement(
                    in unit,
                    context.Source,
                    unit.UnitTypeId,
                    i,
                    out Fix64Vec2 offsetCm,
                    out float facingAngleRad,
                    out bool hasFacing);

                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = unit.UseTemplateSpawn ? RuntimeEntitySpawnKind.Template : RuntimeEntitySpawnKind.UnitType,
                    Source = context.Source,
                    TargetContext = context.TargetContext,
                    WorldPositionCm = origin + offsetCm,
                    HasWorldPosition = 1,
                    FacingAngleRad = facingAngleRad,
                    HasFacing = (byte)(hasFacing ? 1 : 0),
                    UnitTypeId = unit.UnitTypeId,
                    TemplateId = unit.TemplateId,
                    OnSpawnEffectTemplateId = unit.OnSpawnEffectTemplateId,
                    CopySourceTeam = 1,
                    CopySourcePlayerOwner = (byte)(unit.CopySourcePlayerOwner ? 1 : 0),
                    LinkSourceAsParent = (byte)(unit.LinkSourceAsParent ? 1 : 0),
                };

                if (!runtime.SpawnRequests.TryEnqueue(request))
                {
                    throw new InvalidOperationException("RuntimeEntitySpawnQueue capacity exceeded while handling CreateUnit.");
                }
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
            else if (TryResolvePreservedTargetPoint(in mergedParams, out targetPointCm))
            {
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

        private static Fix64Vec2 ResolveCreateUnitOrigin(World world, in EffectContext context, in EffectConfigParams mergedParams)
        {
            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                return world.Get<WorldPositionCm>(context.TargetContext).Value;
            }

            if (TryResolvePreservedTargetPoint(in mergedParams, out var preservedPoint))
            {
                return preservedPoint;
            }

            if (world.IsAlive(context.Source) && world.Has<AbilityExecInstance>(context.Source))
            {
                ref readonly var exec = ref world.Get<AbilityExecInstance>(context.Source);
                if (exec.HasTargetPos != 0)
                {
                    return exec.TargetPosCm;
                }
            }

            if (world.IsAlive(context.Source) && world.Has<WorldPositionCm>(context.Source))
            {
                return world.Get<WorldPositionCm>(context.Source).Value;
            }

            throw new InvalidOperationException("CreateUnit requires target point or source WorldPositionCm.");
        }

        private static bool TryResolvePreservedTargetPoint(in EffectConfigParams mergedParams, out Fix64Vec2 targetPointCm)
        {
            if (mergedParams.TryGetFloat(EffectParamKeys.TargetPosX, out float x) &&
                mergedParams.TryGetFloat(EffectParamKeys.TargetPosY, out float y))
            {
                targetPointCm = Fix64Vec2.FromFloat(x, y);
                return true;
            }

            targetPointCm = default;
            return false;
        }

        private static bool TryResolveProjectileTargetPoint(World world, in EffectContext context, in EffectConfigParams mergedParams, out Fix64Vec2 targetPointCm)
        {
            if (world.IsAlive(context.TargetContext) && world.Has<WorldPositionCm>(context.TargetContext))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.TargetContext).Value;
                return true;
            }

            if (TryResolvePreservedTargetPoint(in mergedParams, out targetPointCm))
            {
                return true;
            }

            if (world.IsAlive(context.Target) && world.Has<WorldPositionCm>(context.Target))
            {
                targetPointCm = world.Get<WorldPositionCm>(context.Target).Value;
                return true;
            }

            targetPointCm = default;
            return false;
        }

        private static bool TryResolveProjectileDirection(
            World world,
            Entity source,
            Entity target,
            bool hasLaunchOrigin,
            in Fix64Vec2 launchOrigin,
            bool hasTargetPoint,
            in Fix64Vec2 targetPointCm,
            out Fix64Vec2 direction)
        {
            if (hasLaunchOrigin && world.IsAlive(target) && world.Has<WorldPositionCm>(target))
            {
                var delta = world.Get<WorldPositionCm>(target).Value - launchOrigin;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    direction = delta.Normalized();
                    return true;
                }
            }

            if (hasLaunchOrigin && hasTargetPoint)
            {
                var delta = targetPointCm - launchOrigin;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    direction = delta.Normalized();
                    return true;
                }
            }

            if (world.IsAlive(source) && world.Has<FacingDirection>(source))
            {
                float angle = world.Get<FacingDirection>(source).AngleRad;
                var angleFix = Fix64.FromFloat(angle);
                direction = new Fix64Vec2(Fix64Math.Cos(angleFix), Fix64Math.Sin(angleFix));
                return true;
            }

            direction = Fix64Vec2.UnitX;
            return false;
        }

        private static Fix64Vec2 RotateDirection(in Fix64Vec2 direction, Fix64 radians)
        {
            Fix64 cos = Fix64Math.Cos(radians);
            Fix64 sin = Fix64Math.Sin(radians);
            return new Fix64Vec2(
                direction.X * cos - direction.Y * sin,
                direction.X * sin + direction.Y * cos).Normalized();
        }

        private static Fix64Vec2 ComputeScatterOffsetCm(Entity source, int unitTypeId, int index, int radiusCm)
        {
            if (radiusCm <= 0)
            {
                return Fix64Vec2.Zero;
            }

            unchecked
            {
                uint seed = (uint)(
                    (source.Id * 73856093) ^
                    (source.WorldId * 19349663) ^
                    ((index + 1) * 83492791) ^
                    (unitTypeId * 265443576));

                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;

                Fix64 angleDeg = Fix64.FromInt((int)(seed % 360u));
                Fix64 angleRad = angleDeg * Fix64.Deg2Rad;

                Fix64 fraction = Fix64.HalfValue + Fix64.FromInt((int)((seed >> 9) & 1023)) / Fix64.FromInt(2047);
                Fix64 radius = Fix64.FromInt(radiusCm) * fraction;

                return new Fix64Vec2(
                    radius * Fix64Math.Cos(angleRad),
                    radius * Fix64Math.Sin(angleRad));
            }
        }

        private static void ComputeUnitCreationPlacement(
            in UnitCreationDescriptor unit,
            Entity source,
            int unitTypeId,
            int index,
            out Fix64Vec2 offsetCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            switch (unit.PlacementPattern)
            {
                case UnitCreationPlacementPattern.Circle:
                    ComputeCirclePlacement(in unit, index, out offsetCm, out facingAngleRad, out hasFacing);
                    return;
                default:
                    offsetCm = ComputeScatterOffsetCm(source, unitTypeId, index, unit.OffsetRadius);
                    facingAngleRad = 0f;
                    hasFacing = false;
                    return;
            }
        }

        private static void ComputeCirclePlacement(
            in UnitCreationDescriptor unit,
            int index,
            out Fix64Vec2 offsetCm,
            out float facingAngleRad,
            out bool hasFacing)
        {
            int count = unit.Count <= 0 ? 1 : unit.Count;
            Fix64 radius = Fix64.FromInt(unit.PlacementRadiusCm > 0 ? unit.PlacementRadiusCm : unit.OffsetRadius);
            Fix64 angleStep = Fix64.FromInt(360) / Fix64.FromInt(count);
            Fix64 angleDeg = Fix64.FromInt(unit.PlacementStartAngleDeg) + angleStep * Fix64.FromInt(index);
            Fix64 angleRad = angleDeg * Fix64.Deg2Rad;

            offsetCm = new Fix64Vec2(
                radius * Fix64Math.Cos(angleRad),
                radius * Fix64Math.Sin(angleRad));

            hasFacing = unit.FacingPattern != UnitCreationFacingPattern.PreserveTemplate;
            Fix64 quarterTurn = Fix64.Pi / Fix64.FromInt(2);
            facingAngleRad = unit.FacingPattern switch
            {
                UnitCreationFacingPattern.RadialOutward => angleRad.ToFloat(),
                UnitCreationFacingPattern.TangentClockwise => (angleRad - quarterTurn).ToFloat(),
                UnitCreationFacingPattern.TangentCounterClockwise => (angleRad + quarterTurn).ToFloat(),
                _ => 0f
            };
        }
    }

    public static class EntityCreationHelper
    {
        public static Entity CreateDisplacement(World world, in DisplacementState state)
        {
            return world.Create(state);
        }
    }
}
