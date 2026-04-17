using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Perform;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class EntityVisualEmitSystem : BaseSystem<World, float>
    {
        private readonly PresentationRequestBuffer _requests;

        private readonly QueryDescription _withCullQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState, PresentationStableId, CullState>();

        private readonly QueryDescription _withoutCullQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState, PresentationStableId>()
            .WithNone<CullState>();

        private readonly QueryDescription _missingStableIdQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState>()
            .WithNone<PresentationStableId>();

        public EntityVisualEmitSystem(
            World world,
            PresentationRequestBuffer requests)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        }

        public override void Update(in float dt)
        {
            ValidateStableIdContract();
            EmitWithCullState();
            EmitWithoutCullState();
        }

        private void EmitWithCullState()
        {
            var query = World.Query(in _withCullQuery);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var visuals = chunk.GetArray<VisualRuntimeState>();
                var stableIds = chunk.GetArray<PresentationStableId>();
                var culls = chunk.GetArray<CullState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(chunk.Entity(i), stableIds[i].Value, visuals[i], transforms[i], culls[i].IsVisible, culls[i].LOD);
                }
            }
        }

        private void EmitWithoutCullState()
        {
            var query = World.Query(in _withoutCullQuery);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var visuals = chunk.GetArray<VisualRuntimeState>();
                var stableIds = chunk.GetArray<PresentationStableId>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(chunk.Entity(i), stableIds[i].Value, visuals[i], transforms[i], cullVisible: true, LODLevel.High);
                }
            }
        }

        private void ValidateStableIdContract()
        {
            var query = World.Query(in _missingStableIdQuery);
            foreach (var chunk in query)
            {
                var visuals = chunk.GetArray<VisualRuntimeState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (visuals[i].HasRenderableAsset)
                    {
                        Entity entity = chunk.Entity(i);
                        throw new InvalidOperationException(
                            $"Presentation snapshot requires PresentationStableId for renderable visual entity #{entity.Id}:{entity.WorldId}.");
                    }
                }
            }
        }

        private void Emit(Entity entity, int stableId, in VisualRuntimeState visual, in VisualTransform transform, bool cullVisible, LODLevel lod)
        {
            if (!visual.HasRenderableAsset)
            {
                return;
            }

            if (World.Has<ModelPerformBinding>(entity) && !visual.RenderPath.IsSkinnedLane() && !visual.HasAnimator)
            {
                return;
            }

            if (stableId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presentation snapshot requires a positive PresentationStableId for renderable visual entity #{entity.Id}:{entity.WorldId}.");
            }

            float baseScale = visual.BaseScale <= 0f ? 1f : visual.BaseScale;
            var scale = transform.Scale * baseScale;
            int templateId = World.Has<VisualTemplateRef>(entity) ? World.Get<VisualTemplateRef>(entity).TemplateId : 0;
            bool hasAnimatorComponent = World.Has<AnimatorPackedState>(entity);
            AnimatorPackedState animator = hasAnimatorComponent ? World.Get<AnimatorPackedState>(entity) : default;
            AnimationOverlayRequest animationOverlay = World.Has<AnimationOverlayRequest>(entity) ? World.Get<AnimationOverlayRequest>(entity) : default;
            PresentationRenderContract.ValidateRuntimeState("EntityVisualEmitSystem", visual, hasAnimatorComponent, animator, animationOverlay);
            VisualVisibility visibility = visual.ResolveVisibility(cullVisible);
            ResolveRenderAsset(visual, lod, out int meshAssetId, out int materialId);

            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Entity,
                MeshAssetId = meshAssetId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = scale,
                Color = TeamColorResolver.Resolve(World, entity),
                StableId = stableId,
                MaterialId = materialId,
                TemplateId = templateId,
                AnimationProfileId = visual.AnimationProfileId,
                RenderPath = visual.RenderPath,
                Mobility = visual.Mobility,
                Flags = visual.Flags,
                Animator = animator,
                AnimationOverlay = animationOverlay,
                Visibility = visibility,
                LOD = lod,
            };
            _requests.Add(PresentationRequest.FromVisualProxy(entity, proxy));
        }

        private static void ResolveRenderAsset(in VisualRuntimeState visual, LODLevel lod, out int meshAssetId, out int materialId)
        {
            meshAssetId = visual.MeshAssetId;
            materialId = visual.MaterialId;

            if (!visual.LodProfile.HasValue || lod == LODLevel.Culled)
            {
                return;
            }

            VisualLodEntry entry = visual.LodProfile.Value.Resolve(lod);
            meshAssetId = entry.MeshAssetId;
            if (entry.MaterialOverrideId > 0)
            {
                materialId = entry.MaterialOverrideId;
            }
        }
    }
}
