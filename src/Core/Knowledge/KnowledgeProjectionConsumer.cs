using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;

namespace Ludots.Core.Knowledge
{
    public static class KnowledgeProjectionConsumer
    {
        private const int RelationSourceBufferCapacity = 32;
        private const int RelationTargetBufferCapacity = 64;

        public static bool HasResolver(Dictionary<string, object> globals)
        {
            return globals.TryGetValue(CoreServiceKeys.KnowledgeProjectionResolver.Name, out object? resolverObj) &&
                   resolverObj is KnowledgeProjectionResolver;
        }

        public static bool TryResolve(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity target,
            out KnowledgeProjection projection)
        {
            projection = default;
            if (viewer == Entity.Null ||
                !world.IsAlive(viewer) ||
                !TryGetResolver(globals, out KnowledgeProjectionResolver resolver))
            {
                return false;
            }

            Span<Entity> scopeMembers = stackalloc Entity[1];
            Span<Entity> relationSources = stackalloc Entity[RelationSourceBufferCapacity];
            Span<Entity> relationTargets = stackalloc Entity[RelationTargetBufferCapacity];
            ScopeKey viewerScope = ScopeKey.Self;
            var roleContext = new RoleResolverContext(
                actor: viewer,
                subject: viewer,
                viewer: viewer);
            return resolver.TryResolveWithRelationGrants(
                viewer,
                target,
                ResolveCurrentTick(globals),
                in viewerScope,
                in roleContext,
                scopeMembers,
                relationSources,
                relationTargets,
                out projection);
        }

        public static bool CanReadPosition(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity target,
            KnowledgePositionAccess requiredPosition,
            out KnowledgeProjection projection)
        {
            return TryResolve(world, globals, viewer, target, out projection) &&
                   projection.CanReadPosition(requiredPosition);
        }

        public static bool TryResolveForViewer(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity target,
            out KnowledgeProjection projection)
        {
            return TryResolveForViewer(
                world,
                globals,
                viewer,
                target,
                ReadOnlySpan<int>.Empty,
                out projection);
        }

        public static bool TryResolveForViewer(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity target,
            ReadOnlySpan<int> requiredAttributeIds,
            out KnowledgeProjection projection)
        {
            projection = default;
            if (viewer == Entity.Null ||
                !world.IsAlive(viewer) ||
                !TryGetResolver(globals, out KnowledgeProjectionResolver resolver))
            {
                return false;
            }

            Span<Entity> scopeMembers = stackalloc Entity[1];
            Span<Entity> relationSources = stackalloc Entity[RelationSourceBufferCapacity];
            Span<Entity> relationTargets = stackalloc Entity[RelationTargetBufferCapacity];
            ScopeKey viewerScope = ScopeKey.Self;
            var roleContext = new RoleResolverContext(
                actor: viewer,
                subject: viewer,
                viewer: viewer);
            return resolver.TryResolveWithRelationGrants(
                viewer,
                target,
                ResolveCurrentTick(globals),
                in viewerScope,
                in roleContext,
                scopeMembers,
                relationSources,
                relationTargets,
                requiredAttributeIds,
                out projection);
        }

        public static bool CanReadPositionForViewer(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity target,
            KnowledgePositionAccess requiredPosition,
            out KnowledgeProjection projection)
        {
            return TryResolveForViewer(world, globals, viewer, target, out projection) &&
                   projection.CanReadPosition(requiredPosition);
        }

        public static bool TryResolveSoleLocalSeatViewer(
            World world,
            Dictionary<string, object> globals,
            out Entity viewer)
        {
            if (Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(globals, out viewer) &&
                world.IsAlive(viewer))
            {
                return true;
            }

            viewer = Entity.Null;
            return false;
        }

        public static int ResolveCurrentTick(Dictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.Clock.Name, out object? clockObj) &&
                clockObj is IClock clock)
            {
                return clock.Now(ClockDomainId.Step);
            }

            return 0;
        }

        private static bool TryGetResolver(
            Dictionary<string, object> globals,
            out KnowledgeProjectionResolver resolver)
        {
            resolver = default!;
            return globals.TryGetValue(CoreServiceKeys.KnowledgeProjectionResolver.Name, out object? resolverObj) &&
                   resolverObj is KnowledgeProjectionResolver candidate &&
                   (resolver = candidate) != null;
        }

    }
}
