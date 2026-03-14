using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class EntityVisualEmitSystem : BaseSystem<World, float>
    {
        private readonly PrimitiveDrawBuffer _drawBuffer;
        private readonly PrimitiveDrawBuffer? _snapshotBuffer;

        private readonly QueryDescription _withCullQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState, PresentationStableId, CullState>();

        private readonly QueryDescription _withoutCullQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState, PresentationStableId>()
            .WithNone<CullState>();

        private readonly QueryDescription _missingStableIdQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState>()
            .WithNone<PresentationStableId>();

        public EntityVisualEmitSystem(World world, PrimitiveDrawBuffer drawBuffer, PrimitiveDrawBuffer? snapshotBuffer = null)
            : base(world)
        {
            _drawBuffer = drawBuffer;
            _snapshotBuffer = snapshotBuffer;
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
                    Emit(chunk.Entity(i), stableIds[i].Value, visuals[i], transforms[i], culls[i].IsVisible);
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
                    Emit(chunk.Entity(i), stableIds[i].Value, visuals[i], transforms[i], cullVisible: true);
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

        private void Emit(Entity entity, int stableId, in VisualRuntimeState visual, in VisualTransform transform, bool cullVisible)
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
            int templateId = World.Has<VisualTemplateRef>(entity) ? World.Get<VisualTemplateRef>(entity).TemplateId : 0;
            bool hasAnimatorComponent = World.Has<AnimatorPackedState>(entity);
            AnimatorPackedState animator = hasAnimatorComponent ? World.Get<AnimatorPackedState>(entity) : default;
            PresentationRenderContract.ValidateRuntimeState("EntityVisualEmitSystem", visual, hasAnimatorComponent, animator);
            VisualVisibility visibility = visual.ResolveVisibility(cullVisible);

            var item = new PrimitiveDrawItem
            {
                MeshAssetId = visual.MeshAssetId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = scale,
                Color = TeamColorResolver.Resolve(World, entity),
                StableId = stableId,
                MaterialId = visual.MaterialId,
                TemplateId = templateId,
                RenderPath = visual.RenderPath,
                Mobility = visual.Mobility,
                Flags = visual.Flags,
                Animator = animator,
                Visibility = visibility,
            };

            if (_snapshotBuffer != null && !_snapshotBuffer.TryAdd(item))
            {
                throw new InvalidOperationException(
                    $"Presentation visual snapshot buffer overflowed while emitting entity #{entity.Id}:{entity.WorldId}. Capacity={_snapshotBuffer.Capacity}.");
            }

            if (visibility == VisualVisibility.Visible)
            {
                _drawBuffer.TryAdd(item);
            }
        }
    }
}
