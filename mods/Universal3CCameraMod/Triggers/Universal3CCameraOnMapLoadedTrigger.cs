using System;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Gameplay;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Universal3CCameraMod.Triggers
{
    /// <summary>
    /// Centers the camera on the vertex map when present.
    /// Camera controller and state values are handled by the preset system (ApplyDefaultCamera).
    /// </summary>
    public sealed class Universal3CCameraOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _context;

        public Universal3CCameraOnMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var session = context.Get(CoreServiceKeys.GameSession);
            if (session == null) return Task.CompletedTask;

            var vertexMap = context.Get(CoreServiceKeys.VertexMap);
            if (vertexMap == null) return Task.CompletedTask;

            int cellsW = vertexMap.WidthInChunks * VertexChunk.ChunkSize;
            int cellsH = vertexMap.HeightInChunks * VertexChunk.ChunkSize;
            float mapW = cellsW * HexCoordinates.HexWidth;
            float mapH = cellsH * HexCoordinates.RowSpacing;

            session.Camera.State.TargetCm = new Vector2(mapW * 0.5f, mapH * 0.5f) * 100f;

            float baseDistCm = MathF.Min(mapW, mapH) * 100f * 1.2f;
            session.Camera.State.DistanceCm = MathF.Max(5000f, MathF.Min(200000f, baseDistCm));

            _context.Log("[Universal3CCameraMod] Camera centered on vertex map");
            return Task.CompletedTask;
        }
    }
}
