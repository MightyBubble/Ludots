using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.CommandSources
{
    /// <summary>
    /// Shared screen-pointer entity hit resolver for command-source acquisition and command intents.
    /// Selectability is simulation-owned: WorldPositionCm plus CommandSourceSelectableTag.
    /// Live inspectability uses KnowledgeProjectionStore, never camera frustum visibility.
    /// </summary>
    public static class CommandSourcePointerHitResolver
    {
        private static readonly QueryDescription SelectableQuery =
            new QueryDescription().WithAll<WorldPositionCm, CommandSourceSelectableTag>();

        public static Entity FindNearestInspectableEntity(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            Vector2 pointer,
            float radiusPixels)
        {
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (!globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) ||
                projectorObj is not IScreenProjector projector)
            {
                return Entity.Null;
            }

            return FindNearestInspectableEntity(world, globals, owner, pointer, radiusPixels, projector);
        }

        /// <summary>
        /// Explicit-projector variant for consumers that answer under one seat's PresentBinding
        /// (split-screen pick): the pointer/radius are binding-local, the projector must carry that
        /// binding's camera and surface metrics. Knowledge gating semantics are identical.
        /// </summary>
        public static Entity FindNearestInspectableEntity(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            Vector2 pointer,
            float radiusPixels,
            IScreenProjector projector)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (projector == null) throw new ArgumentNullException(nameof(projector));

            if (owner == Entity.Null)
            {
                return Entity.Null;
            }

            ScreenProjectionPoseContext projectionPose = ScreenProjectionGrounding.Resolve(world, globals);

            // Delegate-free entity enumeration: a capturing lambda would allocate a
            // closure per call on this every-frame pointer path.
            BestCandidate best = default;
            foreach (Chunk chunk in world.Query(in SelectableQuery))
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = chunk.Entity(i);
                    ConsiderCandidate(world, globals, owner, projector, pointer, radiusPixels, entity, in projectionPose, ref best);
                }
            }

            return best.Entity;
        }

        /// <summary>
        /// Candidate-restricted variant for explicit candidate sets (aim-graph pick): the
        /// same inspectability / pointer-hit / projected-bounds chain, but only the given
        /// candidates are considered — no world scan. Candidate order breaks ties.
        /// </summary>
        public static Entity FindNearestInspectableEntity(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            ReadOnlySpan<Entity> candidates,
            Vector2 pointer,
            float radiusPixels,
            IScreenProjector projector)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (globals == null) throw new ArgumentNullException(nameof(globals));
            if (projector == null) throw new ArgumentNullException(nameof(projector));

            if (owner == Entity.Null)
            {
                return Entity.Null;
            }

            ScreenProjectionPoseContext projectionPose = ScreenProjectionGrounding.Resolve(world, globals);
            BestCandidate best = default;

            for (int i = 0; i < candidates.Length; i++)
            {
                Entity entity = candidates[i];
                if (!world.IsAlive(entity))
                {
                    continue;
                }

                ConsiderCandidate(world, globals, owner, projector, pointer, radiusPixels, entity, in projectionPose, ref best);
            }

            return best.Entity;
        }

        private struct BestCandidate
        {
            public Entity Entity;
            public ScreenRect Bounds;
            public bool HasBounds;
        }

        private static void ConsiderCandidate(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            IScreenProjector projector,
            Vector2 pointer,
            float radiusPixels,
            Entity entity,
            in ScreenProjectionPoseContext projectionPose,
            ref BestCandidate best)
        {
            if (!CommandSourceEligibility.CanInspectLive(world, globals, owner, entity))
            {
                return;
            }

            if (!SpatialBoundsUtility.PointerHitsEntity(world, entity, projector, pointer, radiusPixels, in projectionPose))
            {
                return;
            }

            if (!SpatialBoundsUtility.TryProjectScreenBounds(world, entity, projector, out ScreenRect candidateBounds, in projectionPose))
            {
                return;
            }

            if (!best.HasBounds)
            {
                best.Entity = entity;
                best.Bounds = candidateBounds;
                best.HasBounds = true;
                return;
            }

            int boundsComparison = CompareProjectedBounds(candidateBounds, best.Bounds, pointer);
            if (boundsComparison < 0 ||
                (boundsComparison == 0 && (best.Entity == Entity.Null || Compare(entity, best.Entity) < 0)))
            {
                best.Entity = entity;
                best.Bounds = candidateBounds;
            }
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
