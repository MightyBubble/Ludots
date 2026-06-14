using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Presentation.Components;
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
            out PerformPhaseResult phaseResult)
        {
            PerformAudienceContext audience = ResolveAudienceContext(world, globals);
            if (!audience.HasViewer)
            {
                phaseResult = new PerformPhaseResult
                {
                    ShouldPresent = true,
                    AllowWorldHudProjection = true,
                    IsVisible = true,
                    HasVision = true,
                    LOD = defaultLod,
                };

                return true;
            }

            RelationshipRuntime? relationships = TryResolveRelationshipRuntime(globals);
            PerformPhaseInput input = _phaseResolver.CreateInput(world, owner, audience, relationships, hasVision: true);
            phaseResult = _phaseResolver.Resolve(input);

            return phaseResult.AllowWorldHudProjection;
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

        private static RelationshipRuntime? TryResolveRelationshipRuntime(Dictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.RelationshipRuntime.Name, out object runtimeObj) &&
                runtimeObj is RelationshipRuntime runtime)
            {
                return runtime;
            }

            return null;
        }
    }
}
