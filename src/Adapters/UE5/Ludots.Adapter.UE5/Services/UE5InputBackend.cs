using System;
using System.Numerics;
using Ludots.Core.Diagnostics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 平台输入后端（对标 RaylibInputBackend）。
    ///
    /// UE5 C# 侧每帧将轴/按钮/鼠标状态推入
    /// <see cref="UE5SharedCameraState"/>，Ludots <see cref="PlayerInputHandler"/>
    /// 通过此接口轮询，驱动游戏内输入响应链。
    ///
    /// <b>鼠标坐标语义</b>：直接返回 UE 屏幕坐标（<c>MouseX/MouseY</c>），
    /// <see cref="PlayerInputHandler.RefreshPointerState"/> 通过 <c>currentPos - prevPos</c>
    /// 差分自动产生 <c>_mouseDelta</c>。
    /// 在 <c>bShowMouseCursor=true</c> 模式下，UE 的 <c>GetMousePosition</c> 返回真实光标位置，
    /// 差分结果即为帧间位移。
    /// </summary>
    public sealed class UE5InputBackend : IInputBackend
    {
        private readonly UE5SharedCameraState _state;

        // 节流日志
        private int _logFrameCounter;
        private const int LogInterval = 120;

        public UE5InputBackend(UE5SharedCameraState state) => _state = state;

        public float GetAxis(string devicePath)   => _state.GetAxis(devicePath);
        public bool  GetButton(string devicePath) => _state.GetButton(devicePath);

        /// <summary>
        /// 返回 UE 屏幕坐标（绝对坐标语义）。
        /// <see cref="PlayerInputHandler.RefreshPointerState"/> 每帧调用一次，
        /// 通过 <c>currentPos - prevPos</c> 自动产生正确的 delta。
        /// </summary>
        public Vector2 GetMousePosition()
        {
            var pos = _state.HasMousePosition
                ? new Vector2(_state.MouseX, _state.MouseY)
                : new Vector2(float.NaN, float.NaN);

            _logFrameCounter++;
            if (_logFrameCounter >= LogInterval)
            {
                _logFrameCounter = 0;
            }

            return pos;
        }

        /// <summary>返回鼠标屏幕绝对坐标（用于 Hover/命中检测等非 delta 场景）。</summary>
        public Vector2 GetMouseScreenPosition() => _state.HasMousePosition
            ? new Vector2(_state.MouseX, _state.MouseY)
            : new Vector2(-1f, -1f);

        public float GetMouseWheel() => _state.MouseWheelDelta;

        // IME 暂不支持
        public void   EnableIME(bool enable)             { }
        public void   SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer()                    => string.Empty;
    }
}