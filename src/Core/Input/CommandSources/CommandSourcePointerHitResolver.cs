using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.CommandSources
{
    /// <summary>
    /// Shared screen-pointer entity hit resolver for command-source acquisition and command intents.
    /// </summary>
    public static class CommandSourcePointerHitResolver
    {
        private static readonly QueryDescription SelectableQuery =
            new QueryDescription().WithAll<VisualTransform, CullState, CommandSourceSelectableTag>();

        public static Entity FindNearestInspectableEntity(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            Vector2 pointer,
            float radiusPixels)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (globals == null) throw new ArgumentNullException(nameof(globals));

            if (owner == Entity.Null ||
                !globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) ||
                projectorObj is not IScreenProjector projector)
            {
                return Entity.Null;
            }

            Entity best = Entity.Null;
            ScreenRect bestBounds = default;
            bool hasBestBounds = false;
            bool requiresKnowledgeProjection = Ludots.Core.Knowledge.KnowledgeProjectionConsumer.HasResolver(globals);
            ProjectionSnapshot projectionSnapshot = default;
            bool hasProjectionSnapshot = projector is IProjectionSnapshotProvider snapshotProvider &&
                                         snapshotProvider.TryGetProjectionSnapshot(out projectionSnapshot);
            float radiusSquared = radiusPixels * radiusPixels;

            foreach (ref var chunk in world.Query(in SelectableQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<VisualTransform> transforms = chunk.GetSpan<VisualTransform>();
                Span<CullState> culls = chunk.GetSpan<CullState>();
                bool hasSelectableState = chunk.Has<CommandSourceSelectableState>();
                Span<CommandSourceSelectableState> selectableStates = hasSelectableState
                    ? chunk.GetSpan<CommandSourceSelectableState>()
                    : default;
                bool hasSpatialBounds = chunk.Has<SpatialBounds>();

                foreach (int index in chunk)
                {
                    if (!culls[index].IsVisible ||
                        (hasSelectableState && !selectableStates[index].Enabled))
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (requiresKnowledgeProjection &&
                        !CommandSourceEligibility.CanInspectLive(world, globals, owner, entity))
                    {
                        continue;
                    }

                    ScreenRect candidateBounds;
                    if (!hasSpatialBounds)
                    {
                        Vector3 worldPosition = transforms[index].Position;
                        Vector2 projected = hasProjectionSnapshot
                            ? ProjectionSnapshotMath.WorldToScreen(in projectionSnapshot, in worldPosition)
                            : projector.WorldToScreen(worldPosition);
                        if (!IsFinite(projected) || Vector2.DistanceSquared(projected, pointer) > radiusSquared)
                        {
                            continue;
                        }

                        candidateBounds = new ScreenRect(projected.X, projected.Y, projected.X, projected.Y);
                    }
                    else
                    {
                        if (!SpatialBoundsUtility.PointerHitsEntity(world, entity, projector, pointer, radiusPixels) ||
                            !SpatialBoundsUtility.TryProjectScreenBounds(world, entity, projector, out candidateBounds))
                        {
                            continue;
                        }
                    }

                    if (!hasBestBounds)
                    {
                        best = entity;
                        bestBounds = candidateBounds;
                        hasBestBounds = true;
                        continue;
                    }

                    int boundsComparison = CompareProjectedBounds(candidateBounds, bestBounds, pointer);
                    if (boundsComparison < 0 ||
                        (boundsComparison == 0 && (best == Entity.Null || Compare(entity, best) < 0)))
                    {
                        best = entity;
                        bestBounds = candidateBounds;
                    }
                }
            }

            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector2 value)
        {
            return float.IsFinite(value.X) && float.IsFinite(value.Y);
        }

        private static int CompareProjectedBounds(in ScreenRect candidate, in ScreenRect best, Vector2 pointer)
        {
            float candidateArea = MathF.Max(0f, candidate.MaxX - candidate.MinX) * MathF.Max(0f, candidate.MaxY - candidate.MinY);
            float bestArea = MathF.Max(0f, best.MaxX - best.MinX) * MathF.Max(0f, best.MaxY - best.MinY);
            int areaComparison = candidateArea.CompareTo(bestArea);
            if (areaComparison != 0)
            {
                return areaComparison;
            }

            Vector2 candidateCenter = new((candidate.MinX + candidate.MaxX) * 0.5f, (candidate.MinY + candidate.MaxY) * 0.5f);
            Vector2 bestCenter = new((best.MinX + best.MaxX) * 0.5f, (best.MinY + best.MaxY) * 0.5f);
            float candidateD2 = Vector2.DistanceSquared(candidateCenter, pointer);
            float bestD2 = Vector2.DistanceSquared(bestCenter, pointer);
            return candidateD2.CompareTo(bestD2);
        }

        private static int Compare(Entity a, Entity b)
        {
            int worldCmp = a.WorldId.CompareTo(b.WorldId);
            return worldCmp != 0 ? worldCmp : a.Id.CompareTo(b.Id);
        }
    }
}
