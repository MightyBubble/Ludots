using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class EdgePanBehavior : ICameraBehavior
    {
        private readonly float _marginPx;
        private readonly float _speedCmPerSec;
        private readonly bool _requirePointerInsideViewport;
        private bool _interiorArmed;

        public EdgePanBehavior(float marginPx, float speedCmPerSec, bool requirePointerInsideViewport)
        {
            if (!float.IsFinite(marginPx) || marginPx <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(marginPx), "Edge pan margin must be finite and > 0.");
            }

            _marginPx = marginPx;
            _speedCmPerSec = speedCmPerSec;
            _requirePointerInsideViewport = requirePointerInsideViewport;
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;
            if (!ctx.BehaviorInput.PointerActive)
            {
                _interiorArmed = false;
                return;
            }

            Vector2 mousePos = ctx.BehaviorInput.PointerPosition;
            Vector2 res = ctx.Viewport.Resolution;
            if (res.X < 1f || res.Y < 1f) return;

            bool insideViewport = mousePos.X >= 0f && mousePos.Y >= 0f && mousePos.X <= res.X && mousePos.Y <= res.Y;
            if (_requirePointerInsideViewport && !insideViewport)
            {
                _interiorArmed = false;
                return;
            }

            float edgeX = 0f;
            float edgeY = 0f;

            if (mousePos.X < _marginPx) edgeX = -1f;
            else if (mousePos.X > res.X - _marginPx) edgeX = 1f;

            if (mousePos.Y < _marginPx) edgeY = 1f;
            else if (mousePos.Y > res.Y - _marginPx) edgeY = -1f;

            // Window focus and cursor-enter always land on the rim first. That is not
            // an edge-pan command until the pointer has been in the playfield once.
            if (_requirePointerInsideViewport)
            {
                bool onEdge = MathF.Abs(edgeX) >= 0.001f || MathF.Abs(edgeY) >= 0.001f;
                if (!onEdge && insideViewport)
                {
                    _interiorArmed = true;
                }
                else if (!_interiorArmed)
                {
                    return;
                }
            }

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
