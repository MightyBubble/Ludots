using System;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// Emits the primary non-skinned model projection under performer ownership.
    /// Skinned/animator lanes intentionally remain legacy in this slice.
    /// </summary>
    public sealed class ModelPerformBehavior
    {
        public bool TryCreateRequest(
            World world,
            Entity owner,
            int definitionId,
            in VisualRuntimeState visual,
            in VisualTransform transform,
            int ownerStableId,
            LODLevel lod,
            out PresentationRequest request)
        {
            request = default;

            if (!visual.HasRenderableAsset || !visual.IsVisibleRequested)
            {
                return false;
            }

            if (visual.RenderPath.IsSkinnedLane() || visual.HasAnimator)
            {
                return false;
            }

            if (ownerStableId <= 0)
            {
                throw new InvalidOperationException(
                    $"Model performer requires a positive PresentationStableId for renderable owner #{owner.Id}:{owner.WorldId}.");
            }

            PresentationRenderContract.ValidateRuntimeState(
                nameof(ModelPerformBehavior),
                visual,
                hasAnimatorComponent: false,
                default,
                default);

            float baseScale = visual.BaseScale <= 0f ? 1f : visual.BaseScale;
            int templateId = world.Has<VisualTemplateRef>(owner) ? world.Get<VisualTemplateRef>(owner).TemplateId : 0;
            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = visual.MeshAssetId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = transform.Scale * baseScale,
                Color = TeamColorResolver.Resolve(world, owner),
                StableId = Performers.PerformerVisualIdentity.ComposeStableId(ownerStableId, Performers.PerformerVisualKind.Model, definitionId),
                MaterialId = visual.MaterialId,
                TemplateId = templateId,
                RenderPath = visual.RenderPath,
                Mobility = visual.Mobility,
                Flags = visual.Flags,
                Visibility = lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible,
                LOD = lod,
            };

            request = PresentationRequest.FromVisualProxy(owner, proxy);
            return true;
        }
    }
}
