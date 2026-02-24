using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;

namespace TerrainBenchmarkMod.Systems
{
    public sealed class TerrainBenchmarkCameraConfig
    {
        public Vector2 CenterCm { get; set; } = Vector2.Zero;
        public float AutoRadiusCm { get; set; } = 60000f;

        public string MoveActionId { get; set; } = "Move";
        public string ZoomActionId { get; set; } = "Zoom";

        public float MoveCmPerSecond { get; set; } = 200f;
        public float ZoomCmPerWheel { get; set; } = 2000f;
        public float MinDistanceCm { get; set; } = 5000f;
        public float MaxDistanceCm { get; set; } = 90000f;
        public float AutoXHz { get; set; } = 0.15f;
        public float AutoYHz { get; set; } = 0.12f;
    }

    public sealed class TerrainBenchmarkCameraController : ICameraController
    {
        private readonly TerrainBenchmarkCameraConfig _config;
        private readonly PlayerInputHandler _input;
        private float _t;

        public TerrainBenchmarkCameraController(TerrainBenchmarkCameraConfig config, PlayerInputHandler input)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _input = input;
        }

        public void Update(CameraState state, float dt)
        {
            _t += dt;
            var auto = new Vector2(MathF.Sin(_t * _config.AutoXHz), MathF.Cos(_t * _config.AutoYHz)) * _config.AutoRadiusCm;
            state.TargetCm = _config.CenterCm + auto;

            var move = _input.ReadAction<Vector2>(_config.MoveActionId);
            if (move.LengthSquared() > 0)
            {
                float step = _config.MoveCmPerSecond * dt;
                state.TargetCm += Vector2.Normalize(move) * step;
            }

            var zoom = _input.ReadAction<float>(_config.ZoomActionId);
            if (Math.Abs(zoom) > 0.01f)
            {
                state.DistanceCm -= zoom * _config.ZoomCmPerWheel;
                state.DistanceCm = Math.Clamp(state.DistanceCm, _config.MinDistanceCm, _config.MaxDistanceCm);
            }
        }
    }
}
