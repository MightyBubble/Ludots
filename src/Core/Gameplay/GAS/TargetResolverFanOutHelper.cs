using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Command struct for a single fan-out target produced by TargetResolver.
    /// Shared between EffectApplicationSystem (on-apply) and EffectLifetimeSystem (periodic).
    /// </summary>
    public struct FanOutCommand
    {
        public int RootId;
        public Entity OriginalSource;
        public Entity OriginalTarget;
        public Entity OriginalTargetContext;
        public int PayloadEffectTemplateId;
        public TargetResolverContextMapping ContextMapping;
        public Entity ResolvedEntity;
    }

    /// <summary>
    /// Result of the OnResolve phase: candidates collected but not yet hit-validated.
    /// </summary>
    public struct ResolvedCandidate
    {
        public Entity Entity;
    }

    /// <summary>
    /// Shared helpers for TargetResolver fan-out logic.
    /// Split into two phases per the architecture plan:
    ///   OnResolve: spatial query → collect candidate entities
    ///   OnHit:     per-candidate hit validation (built-in filters + user Graph)
    ///
    /// Convenience method <see cref="CollectFanOutTargets"/> combines both phases in a single call.
    /// </summary>
    public static class TargetResolverFanOutHelper
    {

        // ── OnResolve Phase: spatial query, returns raw candidates ──

        /// <summary>
        /// Execute the OnResolve phase: spatial query based on query descriptor.
        /// Returns the number of candidates written to <paramref name="buffer"/>.
        /// Does NOT apply relationship/layer filters — those are OnHit concerns.
        /// </summary>
        public static int ResolveTargets(
            World world,
            in EffectContext ctx,
            in TargetQueryDescriptor query,
            ISpatialQueryService spatialQueries,
            Entity[] buffer)
        {
            EffectConfigParams mergedParams = default;
            return ResolveTargets(world, in ctx, in query, in mergedParams, spatialQueries, buffer);
        }

        public static int ResolveTargets(
            World world,
            in EffectContext ctx,
            in TargetQueryDescriptor query,
            in EffectConfigParams mergedParams,
            ISpatialQueryService spatialQueries,
            Entity[] buffer)
        {
            if (query.Kind == TargetResolverKind.GraphProgram)
            {
                // Graph-based resolution will be handled by OnResolve Phase Graph.
                return 0;
            }

            if (query.Kind != TargetResolverKind.BuiltinSpatial) return 0;

            WorldCmInt2 center;
            ref readonly var spatial = ref query.Spatial;
            bool preferSourceCenter =
                spatial.Shape == SpatialShape.Cone ||
                spatial.Shape == SpatialShape.Line ||
                spatial.Shape == SpatialShape.Rectangle;

            if (preferSourceCenter && TryResolveQueryOrigin(world, in ctx, in mergedParams, out center))
            {
            }
            else if (!preferSourceCenter && TryResolveTargetPoint(world, in ctx, in mergedParams, out center))
            {
            }
            else if (world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source))
            {
                center = world.Get<WorldPositionCm>(ctx.Source).Value.ToWorldCmInt2();
            }
            else
            {
                return 0;
            }

            int directionDeg = ComputeDirection(world, in ctx, in mergedParams);
            Span<Entity> buf = buffer;
            SpatialQueryResult result;

            switch (spatial.Shape)
            {
                case SpatialShape.Circle:
                    result = spatialQueries.QueryRadius(center, spatial.RadiusCm, buf);
                    break;
                case SpatialShape.Cone:
                    result = spatialQueries.QueryCone(center, directionDeg, spatial.HalfAngleDeg, spatial.RadiusCm, buf);
                    break;
                case SpatialShape.Rectangle:
                    result = spatialQueries.QueryRectangle(center, spatial.HalfWidthCm, spatial.HalfHeightCm, spatial.RotationDeg + directionDeg, buf);
                    break;
                case SpatialShape.Line:
                    result = spatialQueries.QueryLine(center, directionDeg, spatial.LengthCm, spatial.HalfWidthCm, buf);
                    break;
                case SpatialShape.Ring:
                    result = spatialQueries.QueryRadius(center, spatial.RadiusCm, buf);
                    break;
                default:
                    return 0;
            }

            return result.Count;
        }

        // ── OnHit Phase: built-in filters applied per candidate ──

        /// <summary>
        /// Execute the OnHit phase: validate candidates with built-in filters.
        /// Returns the number of validated targets that produced FanOutCommands.
        /// User-defined OnHit Graph validation is performed separately by EffectPhaseExecutor.
        /// </summary>
        public static int ValidateAndCollect(
            World world,
            in EffectContext ctx,
            in TargetQueryDescriptor query,
            in TargetFilterDescriptor filter,
            in TargetDispatchDescriptor dispatch,
            Entity[] buffer,
            int candidateCount,
            RootBudgetTable budget,
            List<FanOutCommand> commands,
            ref int dropped)
        {
            EffectConfigParams mergedParams = default;
            return ValidateAndCollect(world, in ctx, in query, in filter, in dispatch, in mergedParams, buffer, candidateCount, budget, commands, ref dropped);
        }

        public static int ValidateAndCollect(
            World world,
            in EffectContext ctx,
            in TargetQueryDescriptor query,
            in TargetFilterDescriptor filter,
            in TargetDispatchDescriptor dispatch,
            in EffectConfigParams mergedParams,
            Entity[] buffer,
            int candidateCount,
            RootBudgetTable budget,
            List<FanOutCommand> commands,
            ref int dropped)
        {
            ref readonly var spatial = ref query.Spatial;
            WorldCmInt2 center = default;
            bool hasCenter = false;

            // Precompute center for Ring inner-radius check
            if (spatial.Shape == SpatialShape.Ring && spatial.InnerRadiusCm > 0)
            {
                if (TryResolveTargetPoint(world, in ctx, in mergedParams, out center))
                {
                    hasCenter = true;
                }
                else if (world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source))
                {
                    center = world.Get<WorldPositionCm>(ctx.Source).Value.ToWorldCmInt2();
                    hasCenter = true;
                }
            }

            int sourceTeamId = 0;
            if (filter.RelationFilter != RelationshipFilter.All && world.IsAlive(ctx.Source) && world.Has<Team>(ctx.Source))
            {
                sourceTeamId = world.Get<Team>(ctx.Source).Id;
            }

            int maxTargets = filter.MaxTargets > 0 ? filter.MaxTargets : candidateCount;
            int added = 0;

            for (int i = 0; i < candidateCount && added < maxTargets; i++)
            {
                var entity = buffer[i];
                if (!world.IsAlive(entity)) continue;

                if (filter.ExcludeSource && entity.Equals(ctx.Source)) continue;

                // Ring: inner radius exclusion
                if (spatial.Shape == SpatialShape.Ring && spatial.InnerRadiusCm > 0 && hasCenter && world.Has<WorldPositionCm>(entity))
                {
                    var ePos = world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
                    long edx = ePos.X - center.X;
                    long edy = ePos.Y - center.Y;
                    long dist2 = edx * edx + edy * edy;
                    long inner2 = (long)spatial.InnerRadiusCm * spatial.InnerRadiusCm;
                    if (dist2 < inner2) continue;
                }

                // Layer filter
                if (filter.LayerMask != 0 && world.Has<EntityLayer>(entity))
                {
                    uint entityCategory = world.Get<EntityLayer>(entity).Value.Category;
                    if ((entityCategory & filter.LayerMask) == 0) continue;
                }

                // Relationship filter
                if (filter.RelationFilter != RelationshipFilter.All)
                {
                    if (sourceTeamId == 0 || !world.Has<Team>(entity))
                    {
                        continue;
                    }

                    int entityTeamId = world.Get<Team>(entity).Id;
                    if (!RelationshipFilterUtil.Passes(filter.RelationFilter, sourceTeamId, entityTeamId)) continue;
                }

                // Budget check
                if (!budget.TryConsume(ctx.RootId, GasConstants.MAX_CREATES_PER_ROOT))
                {
                    dropped++;
                    continue;
                }

                commands.Add(new FanOutCommand
                {
                    RootId = ctx.RootId,
                    OriginalSource = ctx.Source,
                    OriginalTarget = ctx.Target,
                    OriginalTargetContext = ctx.TargetContext,
                    PayloadEffectTemplateId = dispatch.PayloadEffectTemplateId,
                    ContextMapping = dispatch.ContextMapping,
                    ResolvedEntity = entity
                });
                added++;
            }

            return added;
        }

        // ── One-shot convenience method (calls both phases sequentially) ──

        /// <summary>
        /// Collect fan-out targets from a spatial query using the three-layer descriptors.
        /// Combines OnResolve + OnHit in a single call.
        /// </summary>
        public static void CollectFanOutTargets(
            World world,
            in EffectContext ctx,
            in TargetQueryDescriptor query,
            in TargetFilterDescriptor filter,
            in TargetDispatchDescriptor dispatch,
            ISpatialQueryService spatialQueries,
            RootBudgetTable budget,
            List<FanOutCommand> commands,
            Entity[] buffer,
            ref int dropped)
        {
            int candidateCount = ResolveTargets(world, in ctx, in query, spatialQueries, buffer);
            if (candidateCount <= 0) return;
            ValidateAndCollect(world, in ctx, in query, in filter, in dispatch, buffer, candidateCount, budget, commands, ref dropped);
        }

        /// <summary>
        /// Publish all collected fan-out commands as EffectRequests.
        /// </summary>
        public static void PublishFanOutCommands(List<FanOutCommand> commands, EffectRequestQueue queue)
        {
            if (queue == null || commands.Count == 0) return;

            for (int i = 0; i < commands.Count; i++)
            {
                FanOutCommand cmd = commands[i];
                PublishCommand(in cmd, queue);
            }
        }

        public static void PublishCommand(in FanOutCommand cmd, EffectRequestQueue queue)
        {
            if (queue == null || cmd.PayloadEffectTemplateId <= 0)
            {
                return;
            }

            queue.Publish(new EffectRequest
            {
                RootId = cmd.RootId,
                Source = ResolveSlot(cmd.ContextMapping.PayloadSource, in cmd),
                Target = ResolveSlot(cmd.ContextMapping.PayloadTarget, in cmd),
                TargetContext = ResolveSlot(cmd.ContextMapping.PayloadTargetContext, in cmd),
                TemplateId = cmd.PayloadEffectTemplateId
            });
        }

        public static void PublishResolvedTargets(
            int rootId,
            Entity originalSource,
            Entity originalTarget,
            Entity originalTargetContext,
            ReadOnlySpan<Entity> resolvedTargets,
            int payloadEffectTemplateId,
            in TargetResolverContextMapping contextMapping,
            EffectRequestQueue queue)
        {
            if (queue == null || payloadEffectTemplateId <= 0)
            {
                return;
            }

            for (int i = 0; i < resolvedTargets.Length; i++)
            {
                var resolved = resolvedTargets[i];
                var cmd = new FanOutCommand
                {
                    RootId = rootId,
                    OriginalSource = originalSource,
                    OriginalTarget = originalTarget,
                    OriginalTargetContext = originalTargetContext,
                    PayloadEffectTemplateId = payloadEffectTemplateId,
                    ContextMapping = contextMapping,
                    ResolvedEntity = resolved
                };
                PublishCommand(in cmd, queue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity ResolveSlot(ContextSlot slot, in FanOutCommand cmd)
        {
            return slot switch
            {
                ContextSlot.OriginalSource => cmd.OriginalSource,
                ContextSlot.OriginalTarget => cmd.OriginalTarget,
                ContextSlot.OriginalTargetContext => cmd.OriginalTargetContext,
                ContextSlot.ResolvedEntity => cmd.ResolvedEntity,
                _ => cmd.OriginalSource
            };
        }

        // ── Shared helpers ──

        private static int ComputeDirection(World world, in EffectContext ctx)
        {
            EffectConfigParams mergedParams = default;
            return ComputeDirection(world, in ctx, in mergedParams);
        }

        private static int ComputeDirection(World world, in EffectContext ctx, in EffectConfigParams mergedParams)
        {
            if (TryResolveExplicitVectorDirection(world, in ctx, in mergedParams, out int explicitVectorDirectionDeg))
            {
                return explicitVectorDirectionDeg;
            }

            if (TryResolveBlackboardFacing(world, ctx.Source, out int blackboardFacingDeg))
            {
                return blackboardFacingDeg;
            }

            if (world.IsAlive(ctx.Source) && world.Has<FacingDirection>(ctx.Source))
            {
                float degrees = WorldPlane2D.NormalizeDegreesPositive(
                    WorldPlane2D.RadToDegValue(world.Get<FacingDirection>(ctx.Source).AngleRad));
                return (int)MathF.Round(degrees);
            }

            if (TryResolveQueryOrigin(world, in ctx, in mergedParams, out var sourcePos) &&
                TryResolveTargetPoint(world, in ctx, in mergedParams, out var targetPos))
            {
                int dx = targetPos.X - sourcePos.X;
                int dy = targetPos.Y - sourcePos.Y;
                if (dx != 0 || dy != 0)
                {
                    return WorldPlane2D.FacingDegreesPositiveFromDirection(dx, dy);
                }
            }
            return 0;
        }

        private static bool TryResolveExplicitVectorDirection(
            World world,
            in EffectContext ctx,
            in EffectConfigParams mergedParams,
            out int directionDeg)
        {
            directionDeg = 0;
            if (EffectTargetPointResolver.TryResolvePreservedTargetPoint(in mergedParams, out Fix64Vec2 preservedTarget))
            {
                if (EffectTargetPointResolver.TryResolvePreservedTargetOrigin(in mergedParams, out Fix64Vec2 preservedOrigin) &&
                    TryComputeDirection(in preservedOrigin, in preservedTarget, out directionDeg))
                {
                    return true;
                }

                if (TryResolveSourceOrigin(world, in ctx, out Fix64Vec2 sourceOrigin) &&
                    TryComputeDirection(in sourceOrigin, in preservedTarget, out directionDeg))
                {
                    return true;
                }
            }

            if (world.IsAlive(ctx.Source) && world.Has<AbilityExecInstance>(ctx.Source))
            {
                ref readonly var exec = ref world.Get<AbilityExecInstance>(ctx.Source);
                if (exec.HasTargetPos != 0)
                {
                    if (exec.HasTargetOriginPos != 0 &&
                        TryComputeDirection(in exec.TargetOriginPosCm, in exec.TargetPosCm, out directionDeg))
                    {
                        return true;
                    }

                    if (TryResolveSourceOrigin(world, in ctx, out Fix64Vec2 sourceOrigin) &&
                        TryComputeDirection(in sourceOrigin, in exec.TargetPosCm, out directionDeg))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryResolveSourceOrigin(World world, in EffectContext ctx, out Fix64Vec2 origin)
        {
            if (world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source))
            {
                origin = world.Get<WorldPositionCm>(ctx.Source).Value;
                return true;
            }

            origin = default;
            return false;
        }

        private static bool TryComputeDirection(in Fix64Vec2 origin, in Fix64Vec2 target, out int directionDeg)
        {
            var dx = target.X - origin.X;
            var dy = target.Y - origin.Y;
            if (dx == Fix64.Zero && dy == Fix64.Zero)
            {
                directionDeg = 0;
                return false;
            }

            directionDeg = WorldPlane2D.FacingDegreesPositiveFromDirection(dx.ToInt(), dy.ToInt());
            return true;
        }

        private static bool TryResolveBlackboardFacing(World world, Entity source, out int facingDeg)
        {
            facingDeg = 0;
            if (!world.IsAlive(source) || !world.Has<BlackboardFloatBuffer>(source))
            {
                return false;
            }

            ref readonly BlackboardFloatBuffer blackboard = ref world.Get<BlackboardFloatBuffer>(source);
            if (!blackboard.TryGet(Orders.OrderBlackboardKeys.Cast_Facing, out float degrees))
            {
                return false;
            }

            facingDeg = (int)MathF.Round(WorldPlane2D.NormalizeDegreesPositive(degrees));
            return true;
        }

        private static bool TryResolveQueryOrigin(World world, in EffectContext ctx, out WorldCmInt2 point)
        {
            EffectConfigParams mergedParams = default;
            return TryResolveQueryOrigin(world, in ctx, in mergedParams, out point);
        }

        private static bool TryResolveQueryOrigin(World world, in EffectContext ctx, in EffectConfigParams mergedParams, out WorldCmInt2 point)
        {
            if (EffectTargetPointResolver.TryResolveOrigin(world, in ctx, in mergedParams, out Fix64Vec2 positionCm))
            {
                point = positionCm.ToWorldCmInt2();
                return true;
            }

            point = default;
            return false;
        }

        private static bool TryResolveTargetPoint(World world, in EffectContext ctx, out WorldCmInt2 point)
        {
            EffectConfigParams mergedParams = default;
            return TryResolveTargetPoint(world, in ctx, in mergedParams, out point);
        }

        private static bool TryResolveTargetPoint(World world, in EffectContext ctx, in EffectConfigParams mergedParams, out WorldCmInt2 point)
        {
            if (EffectTargetPointResolver.TryResolve(world, in ctx, in mergedParams, out Fix64Vec2 positionCm))
            {
                point = positionCm.ToWorldCmInt2();
                return true;
            }

            point = default;
            return false;
        }
    }
}
