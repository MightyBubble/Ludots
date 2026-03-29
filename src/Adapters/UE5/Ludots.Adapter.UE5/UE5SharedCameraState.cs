using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 平台适配器的共享状态容器。
    ///
    /// 写入方：UE5 C# 脚本（GameThread），每帧调用各 Push/Set 方法更新。
    /// 读取方：Ludots 核心系统（通过适配器接口），在引擎 Tick 中读取。
    ///
    /// 相机状态使用 lock 保护；输入状态（鼠标/按钮）也使用同一 lock，
    /// 保证跨线程安全。
    /// </summary>
    public sealed class UE5SharedCameraState
    {
        private readonly object _lock = new();

        // ── 相机 ─────────────────────────────────────────────────────

        /// <summary>Ludots CameraRenderState3D（由 CameraPresenter 通过 UpdateCamera 推入，
        /// 或由 UE5 PlayerCameraManager 直接推入以驱动 Ludots 相机）。</summary>
        /// <summary>
        /// Render camera snapshot published by Ludots. UE consumes this snapshot as output only.
        /// </summary>
        private CameraRenderState3D _cameraState;
        private CameraStateSnapshot _logicalCameraState;
        private bool _hasLogicalCameraState;

        public void PushCameraState(CameraRenderState3D state) { lock (_lock) _cameraState = state; }
        public CameraRenderState3D ReadCameraState()           { lock (_lock) return _cameraState; }

        public void PushLogicalCameraState(CameraStateSnapshot state)
        {
            lock (_lock)
            {
                _logicalCameraState = state;
                _hasLogicalCameraState = true;
            }
        }

        public bool TryReadLogicalCameraState(out CameraStateSnapshot state)
        {
            lock (_lock)
            {
                state = _logicalCameraState;
                return _hasLogicalCameraState;
            }
        }

        // ── 视口 ─────────────────────────────────────────────────────
        // C# 中 volatile 不支持 float，统一用 _lock 保护。

        private float _viewportWidth  = 1920f;
        private float _viewportHeight = 1080f;

        /// <summary>UE5 Viewport 宽度（像素）。线程安全属性。</summary>
        public float ViewportWidth
        {
            get { lock (_lock) return _viewportWidth;  }
            set { lock (_lock) _viewportWidth  = value; }
        }

        /// <summary>UE5 Viewport 高度（像素）。线程安全属性。</summary>
        public float ViewportHeight
        {
            get { lock (_lock) return _viewportHeight;  }
            set { lock (_lock) _viewportHeight  = value; }
        }

        /// <summary>垂直 FOV（度）。线程安全属性。</summary>
        public float ReadCameraFovYDeg(float fallbackFovYDeg = 60f)
        {
            lock (_lock)
            {
                return IsRenderableFov(_cameraState.FovYDeg)
                    ? _cameraState.FovYDeg
                    : fallbackFovYDeg;
            }
        }

        // ── 鼠标 ─────────────────────────────────────────────────────

        private float _mouseX;
        private float _mouseY;
        private bool _hasMousePosition;
        private float _mouseDeltaX;
        private float _mouseDeltaY;
        private float _mouseWheelDelta;

        /// <summary>鼠标屏幕 X 坐标（像素）。线程安全属性。</summary>
        public float MouseX
        {
            get { lock (_lock) return _mouseX;  }
            set { lock (_lock) _mouseX  = value; }
        }

        /// <summary>鼠标屏幕 Y 坐标（像素）。线程安全属性。</summary>
        public float MouseY
        {
            get { lock (_lock) return _mouseY;  }
            set { lock (_lock) _mouseY  = value; }
        }

        /// <summary>当前帧是否持有可被 Ludots 消费的有效视口鼠标坐标。</summary>
        public bool HasMousePosition
        {
            get { lock (_lock) return _hasMousePosition; }
            set { lock (_lock) _hasMousePosition = value; }
        }

        /// <summary>
        /// 本帧鼠标原始 X 位移（像素，由 UE GetInputMouseDelta 提供）。
        /// 线程安全属性；每帧末由 <see cref="FlushTransient"/> 清零。
        /// 应优先用于旋转/拖拽行为，而非通过 MouseX/Y 差分计算，
        /// 以避免在 ShowMouseCursor 状态切换或视口中心锁定时 delta=0 的问题。
        /// </summary>
        public float MouseDeltaX
        {
            get { lock (_lock) return _mouseDeltaX;  }
            set { lock (_lock) _mouseDeltaX  = value; }
        }

        /// <summary>
        /// 本帧鼠标原始 Y 位移（像素，由 UE GetInputMouseDelta 提供）。
        /// 线程安全属性；每帧末由 <see cref="FlushTransient"/> 清零。
        /// </summary>
        public float MouseDeltaY
        {
            get { lock (_lock) return _mouseDeltaY;  }
            set { lock (_lock) _mouseDeltaY  = value; }
        }

        /// <summary>本帧鼠标滚轮增量。线程安全属性；每帧末由 <see cref="FlushTransient"/> 清零。</summary>
        public float MouseWheelDelta
        {
            get { lock (_lock) return _mouseWheelDelta;  }
            set { lock (_lock) _mouseWheelDelta  = value; }
        }

        // ── 按钮 / 轴（由 UE5 PlayerController 推入）────────────────

        private readonly Dictionary<string, float> _axes    = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool>  _buttons = new(StringComparer.OrdinalIgnoreCase);

        public void SetAxis(string devicePath, float value)
        {
            lock (_lock) _axes[devicePath] = value;
        }

        public void SetButton(string devicePath, bool pressed)
        {
            lock (_lock) _buttons[devicePath] = pressed;
        }

        public float GetAxis(string devicePath)
        {
            lock (_lock) return _axes.TryGetValue(devicePath, out var v) ? v : 0f;
        }

        public bool GetButton(string devicePath)
        {
            lock (_lock) return _buttons.TryGetValue(devicePath, out var v) && v;
        }

        /// <summary>
        /// 每帧末由 UE5 侧调用，重置一帧有效的瞬态输入（鼠标滚轮等）。
        /// </summary>
        public void FlushTransient()
        {
            lock (_lock)
            {
                _mouseWheelDelta = 0f;
                _mouseDeltaX     = 0f;
                _mouseDeltaY     = 0f;
            }
        }

        private static bool IsRenderableFov(float fovYDeg)
        {
            return float.IsFinite(fovYDeg) && fovYDeg > 1f && fovYDeg < 179f;
        }
    }
}
