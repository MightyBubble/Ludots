using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CameraBootstrapMod.Triggers
{
    /// <summary>
    /// Centers the runtime camera on the currently loaded map bounds when the map opts in via tag.
    /// Vertex map bounds are preferred; otherwise spawned map entities define the bootstrap extents.
    /// </summary>
    public sealed class CameraBootstrapOnMapLoadedTrigger : Trigger
    {
        private const string CenterBoundsTag = "camera.bootstrap.center_bounds";
        private readonly IModContext _context;

        public CameraBootstrapOnMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, CenterBoundsTag))
            {
                return Task.CompletedTask;
            }

            var engine = context.GetEngine();
            if (engine == null || !TryResolveBounds(context, out var targetCm, out var distanceCm))
            {
                return Task.CompletedTask;
            }

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                TargetCm = targetCm,
                DistanceCm = distanceCm
            });

            _context.Log($"[CameraBootstrapMod] Centered camera from tag '{CenterBoundsTag}' at {targetCm} distance={distanceCm}");
            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string expected)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveBounds(ScriptContext context, out Vector2 targetCm, out float distanceCm)
        {
            var vertexMap = context.Get(CoreServiceKeys.VertexMap);
            if (vertexMap != null)
            {
                int cellsW = vertexMap.WidthInChunks * VertexChunk.ChunkSize;
                int cellsH = vertexMap.HeightInChunks * VertexChunk.ChunkSize;
                float mapW = cellsW * HexCoordinates.HexWidth;
                float mapH = cellsH * HexCoordinates.RowSpacing;

                targetCm = new Vector2(mapW * 0.5f, mapH * 0.5f) * 100f;
                float baseDistCm = MathF.Min(mapW, mapH) * 100f * 1.2f;
                distanceCm = MathF.Max(5000f, MathF.Min(200000f, baseDistCm));
                return true;
            }

            return TryResolveEntityBounds(context, out targetCm, out distanceCm);
        }

        private static bool TryResolveEntityBounds(ScriptContext context, out Vector2 targetCm, out float distanceCm)
        {
            targetCm = Vector2.Zero;
            distanceCm = 0f;

            var world = context.GetWorld();
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (world == null || mapId == null)
            {
                return false;
            }

            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            bool found = false;
            var query = new QueryDescription().WithAll<MapEntity, WorldPositionCm>();
            world.Query(in query, (Entity entity, ref MapEntity mapEntity, ref WorldPositionCm position) =>
            {
                if (mapEntity.MapId != mapId)
                {
                    return;
                }

                var point = position.Value.ToVector2();
                if (!found)
                {
                    minX = maxX = point.X;
                    minY = maxY = point.Y;
                    found = true;
                    return;
                }

                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            });

            if (!found)
            {
                return false;
            }

            targetCm = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float spanCm = MathF.Max(1000f, MathF.Max(maxX - minX, maxY - minY));
            distanceCm = MathF.Max(4000f, MathF.Min(50000f, spanCm * 1.35f));
            return true;
        }
    }
}
