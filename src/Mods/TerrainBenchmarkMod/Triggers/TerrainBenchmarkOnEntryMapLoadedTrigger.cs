using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TerrainBenchmarkMod.Systems;

namespace TerrainBenchmarkMod.Triggers
{
    public sealed class TerrainBenchmarkOnEntryMapLoadedTrigger : Trigger
    {
        private readonly IModContext _context;
        private static bool _registered;
        private const string ControllerId = "TerrainBenchmark.AutoOrbit";

        public TerrainBenchmarkOnEntryMapLoadedTrigger(IModContext context)
        {
            _context = context;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapId = context.Get<MapId>(ContextKeys.MapId);
            if (mapId.Value != engine.MergedConfig.StartupMapId) return Task.CompletedTask;

            var session = context.Get<GameSession>(ContextKeys.GameSession);
            var input = context.Get<PlayerInputHandler>(ContextKeys.InputHandler);
            var vertexMap = context.Get<VertexMap>(ContextKeys.VertexMap);
            if (session == null || input == null) return Task.CompletedTask;
            if (session.Camera.Controller != null) return Task.CompletedTask;

            Vector2 center;
            float autoRadius;
            if (vertexMap != null)
            {
                int cellsW = vertexMap.WidthInChunks * VertexChunk.ChunkSize;
                int cellsH = vertexMap.HeightInChunks * VertexChunk.ChunkSize;
                float mapW = cellsW * HexCoordinates.HexWidth;
                float mapH = cellsH * HexCoordinates.RowSpacing;
                center = new Vector2(mapW * 0.5f, mapH * 0.5f);
                autoRadius = MathF.Max(400f, MathF.Min(mapW, mapH) * 0.35f);
                autoRadius = MathF.Max(400f, MathF.Min(2500f, autoRadius));
            }
            else
            {
                center = Vector2.Zero;
                autoRadius = 1200f;
            }

            session.Camera.State.Yaw = 35f;
            session.Camera.State.Pitch = 60f;
            session.Camera.State.DistanceCm = 40000f;

            if (!_registered)
            {
                if (!engine.GlobalContext.TryGetValue(ContextKeys.CameraControllerRegistry, out var obj) || obj is not CameraControllerRegistry registry)
                {
                    throw new System.InvalidOperationException("CameraControllerRegistry is missing.");
                }

                registry.Register(ControllerId, (configObj, services) =>
                {
                    TerrainBenchmarkCameraConfig cfg = configObj switch
                    {
                        null => new TerrainBenchmarkCameraConfig(),
                        TerrainBenchmarkCameraConfig c => c,
                        _ => throw new System.InvalidOperationException($"TerrainBenchmark controller expects config type {nameof(TerrainBenchmarkCameraConfig)}.")
                    };

                    return new TerrainBenchmarkCameraController(cfg, services.Input);
                });

                _registered = true;
            }

            engine.GlobalContext[ContextKeys.CameraControllerRequest] = new CameraControllerRequest
            {
                Id = ControllerId,
                Config = new TerrainBenchmarkCameraConfig
                {
                    CenterCm = center * 100f,
                    AutoRadiusCm = autoRadius * 100f
                }
            };

            _context.Log("[TerrainBenchmarkMod] Camera controller requested");
            return Task.CompletedTask;
        }
    }
}
