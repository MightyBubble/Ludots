using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TerrainBenchmarkMod.Triggers
{
    public sealed class TerrainBenchmarkOnEntryMapLoadedTrigger : Trigger
    {
        private readonly IModContext _context;

        public TerrainBenchmarkOnEntryMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (mapId.Value != engine.MergedConfig.StartupMapId) return Task.CompletedTask;

            var vertexMap = context.Get(CoreServiceKeys.VertexMap);

            Vector2 center;
            float autoRadius;
            if (vertexMap != null)
            {
                int cellsW = vertexMap.WidthInChunks * VertexChunk.ChunkSize;
                int cellsH = vertexMap.HeightInChunks * VertexChunk.ChunkSize;
                float mapW = cellsW * HexCoordinates.HexWidth;
                float mapH = cellsH * HexCoordinates.RowSpacing;
                center = new Vector2(mapW * 0.5f, mapH * 0.5f) * 100f;
                autoRadius = MathF.Max(40000f, MathF.Min(MathF.Min(mapW, mapH) * 35f, 250000f));
            }
            else
            {
                center = Vector2.Zero;
                autoRadius = 120000f;
            }

            engine.SetService(CoreServiceKeys.CameraPresetRequest, new CameraPresetRequest
            {
                PresetId = "TopDown"
            });
            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                TargetCm = center,
                Yaw = 35f,
                Pitch = 60f,
                DistanceCm = MathF.Max(40000f, autoRadius)
            });

            _context.Log("[TerrainBenchmarkMod] Camera preset + pose requested");
            return Task.CompletedTask;
        }
    }
}
