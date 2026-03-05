using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Universal3CCameraMod.Triggers
{
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
            var session = context.Get<GameSession>(ContextKeys.GameSession);
            if (session == null) return Task.CompletedTask;
            if (session.Camera.Controller != null) return Task.CompletedTask;

            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var vertexMap = context.Get<VertexMap>(ContextKeys.VertexMap);
            if (vertexMap != null)
            {
                int cellsW = vertexMap.WidthInChunks * VertexChunk.ChunkSize;
                int cellsH = vertexMap.HeightInChunks * VertexChunk.ChunkSize;
                float mapW = cellsW * HexCoordinates.HexWidth;
                float mapH = cellsH * HexCoordinates.RowSpacing;

                var centerCm = new Vector2(mapW * 0.5f, mapH * 0.5f) * 100f;
                session.Camera.State.TargetCm = centerCm;

                float baseDistCm = MathF.Min(mapW, mapH) * 100f * 1.2f;
                session.Camera.State.DistanceCm = MathF.Max(5000f, MathF.Min(200000f, baseDistCm));
            }
            else
            {
                session.Camera.State.TargetCm = Vector2.Zero;
                session.Camera.State.DistanceCm = 60000f;
            }

            session.Camera.State.Yaw = 35f;
            session.Camera.State.Pitch = 60f;

            engine.GlobalContext[ContextKeys.CameraControllerRequest] = new CameraControllerRequest
            {
                Id = CameraControllerIds.Orbit3C,
                Config = new Orbit3CCameraConfig
                {
                    EnablePan = false
                }
            };

            _context.Log("[Universal3CCameraMod] Requested Orbit3C camera controller");
            return Task.CompletedTask;
        }
    }
}
