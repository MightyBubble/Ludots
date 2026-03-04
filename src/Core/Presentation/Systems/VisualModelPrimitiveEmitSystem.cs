using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Emits primitive body geometry from VisualModel + VisualTransform.
    /// This is a minimal runtime fallback before full model/asset renderer integration.
    /// </summary>
    public sealed class VisualModelPrimitiveEmitSystem : BaseSystem<World, float>
    {
        private static int _debugVisualModelLogsRemaining = 8;
        private readonly PrimitiveDrawBuffer _primitives;
        private readonly Func<World, Entity, Vector4> _colorResolver;
        private readonly QueryDescription _withCull = new QueryDescription().WithAll<VisualTransform, VisualModel, CullState>();
        private readonly QueryDescription _noCull = new QueryDescription().WithAll<VisualTransform, VisualModel>().WithNone<CullState>();

        public VisualModelPrimitiveEmitSystem(World world, PrimitiveDrawBuffer primitives, Func<World, Entity, Vector4> colorResolver = null)
            : base(world)
        {
            _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
            _colorResolver = colorResolver ?? ((_, _) => new Vector4(0.9f, 0.9f, 0.9f, 1f));
        }

        public override void Update(in float dt)
        {
            int emitted = 0;
            emitted += EmitQuery(_noCull, requireCullCheck: false);
            emitted += EmitQuery(_withCull, requireCullCheck: true);

            if (_debugVisualModelLogsRemaining > 0)
            {
                _debugVisualModelLogsRemaining--;
                // #region agent log
                File.AppendAllText("/opt/cursor/logs/debug.log", JsonSerializer.Serialize(new
                {
                    hypothesisId = "H1",
                    location = "VisualModelPrimitiveEmitSystem:Update",
                    message = "VisualModel primitive emit summary",
                    data = new { emitted },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + "\n");
                // #endregion
            }
        }

        private int EmitQuery(QueryDescription query, bool requireCullCheck)
        {
            int emitted = 0;
            var q = World.Query(in query);
            foreach (var chunk in q)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var models = chunk.GetArray<VisualModel>();
                var culls = requireCullCheck ? chunk.GetArray<CullState>() : null;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (requireCullCheck && culls != null && !culls[i].IsVisible) continue;

                    var model = models[i];
                    int meshId = model.MeshId <= 0 ? PrimitiveMeshAssetIds.Cube : model.MeshId;
                    float baseScale = model.BaseScale <= 0f ? 0.85f : model.BaseScale;

                    var s = transforms[i].Scale;
                    if (s.X == 0f && s.Y == 0f && s.Z == 0f)
                        s = Vector3.One;
                    s *= baseScale;

                    var pos = transforms[i].Position;
                    pos.Y += s.Y * 0.5f;
                    var entity = chunk.Entity(i);

                    _primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = meshId,
                        Position = pos,
                        Scale = s,
                        Color = _colorResolver(World, entity)
                    });
                    emitted++;
                }
            }
            return emitted;
        }
    }
}
