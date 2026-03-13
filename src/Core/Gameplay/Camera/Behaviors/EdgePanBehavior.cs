using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class EdgePanBehavior : ICameraBehavior
    {
        private readonly string _pointerPosActionId;
        private readonly float _marginPx;
        private readonly float _speedCmPerSec;
        private readonly bool _requirePointerInsideViewport;

        public EdgePanBehavior(string pointerPosActionId, float marginPx, float speedCmPerSec, bool requirePointerInsideViewport)
        {
            _pointerPosActionId = pointerPosActionId ?? "PointerPos";
            _marginPx = MathF.Max(1f, marginPx);
            _speedCmPerSec = speedCmPerSec;
            _requirePointerInsideViewport = requirePointerInsideViewport;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;

            Vector2 mousePos = ctx.Input.ReadAction<Vector2>(_pointerPosActionId);
            Vector2 res = ctx.Viewport.Resolution;
            if (res.X < 1f || res.Y < 1f) return;
            if (_requirePointerInsideViewport &&
                (mousePos.X < 0f || mousePos.Y < 0f || mousePos.X > res.X || mousePos.Y > res.Y))
            {
                return;
            }

            float edgeX = 0f;
            float edgeY = 0f;

            if (mousePos.X < _marginPx) edgeX = -1f;
            else if (mousePos.X > res.X - _marginPx) edgeX = 1f;

            if (mousePos.Y < _marginPx) edgeY = 1f;
            else if (mousePos.Y > res.Y - _marginPx) edgeY = -1f;

            if (MathF.Abs(edgeX) < 0.001f && MathF.Abs(edgeY) < 0.001f) return;

            var moveInput = new Vector2(edgeX, edgeY);
            if (moveInput.LengthSquared() > 1f)
                moveInput = Vector2.Normalize(moveInput);

            Vector2 dir = OrbitCameraDirectionUtil.MoveInputToDirection(state.Yaw, moveInput);
            if (dir.LengthSquared() > 0.0001f)
                state.TargetCm += dir * (_speedCmPerSec * dt);
        }
    }
}
