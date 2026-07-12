using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.CommandSources
{
    /// <summary>
    /// Candidate checks shared by click, box, tab, and command targeting.
    /// </summary>
    public static class CommandSourceEligibility
    {
        private const int RelationSourceBufferCapacity = 32;
        private const int RelationTargetBufferCapacity = 64;

        public static bool IsSelectableNow(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<CommandSourceSelectableTag>(entity))
            {
                return false;
            }

            return !world.Has<CommandSourceSelectableState>(entity) ||
                   world.Get<CommandSourceSelectableState>(entity).Enabled;
        }

        public static bool CanAcquire(
            World world,
            Dictionary<string, object> globals,
            Entity selector,
            Entity candidate,
            RelationshipFilter relationFilter)
        {
            if (!world.IsAlive(selector) || !IsSelectableNow(world, candidate))
            {
                return false;
            }

            if (relationFilter != RelationshipFilter.All &&
                !PassesRelationshipFilter(world, globals, selector, candidate, relationFilter))
            {
                return false;
            }

            return CanInspectLive(world, globals, selector, candidate);
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

        public static bool CanTargetCommand(
            World world,
            KnowledgeProjectionResolver resolver,
            int currentTick,
            Entity viewer,
            Entity candidate,
            KnowledgePositionAccess requiredPosition)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            if (!world.IsAlive(candidate) ||
                !TryResolveExplicitViewer(world, viewer, out Entity resolvedViewer))
            {
                return false;
            }

            Span<Entity> scopeMembers = stackalloc Entity[1];
            Span<Entity> relationSources = stackalloc Entity[RelationSourceBufferCapacity];
            Span<Entity> relationTargets = stackalloc Entity[RelationTargetBufferCapacity];
            ScopeKey viewerScope = ScopeKey.Self;
            var roleContext = new RoleResolverContext(
                actor: resolvedViewer,
                subject: resolvedViewer,
                viewer: resolvedViewer);
            return resolver.TryResolveWithRelationGrants(
                       resolvedViewer,
                       candidate,
                       currentTick,
                       in viewerScope,
                       in roleContext,
                       scopeMembers,
                       relationSources,
                       relationTargets,
                       out KnowledgeProjection projection) &&
                   projection.CanReadPosition(requiredPosition);
        }

        private static bool TryResolveExplicitViewer(World world, Entity viewer, out Entity resolvedViewer)
        {
            resolvedViewer = viewer;
            return viewer != Entity.Null && world.IsAlive(viewer);
        }

        private static bool PassesRelationshipFilter(
            World world,
            Dictionary<string, object> globals,
            Entity selector,
            Entity candidate,
            RelationshipFilter relationFilter)
        {
            ControlDomainQuery controlDomains = RequireService<ControlDomainQuery>(globals, CoreServiceKeys.ControlDomainQuery);
            DomainStanceQuery stances = RequireService<DomainStanceQuery>(globals, CoreServiceKeys.DomainStanceQuery);
            if (!TryResolveDomain(world, controlDomains, stances, selector, out Entity selectorDomain) ||
                !TryResolveDomain(world, controlDomains, stances, candidate, out Entity candidateDomain))
            {
                return false;
            }

            int actualStance = stances.GetStance(selectorDomain, candidateDomain);
            return relationFilter switch
            {
                RelationshipFilter.Hostile => actualStance == RequireStanceId(stances, nameof(RelationshipFilter.Hostile)),
                RelationshipFilter.Friendly => actualStance == RequireStanceId(stances, nameof(RelationshipFilter.Friendly)),
                RelationshipFilter.Neutral => actualStance == RequireStanceId(stances, nameof(RelationshipFilter.Neutral)),
                RelationshipFilter.NotFriendly => actualStance != RequireStanceId(stances, nameof(RelationshipFilter.Friendly)),
                RelationshipFilter.NotHostile => actualStance != RequireStanceId(stances, nameof(RelationshipFilter.Hostile)),
                RelationshipFilter.All => true,
                _ => throw new ArgumentOutOfRangeException(nameof(relationFilter), relationFilter, "Unsupported relationship filter."),
            };
        }

        private static bool TryResolveDomain(
            World world,
            ControlDomainQuery controlDomains,
            DomainStanceQuery stances,
            Entity entity,
            out Entity domain)
        {
            domain = Entity.Null;
            return world.IsAlive(entity) &&
                   (controlDomains.TryResolveControlDomain(entity, out domain) ||
                    stances.TryResolveStanceDomain(entity, out domain));
        }

        private static int RequireStanceId(DomainStanceQuery stances, string stanceName)
        {
            if (!stances.TryResolveStanceId(stanceName, out int stanceId))
            {
                throw new InvalidOperationException(
                    $"Command-source relation filter '{stanceName}' requires a matching registered domain stance.");
            }

            return stanceId;
        }

        private static T RequireService<T>(Dictionary<string, object> globals, ServiceKey<T> key)
            where T : class
        {
            if (globals.TryGetValue(key.Name, out object? value) && value is T service)
            {
                return service;
            }

            throw new InvalidOperationException(
                $"Command-source relation filtering requires service '{key.Name}'.");
        }
    }
}
