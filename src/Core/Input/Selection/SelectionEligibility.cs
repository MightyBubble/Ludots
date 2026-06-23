using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Formal selection candidate checks shared by click/box/tab input.
    /// </summary>
    public static class SelectionEligibility
    {
        public static bool IsSelectableNow(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<SelectionSelectableTag>(entity))
            {
                return false;
            }

            return !world.Has<SelectionSelectableState>(entity) ||
                   world.Get<SelectionSelectableState>(entity).Enabled;
        }

        public static bool CanAcquire(World world, Entity selector, Entity candidate, RelationshipFilter relationFilter)
        {
            if (!world.IsAlive(selector) || !IsSelectableNow(world, candidate))
            {
                return false;
            }

            if (relationFilter == RelationshipFilter.All)
            {
                return true;
            }

            return world.TryGet(selector, out Team selectorTeam) &&
                   world.TryGet(candidate, out Team candidateTeam) &&
                   RelationshipFilterUtil.Passes(relationFilter, selectorTeam.Id, candidateTeam.Id);
        }

        public static bool CanAcquire(
            World world,
            Dictionary<string, object> globals,
            Entity selector,
            Entity candidate,
            RelationshipFilter relationFilter)
        {
            return CanAcquire(world, selector, candidate, relationFilter) &&
                   CanInspectLive(world, globals, selector, candidate);
        }

        public static bool CanInspectLive(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity candidate)
        {
            if (!IsSelectableNow(world, candidate))
            {
                return false;
            }

            if (!KnowledgeProjectionConsumer.HasResolver(globals))
            {
                return true;
            }

            if (!TryResolveExplicitViewer(world, viewer, out Entity resolvedViewer))
            {
                return false;
            }

            return KnowledgeProjectionConsumer.CanReadPositionForViewer(
                       world,
                       globals,
                       resolvedViewer,
                       candidate,
                       KnowledgePositionAccess.Live,
                       out KnowledgeProjection projection) &&
                   projection.Presence == KnowledgePresence.LiveVisible;
        }

        public static bool CanTargetCommand(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity candidate,
            KnowledgePositionAccess requiredPosition)
        {
            if (!world.IsAlive(candidate))
            {
                return false;
            }

            if (!KnowledgeProjectionConsumer.HasResolver(globals))
            {
                return true;
            }

            if (!TryResolveExplicitViewer(world, viewer, out Entity resolvedViewer))
            {
                return false;
            }

            return KnowledgeProjectionConsumer.CanReadPositionForViewer(
                world,
                globals,
                resolvedViewer,
                candidate,
                requiredPosition,
                out _);
        }

        private static bool TryResolveExplicitViewer(World world, Entity viewer, out Entity resolvedViewer)
        {
            resolvedViewer = viewer;
            return viewer != Entity.Null && world.IsAlive(viewer);
        }
    }
}
