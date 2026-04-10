using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class EntityVisualEmitSystem : BaseSystem<World, float>
    {
        private readonly PresentationVisualProxyEmitter _proxyEmitter;

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
            PrimitiveDrawBuffer drawBuffer,
            PrimitiveDrawBuffer? snapshotBuffer = null,
            PresentationVisualProxyBuffer? proxyBuffer = null,
            SkinnedVisualBatchBuffer? skinnedBatchBuffer = null)
            : base(world)
        {
            _proxyEmitter = new PresentationVisualProxyEmitter(drawBuffer, snapshotBuffer, proxyBuffer, skinnedBatchBuffer);
        }

        public override void Update(in float dt)
        {
            ValidateStableIdContract();
            EmitWithCullState();
            EmitWithoutCullState();
        }

        private void EmitWithCullState()
        {
            foreach (ref var chunk in World.Query(in _withCullQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var transforms = chunk.GetSpan<VisualTransform>();
                var visuals = chunk.GetSpan<VisualRuntimeState>();
                var stableIds = chunk.GetSpan<PresentationStableId>();
                var culls = chunk.GetSpan<CullState>();
                bool hasTemplates = chunk.Has<VisualTemplateRef>();
                var templates = hasTemplates ? chunk.GetSpan<VisualTemplateRef>() : default;
                bool hasAnimator = chunk.Has<AnimatorPackedState>();
                var animators = hasAnimator ? chunk.GetSpan<AnimatorPackedState>() : default;
                bool hasOverlay = chunk.Has<AnimationOverlayRequest>();
                var overlays = hasOverlay ? chunk.GetSpan<AnimationOverlayRequest>() : default;
                bool hasTeams = chunk.Has<Team>();
                var teams = hasTeams ? chunk.GetSpan<Team>() : default;
                bool hasOwners = chunk.Has<PlayerOwner>();
                var owners = hasOwners ? chunk.GetSpan<PlayerOwner>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(
                        Unsafe.Add(ref entityFirst, i),
                        stableIds[i].Value,
                        visuals[i],
                        transforms[i],
                        culls[i].IsVisible,
                        hasTemplates ? templates[i].TemplateId : 0,
                        hasAnimator ? animators[i] : default,
                        hasOverlay ? overlays[i] : default,
                        ResolveColor(hasTeams, teams, hasOwners, owners, i));
                }
            }
        }

        private void EmitWithoutCullState()
        {
            foreach (ref var chunk in World.Query(in _withoutCullQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                var transforms = chunk.GetSpan<VisualTransform>();
                var visuals = chunk.GetSpan<VisualRuntimeState>();
                var stableIds = chunk.GetSpan<PresentationStableId>();
                bool hasTemplates = chunk.Has<VisualTemplateRef>();
                var templates = hasTemplates ? chunk.GetSpan<VisualTemplateRef>() : default;
                bool hasAnimator = chunk.Has<AnimatorPackedState>();
                var animators = hasAnimator ? chunk.GetSpan<AnimatorPackedState>() : default;
                bool hasOverlay = chunk.Has<AnimationOverlayRequest>();
                var overlays = hasOverlay ? chunk.GetSpan<AnimationOverlayRequest>() : default;
                bool hasTeams = chunk.Has<Team>();
                var teams = hasTeams ? chunk.GetSpan<Team>() : default;
                bool hasOwners = chunk.Has<PlayerOwner>();
                var owners = hasOwners ? chunk.GetSpan<PlayerOwner>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(
                        Unsafe.Add(ref entityFirst, i),
                        stableIds[i].Value,
                        visuals[i],
                        transforms[i],
                        cullVisible: true,
                        hasTemplates ? templates[i].TemplateId : 0,
                        hasAnimator ? animators[i] : default,
                        hasOverlay ? overlays[i] : default,
                        ResolveColor(hasTeams, teams, hasOwners, owners, i));
                }
            }
        }

        private void ValidateStableIdContract()
        {
            foreach (ref var chunk in World.Query(in _missingStableIdQuery))
            {
                var visuals = chunk.GetSpan<VisualRuntimeState>();
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

        private void Emit(
            Entity entity,
            int stableId,
            in VisualRuntimeState visual,
            in VisualTransform transform,
            bool cullVisible,
            int templateId,
            in AnimatorPackedState animator,
            in AnimationOverlayRequest animationOverlay,
            in Vector4 color)
        {
            if (!visual.HasRenderableAsset)
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
            bool hasAnimatorComponent = visual.HasAnimator;
            PresentationRenderContract.ValidateRuntimeState("EntityVisualEmitSystem", visual, hasAnimatorComponent, animator, animationOverlay);
            VisualVisibility visibility = visual.ResolveVisibility(cullVisible);

            _proxyEmitter.Emit(new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Entity,
                MeshAssetId = visual.MeshAssetId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = scale,
                Color = color,
                StableId = stableId,
                MaterialId = visual.MaterialId,
                TemplateId = templateId,
                AnimationProfileId = visual.AnimationProfileId,
                RenderPath = visual.RenderPath,
                Mobility = visual.Mobility,
                Flags = visual.Flags,
                Animator = animator,
                AnimationOverlay = animationOverlay,
                Visibility = visibility,
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector4 ResolveColor(
            bool hasTeams,
            Span<Team> teams,
            bool hasOwners,
            Span<PlayerOwner> owners,
            int index)
        {
            if (hasTeams)
            {
                return teams[index].Id == 1
                    ? new Vector4(0.2f, 0.9f, 0.2f, 1f)
                    : new Vector4(0.9f, 0.2f, 0.2f, 1f);
            }

            if (hasOwners)
            {
                return owners[index].PlayerId == 1
                    ? new Vector4(0.2f, 0.9f, 0.2f, 1f)
                    : new Vector4(0.9f, 0.2f, 0.2f, 1f);
            }

            return new Vector4(1f, 1f, 1f, 1f);
        }
    }
}
