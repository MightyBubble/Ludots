using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// World HUD projection consumer.
    /// Knowledge readability is resolved upstream via <see cref="KnowledgeProjectionConsumer"/>;
    /// this helper only translates presentation/cull facts into the perform phase contract.
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
                phaseResult = new PerformPhaseResult
                {
                    LOD = defaultLod,
                };

                return false;
            }

            PerformProjectionFacts projection = ResolveProjectionFacts(world, globals, audience.Viewer, owner, requiredAttributeIds);
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
                KnowledgeProjectionConsumer.TryResolveViewer(world, globals, Entity.Null, out Entity viewer))
            {
                return _phaseResolver.CreateAudienceContext(world, viewer, ResolveRevealHidden(globals));
            }

            return PerformAudienceContext.Default;
        }

        private static bool ResolveRevealHidden(Dictionary<string, object> globals)
        {
            return globals != null &&
                   globals.TryGetValue(CoreServiceKeys.PresentationAudienceRevealHidden.Name, out object value) &&
                   value is bool revealHidden &&
                   revealHidden;
        }

        private static PerformProjectionFacts ResolveProjectionFacts(
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
                return new PerformProjectionFacts(in projection);
            }

            return default;
        }
    }
}
