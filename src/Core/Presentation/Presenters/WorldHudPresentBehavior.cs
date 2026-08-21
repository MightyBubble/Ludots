using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// World HUD projection consumer.
    /// Knowledge readability is resolved upstream via <see cref="KnowledgeProjectionConsumer"/>;
    /// this helper only translates presentation/cull facts into the present phase contract.
    /// </summary>
    public sealed class WorldHudPresentBehavior
    {
        private readonly PresentPhaseResolver _phaseResolver = new();

        public bool TryResolveProjection(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            LODLevel defaultLod,
            WorldHudItemKind itemKind,
            ReadOnlySpan<int> requiredAttributeIds,
            out PresentPhaseResult phaseResult)
        {
            PresentAudienceContext audience = ResolveAudienceContext(world, globals);
            if (!audience.HasViewer)
            {
                phaseResult = new PresentPhaseResult
                {
                    LOD = defaultLod,
                };

                return false;
            }

            PresentProjectionFacts projection = ResolveProjectionFacts(world, globals, audience.Viewer, owner, requiredAttributeIds);
            PresentPhaseInput input = _phaseResolver.CreateInput(
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
            out PresentPhaseResult phaseResult)
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
            out PresentPhaseResult phaseResult)
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

        private PresentAudienceContext ResolveAudienceContext(World world, Dictionary<string, object> globals)
        {
            if (globals != null &&
                KnowledgeProjectionConsumer.TryResolveSoleLocalSeatViewer(world, globals, out Entity viewer))
            {
                return _phaseResolver.CreateAudienceContext(world, viewer, ResolveRevealHidden(globals));
            }

            return PresentAudienceContext.Default;
        }

        private static bool ResolveRevealHidden(Dictionary<string, object> globals)
        {
            return globals != null &&
                   globals.TryGetValue(CoreServiceKeys.PresentationAudienceRevealHidden.Name, out object value) &&
                   value is bool revealHidden &&
                   revealHidden;
        }

        private static PresentProjectionFacts ResolveProjectionFacts(
            World world,
            Dictionary<string, object> globals,
            Entity viewer,
            Entity owner,
            ReadOnlySpan<int> requiredAttributeIds)
        {
            if (globals != null &&
                KnowledgeProjectionConsumer.TryResolveForViewer(
                    world,
                    globals,
                    viewer,
                    owner,
                    requiredAttributeIds,
                    out KnowledgeProjection projection))
            {
                return new PresentProjectionFacts(in projection);
            }

            return default;
        }
    }
}
