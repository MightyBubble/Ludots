using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.SceneKit
{
    /// <summary>
    /// 引擎检视视图的轨道相机（调试工具）：左键拖拽旋转、滚轮缩放、WASD/方向键平移、R 复位。
    /// 只服务引擎侧的场景检视与取证，不参与游戏运行——游戏运行时的相机、角色控制与
    /// 输入路由由 Ludots 世界侧接管（Core 虚拟相机 + 指令系统 + 宿主输入路由）。
    /// 初始位姿来自关卡容器的 camera 声明，R 键复位到最近一次设定的默认值。
    /// </summary>
    public sealed class EngineOrbitCamera
    {
        private float _defaultYawDeg = 45f;
        private float _defaultPitchDeg = 25f;
        private float _defaultDistance = 40f;
        private float _defaultFovy = 45f;
        private Vector3 _defaultTarget = Vector3.Zero;
        private MouseButton _rotateButton = MouseButton.MOUSE_LEFT_BUTTON;
        private float _yawDeg;
        private float _pitchDeg;
        private float _distance = 40f;
        private float _fovy = 45f;
        private Vector3 _target = Vector3.Zero;
        private Vector2 _lastMouse;

        public Camera3D Camera { get; private set; }

        public Vector3 Target => _target;

        public EngineOrbitCamera(
            float distance = 40f,
            float pitchDeg = 25f,
            float yawDeg = 45f,
            Vector3 target = default,
            float fovy = 45f,
            MouseButton rotateButton = MouseButton.MOUSE_LEFT_BUTTON)
        {
            _rotateButton = rotateButton;
            _defaultDistance = distance;
            _defaultPitchDeg = pitchDeg;
            _defaultYawDeg = yawDeg;
            _defaultTarget = target;
            _defaultFovy = fovy;
            ResetToDefaults();
        }

        public void Reset(float distance, float pitchDeg, float yawDeg, Vector3 target, float fovy = 45f)
        {
            _defaultDistance = distance;
            _defaultPitchDeg = pitchDeg;
            _defaultYawDeg = yawDeg;
            _defaultTarget = target;
            _defaultFovy = fovy;
            ResetToDefaults();
        }

        /// <summary>编辑态切换旋转键：Edit 用右键（左键留给拾取/gizmo），检视用左键。</summary>
        public void UseRotateButton(MouseButton button)
        {
            _rotateButton = button;
        }

        public void Update(float deltaSeconds)
        {
            const float panSpeed = 18f;
            const float zoomFactor = 1.1f;

            if (Rl.IsMouseButtonDown(_rotateButton))
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
            if (Rl.IsKeyPressed(KeyboardKey.KEY_R)) ResetToDefaults();

            Rebuild();
        }

        private void ResetToDefaults()
        {
            _distance = _defaultDistance;
            _pitchDeg = _defaultPitchDeg;
            _yawDeg = _defaultYawDeg;
            _target = _defaultTarget;
            _fovy = _defaultFovy;
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
                fovy = _fovy,
                projection = CameraProjection.CAMERA_PERSPECTIVE,
            };
        }
    }
}
