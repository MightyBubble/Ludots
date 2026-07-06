using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// First extracted behavior helper for world HUD projection.
    /// It consumes the minimal phase contract and keeps viewer-policy truth upstream.
    /// </summary>
    public sealed class WorldHudPerformBehavior
    {
        private readonly PerformPhaseResolver _phaseResolver = new();

        public bool TryResolveProjection(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            LODLevel defaultLod,
            WorldHudItemKind itemKind,
            ReadOnlySpan<int> requiredAttributeIds,
            out PerformPhaseResult phaseResult)
        {
            PerformAudienceContext audience = ResolveAudienceContext(world, globals);
            if (!audience.HasViewer)
            {
                bool requiresAttributeProjection = !requiredAttributeIds.IsEmpty;
                bool hasAttributeProjection = OwnerSatisfiesRequiredAttributes(world, owner, requiredAttributeIds);
                phaseResult = new PerformPhaseResult
                {
                    ShouldPresent = true,
                    AllowWorldHudProjection = hasAttributeProjection,
                    IsVisible = true,
                    HasVision = true,
                    RequiresAttributeProjection = requiresAttributeProjection,
                    HasAttributeProjection = hasAttributeProjection,
                    LOD = defaultLod,
                };

                return phaseResult.AllowWorldHudProjection;
            }

            PerformProjectionFacts projection = ResolveProjectionFacts(globals, audience.Viewer, owner);
            PerformPhaseInput input = _phaseResolver.CreateInput(
                world,
                owner,
                audience,
                in projection,
                requiredAttributeIds);
            input.AllowVisibleTransientWorldText = itemKind == WorldHudItemKind.Text && requiredAttributeIds.IsEmpty;
            phaseResult = _phaseResolver.Resolve(input);

            return phaseResult.AllowWorldHudProjection;
        }

        public bool TryResolveProjection(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            LODLevel defaultLod,
            ReadOnlySpan<int> requiredAttributeIds,
            out PerformPhaseResult phaseResult)
        {
            return TryResolveProjection(
                world,
                globals,
                owner,
                defaultLod,
                WorldHudItemKind.Bar,
                requiredAttributeIds,
                out phaseResult);
        }

        public bool TryResolveProjection(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            LODLevel defaultLod,
            out PerformPhaseResult phaseResult)
        {
            return TryResolveProjection(
                world,
                globals,
                owner,
                defaultLod,
                WorldHudItemKind.Bar,
                ReadOnlySpan<int>.Empty,
                out phaseResult);
        }

        private PerformAudienceContext ResolveAudienceContext(World world, Dictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object viewerObj) &&
                viewerObj is Entity viewer &&
                world.IsAlive(viewer))
            {
                return _phaseResolver.CreateAudienceContext(world, viewer);
            }

            return PerformAudienceContext.Default;
        }

        private static PerformProjectionFacts ResolveProjectionFacts(
            Dictionary<string, object> globals,
            Entity viewer,
            Entity owner)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.KnowledgeProjectionResolver.Name, out object resolverObj) &&
                resolverObj is KnowledgeProjectionResolver resolver &&
                resolver.TryResolve(viewer, owner, ResolveCurrentTick(globals), out KnowledgeProjection projection))
            {
                return new PerformProjectionFacts(in projection);
            }

            return default;
        }

        private static int ResolveCurrentTick(Dictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.Clock.Name, out object clockObj) &&
                clockObj is IClock clock)
            {
                return clock.Now(ClockDomainId.Step);
            }

            return 0;
        }

        private static bool OwnerSatisfiesRequiredAttributes(
            World world,
            Entity owner,
            ReadOnlySpan<int> requiredAttributeIds)
        {
            if (requiredAttributeIds.IsEmpty)
            {
                return true;
            }

            if (!world.IsAlive(owner) || !world.Has<AttributeBuffer>(owner))
            {
                return false;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(owner);
            for (int i = 0; i < requiredAttributeIds.Length; i++)
            {
                if (!attributes.HasAttribute(requiredAttributeIds[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
