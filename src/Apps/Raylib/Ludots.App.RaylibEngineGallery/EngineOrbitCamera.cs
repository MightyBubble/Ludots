using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.App.RaylibEngineGallery
{
    /// <summary>
    /// 画廊内置轨道相机：左键拖拽旋转、滚轮缩放、WASD/方向键平移、R 复位。
    /// </summary>
    public sealed class EngineOrbitCamera
    {
        private float _yawDeg;
        private float _pitchDeg;
        private float _distance = 40f;
        private Vector3 _target = Vector3.Zero;
        private Vector2 _lastMouse;

        public Camera3D Camera { get; private set; }

        public Vector3 Target => _target;

        public EngineOrbitCamera(float distance = 40f, float pitchDeg = 25f, float yawDeg = 45f)
        {
            _distance = distance;
            _pitchDeg = pitchDeg;
            _yawDeg = yawDeg;
            Rebuild();
        }

        public void Reset(float distance, float pitchDeg, float yawDeg, Vector3 target)
        {
            _distance = distance;
            _pitchDeg = pitchDeg;
            _yawDeg = yawDeg;
            _target = target;
            Rebuild();
        }

        public void Update(float deltaSeconds)
        {
            const float panSpeed = 18f;
            const float zoomFactor = 1.1f;

            if (Rl.IsMouseButtonDown(MouseButton.MOUSE_LEFT_BUTTON))
            {
                Vector2 mouse = Rl.GetMousePosition();
                Vector2 delta = mouse - _lastMouse;
                _yawDeg -= delta.X * 0.3f;
                _pitchDeg = Math.Clamp(_pitchDeg + delta.Y * 0.3f, 5f, 85f);
                _lastMouse = mouse;
            }
            else
            {
                _lastMouse = Rl.GetMousePosition();
            }

            float wheel = Rl.GetMouseWheelMove();
            if (wheel != 0f)
            {
                _distance = Math.Max(2f, _distance / MathF.Pow(zoomFactor, wheel));
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
            if (Rl.IsKeyPressed(KeyboardKey.KEY_R)) RebuildDefaults();

            Rebuild();
        }

        private void RebuildDefaults()
        {
            _distance = 40f;
            _pitchDeg = 25f;
            _yawDeg = 45f;
            _target = Vector3.Zero;
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
