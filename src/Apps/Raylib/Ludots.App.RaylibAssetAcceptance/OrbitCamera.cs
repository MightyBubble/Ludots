using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibAssetAcceptance
{
    /// <summary>验收台轨道相机：左键旋转、滚轮缩放、WASD/方向键平移、R 复位到资产对位视角。</summary>
    public sealed class OrbitCamera
    {
        private float _yawDeg = 35f;
        private float _pitchDeg = 18f;
        private float _distance = 8f;
        private Vector3 _target = new(0f, 1.4f, 0f);
        private Vector2 _lastMouse;

        public Camera3D Camera { get; private set; }

        public OrbitCamera()
        {
            Rebuild();
        }

        public void ResetToFit(float modelHeight)
        {
            _yawDeg = 35f;
            _pitchDeg = 18f;
            _distance = MathF.Max(6f, modelHeight * 2.6f);
            _target = new Vector3(0f, modelHeight * 0.5f, 0f);
            Rebuild();
        }

        public void Update(float deltaSeconds)
        {
            const float panSpeed = 4f;
            const float zoomFactor = 1.1f;

            if (Rl.IsMouseButtonDown(MouseButton.MOUSE_LEFT_BUTTON))
            {
                Vector2 mouse = Rl.GetMousePosition();
                Vector2 delta = mouse - _lastMouse;
                _yawDeg -= delta.X * 0.3f;
                _pitchDeg = Math.Clamp(_pitchDeg + delta.Y * 0.3f, 3f, 85f);
                _lastMouse = mouse;
            }
            else
            {
                _lastMouse = Rl.GetMousePosition();
            }

            float wheel = Rl.GetMouseWheelMove();
            if (wheel != 0f)
            {
                _distance = Math.Max(1.5f, _distance / MathF.Pow(zoomFactor, wheel));
            }

            Vector3 forward = new(
                MathF.Cos(_yawDeg * MathF.PI / 180f) * MathF.Cos(_pitchDeg * MathF.PI / 180f),
                MathF.Sin(_pitchDeg * MathF.PI / 180f),
                MathF.Sin(_yawDeg * MathF.PI / 180f) * MathF.Cos(_pitchDeg * MathF.PI / 180f));
            Vector3 flatForward = Vector3.Normalize(new Vector3(forward.X, 0f, forward.Z));
            Vector3 flatRight = new(-flatForward.Z, 0f, flatForward.X);

            if (Rl.IsKeyDown(KeyboardKey.KEY_W) || Rl.IsKeyDown(KeyboardKey.KEY_UP)) _target += flatForward * panSpeed * deltaSeconds;
            if (Rl.IsKeyDown(KeyboardKey.KEY_S) || Rl.IsKeyDown(KeyboardKey.KEY_DOWN)) _target -= flatForward * panSpeed * deltaSeconds;
            if (Rl.IsKeyDown(KeyboardKey.KEY_A) || Rl.IsKeyDown(KeyboardKey.KEY_LEFT)) _target -= flatRight * panSpeed * deltaSeconds;
            if (Rl.IsKeyDown(KeyboardKey.KEY_D) || Rl.IsKeyDown(KeyboardKey.KEY_RIGHT)) _target += flatRight * panSpeed * deltaSeconds;

            Rebuild();
        }

        private void Rebuild()
        {
            Camera = new Camera3D
            {
                position = new Vector3(
                    _target.X + _distance * MathF.Cos(_yawDeg * MathF.PI / 180f) * MathF.Cos(_pitchDeg * MathF.PI / 180f),
                    _target.Y + _distance * MathF.Sin(_pitchDeg * MathF.PI / 180f),
                    _target.Z + _distance * MathF.Sin(_yawDeg * MathF.PI / 180f) * MathF.Cos(_pitchDeg * MathF.PI / 180f)),
                target = _target,
                up = Vector3.UnitY,
                fovy = 45f,
                projection = CameraProjection.CAMERA_PERSPECTIVE,
            };
        }
    }
}
