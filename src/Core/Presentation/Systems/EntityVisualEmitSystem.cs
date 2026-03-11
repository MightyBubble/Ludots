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
            .WithAll<VisualTransform, VisualModel, CullState>();

        private readonly QueryDescription _unculledQuery = new QueryDescription()
            .WithAll<VisualTransform, VisualModel>()
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
                var visuals = chunk.GetArray<VisualModel>();
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
                var visuals = chunk.GetArray<VisualModel>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    Emit(chunk.Entity(i), visuals[i], transforms[i]);
                }
            }
        }

        private void Emit(Entity entity, in VisualModel visual, in VisualTransform transform)
        {
            if (visual.MeshId <= 0)
            {
                return;
            }

            float baseScale = visual.BaseScale <= 0f ? 1f : visual.BaseScale;
            var scale = transform.Scale * baseScale;

            _drawBuffer.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = visual.MeshId,
                Position = transform.Position,
                Scale = scale,
                Color = TeamColorResolver.Resolve(World, entity)
            });
        }
    }
}
