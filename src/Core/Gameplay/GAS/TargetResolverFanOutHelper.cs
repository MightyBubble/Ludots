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
    /// Legacy one-shot method <see cref="CollectFanOutTargets"/> is preserved for backward compat.
    /// </summary>
    public static class TargetResolverFanOutHelper
    {
        private static readonly Fix64 _180 = Fix64.FromInt(180);

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

            if (preferSourceCenter && world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source))
            {
                center = world.Get<WorldPositionCm>(ctx.Source).Value.ToWorldCmInt2();
            }
            else if (!preferSourceCenter && world.IsAlive(ctx.Target) && world.Has<WorldPositionCm>(ctx.Target))
            {
                center = world.Get<WorldPositionCm>(ctx.Target).Value.ToWorldCmInt2();
            }
            else if (world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source))
            {
                center = world.Get<WorldPositionCm>(ctx.Source).Value.ToWorldCmInt2();
            }
            else
            {
                return 0;
            }

            int directionDeg = ComputeDirection(world, in ctx);
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
            ref readonly var spatial = ref query.Spatial;
            WorldCmInt2 center = default;
            bool hasCenter = false;

            // Precompute center for Ring inner-radius check
            if (spatial.Shape == SpatialShape.Ring && spatial.InnerRadiusCm > 0)
            {
                if (world.IsAlive(ctx.Target) && world.Has<WorldPositionCm>(ctx.Target))
                {
                    center = world.Get<WorldPositionCm>(ctx.Target).Value.ToWorldCmInt2();
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

                // Relationship filter (only if source team is known)
                if (filter.RelationFilter != RelationshipFilter.All && sourceTeamId != 0 && world.Has<Team>(entity))
                {
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
                var cmd = commands[i];
                queue.Publish(new EffectRequest
                {
                    RootId = cmd.RootId,
                    Source = ResolveSlot(cmd.ContextMapping.PayloadSource, in cmd),
                    Target = ResolveSlot(cmd.ContextMapping.PayloadTarget, in cmd),
                    TargetContext = ResolveSlot(cmd.ContextMapping.PayloadTargetContext, in cmd),
                    TemplateId = cmd.PayloadEffectTemplateId
                });
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
            if (world.IsAlive(ctx.Source) && world.Has<WorldPositionCm>(ctx.Source) &&
                world.IsAlive(ctx.Target) && world.Has<WorldPositionCm>(ctx.Target))
            {
                var srcPos = world.Get<WorldPositionCm>(ctx.Source).Value.ToWorldCmInt2();
                var tgtPos = world.Get<WorldPositionCm>(ctx.Target).Value.ToWorldCmInt2();
                int dx = tgtPos.X - srcPos.X;
                int dy = tgtPos.Y - srcPos.Y;
                if (dx != 0 || dy != 0)
                {
                    var rad = Fix64Math.Atan2Fast(Fix64.FromInt(dy), Fix64.FromInt(dx));
                    int deg = (rad * _180 / Fix64.Pi).RoundToInt();
                    if (deg < 0) deg += 360;
                    return deg;
                }
            }
            return 0;
        }
    }
}
