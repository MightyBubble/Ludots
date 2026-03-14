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

        private readonly QueryDescription _visibleQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState, CullState>();

        private readonly QueryDescription _unculledQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualRuntimeState>()
            .WithNone<CullState>();

        public EntityVisualEmitSystem(World world, PrimitiveDrawBuffer drawBuffer)
            : base(world)
        {
            _drawBuffer = drawBuffer;
        }

        public override void Update(in float dt)
        {
            EmitVisible();
            EmitUnculled();
        }

        private void EmitVisible()
        {
            var query = World.Query(in _visibleQuery);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var visuals = chunk.GetArray<VisualRuntimeState>();
                var culls = chunk.GetArray<CullState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible)
                    {
                        continue;
                    }

                    Emit(chunk.Entity(i), visuals[i], transforms[i]);
                }
            }
        }

        private void EmitUnculled()
        {
            var query = World.Query(in _unculledQuery);
            foreach (var chunk in query)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var visuals = chunk.GetArray<VisualRuntimeState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(chunk.Entity(i), visuals[i], transforms[i]);
                }
            }
        }

        private void Emit(Entity entity, in VisualRuntimeState visual, in VisualTransform transform)
        {
            if (!visual.ShouldEmit)
            {
                return;
            }

            float baseScale = visual.BaseScale <= 0f ? 1f : visual.BaseScale;
            var scale = transform.Scale * baseScale;
            int stableId = World.Has<PresentationStableId>(entity) ? World.Get<PresentationStableId>(entity).Value : 0;
            int templateId = World.Has<VisualTemplateRef>(entity) ? World.Get<VisualTemplateRef>(entity).TemplateId : 0;
            bool hasAnimatorComponent = World.Has<AnimatorPackedState>(entity);
            AnimatorPackedState animator = hasAnimatorComponent ? World.Get<AnimatorPackedState>(entity) : default;
            PresentationRenderContract.ValidateRuntimeState("EntityVisualEmitSystem", visual, hasAnimatorComponent, animator);

            _drawBuffer.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = visual.MeshAssetId,
                Position = transform.Position,
                Scale = scale,
                Color = TeamColorResolver.Resolve(World, entity),
                StableId = stableId,
                MaterialId = visual.MaterialId,
                TemplateId = templateId,
                RenderPath = visual.RenderPath,
                Mobility = visual.Mobility,
                Flags = visual.Flags,
                Animator = animator,
            });
        }
    }
}
