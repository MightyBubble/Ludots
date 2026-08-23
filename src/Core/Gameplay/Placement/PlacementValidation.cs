using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Placement
{
    /// <summary>
    /// Layer 0 placement primitives. Shared by aim snap, ability activation validation,
    /// OnPropose/OnApply phase graphs, and presentation preview.
    /// </summary>
    public static class PlacementValidation
    {
        private static readonly Fix64 RangeEpsilon = Fix64.FromFloat(0.01f);

        public static bool ClampToRange(
            in Fix64Vec2 originCm,
            ref Fix64Vec2 targetCm,
            Fix64 rangeCm,
            out bool inRange)
        {
            inRange = true;
            if (rangeCm <= Fix64.Zero)
            {
                return true;
            }

            Fix64 distance = Fix64Vec2.Distance(originCm, targetCm);
            if (distance <= rangeCm + RangeEpsilon || distance <= RangeEpsilon)
            {
                return true;
            }

            inRange = false;
            Fix64 scale = rangeCm / distance;
            Fix64Vec2 delta = targetCm - originCm;
            targetCm = originCm + new Fix64Vec2(delta.X * scale, delta.Y * scale);
            return false;
        }

        public static bool IsPointInCircle(in Fix64Vec2 pointCm, in Fix64Vec2 centerCm, Fix64 radiusCm)
        {
            if (radiusCm <= Fix64.Zero)
            {
                return true;
            }

            return Fix64Vec2.Distance(pointCm, centerCm) <= radiusCm + RangeEpsilon;
        }

        public static bool TrySnapToNearestInCollection(
            World world,
            EntityCollectionStore collections,
            Entity owner,
            int collectionKeyId,
            in Fix64Vec2 pointCm,
            Fix64 maxDistanceCm,
            out Fix64Vec2 snappedCm,
            out Entity snappedEntity)
        {
            snappedCm = pointCm;
            snappedEntity = Entity.Null;
            if (collections == null || !world.IsAlive(owner) || collectionKeyId <= 0)
            {
                return false;
            }

            if (!collections.TryGet(owner, collectionKeyId, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            Fix64 bestDistanceSq = maxDistanceCm > Fix64.Zero ? maxDistanceCm * maxDistanceCm : Fix64.MaxValue;
            bool found = false;

            for (int i = 0; i < view.Count; i++)
            {
                if (!collections.TryGetEntityAt(handle, i, out Entity entity))
                {
                    continue;
                }

                if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
                {
                    continue;
                }

                Fix64Vec2 candidate = world.Get<WorldPositionCm>(entity).Value;
                Fix64 distanceSq = Fix64Vec2.DistanceSquared(pointCm, candidate);
                if (distanceSq > bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                snappedCm = candidate;
                snappedEntity = entity;
                found = true;
            }

            return found;
        }

        public static bool TryGetEntityWorldPositionCm(World world, Entity entity, out Fix64Vec2 positionCm)
        {
            positionCm = default;
            if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                return false;
            }

            positionCm = world.Get<WorldPositionCm>(entity).Value;
            return true;
        }

        /// <summary>
        /// Projects <paramref name="pointCm"/> onto the nearest outgoing graph edge within
        /// <paramref name="searchRadiusCm"/>. Reuses <see cref="GraphEdgeProjectionQuery"/>.
        /// </summary>
        public static bool TrySnapToNearestGraphEdge(
            NodeGraph graph,
            INodeGraphSpatialIndex index,
            ref Fix64Vec2 pointCm,
            Fix64 searchRadiusCm,
            Span<int> candidateScratch,
            out GraphEdgeProjection projection)
        {
            projection = default;
            if (graph == null || index == null || searchRadiusCm <= Fix64.Zero || candidateScratch.Length == 0)
            {
                return false;
            }

            var rounded = pointCm.RoundToInt();
            var position = new WorldCmInt2(rounded.x, rounded.y);
            int radiusCm = searchRadiusCm.RoundToInt();
            if (radiusCm <= 0)
            {
                return false;
            }

            if (!GraphEdgeProjectionQuery.TryProjectNearestEdge(
                    graph,
                    index,
                    position,
                    radiusCm,
                    candidateScratch,
                    out projection))
            {
                return false;
            }

            pointCm = Fix64Vec2.FromInt(projection.ProjectedXcm, projection.ProjectedYcm);
            return true;
        }
    }

}
