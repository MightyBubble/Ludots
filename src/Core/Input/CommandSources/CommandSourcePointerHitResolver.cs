using System;
using System.Collections.Generic;
using System.Numerics;
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

            world.Query(in SelectableQuery, (Entity entity, ref VisualTransform transform, ref CullState cull, ref CommandSourceSelectableTag selectable) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                if (!CommandSourceEligibility.CanInspectLive(world, globals, owner, entity))
                {
                    return;
                }

                if (!SpatialBoundsUtility.PointerHitsEntity(world, entity, projector, pointer, radiusPixels))
                {
                    return;
                }

                if (!SpatialBoundsUtility.TryProjectScreenBounds(world, entity, projector, out ScreenRect candidateBounds))
                {
                    return;
                }

                if (!hasBestBounds)
                {
                    best = entity;
                    bestBounds = candidateBounds;
                    hasBestBounds = true;
                    return;
                }

                int boundsComparison = CompareProjectedBounds(candidateBounds, bestBounds, pointer);
                if (boundsComparison < 0 ||
                    (boundsComparison == 0 && (best == Entity.Null || Compare(entity, best) < 0)))
                {
                    best = entity;
                    bestBounds = candidateBounds;
                }
            });

            return best;
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
