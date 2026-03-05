using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    public sealed class GrabDragPanBehavior : ICameraBehavior
    {
        private readonly string _holdActionId;
        private readonly string _pointerPosActionId;
        private bool _isDragging;
        private Vector2 _lastPointerPos;

        public GrabDragPanBehavior(string holdActionId, string pointerPosActionId)
        {
            _holdActionId = holdActionId ?? "OrbitRotateHold";
            _pointerPosActionId = pointerPosActionId ?? "PointerPos";
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;

            bool hold = ctx.Input.ReadAction<bool>(_holdActionId);
            Vector2 pointerPos = ctx.Input.ReadAction<Vector2>(_pointerPosActionId);

            if (hold)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _lastPointerPos = pointerPos;
                }
                else
                {
                    Vector2 delta = pointerPos - _lastPointerPos;
                    _lastPointerPos = pointerPos;

                    if (MathF.Abs(delta.X) < 0.01f && MathF.Abs(delta.Y) < 0.01f) return;

                    float fovRad = state.FovYDeg * (MathF.PI / 180f);
                    float viewHeight = ctx.Viewport.Resolution.Y;
                    if (viewHeight < 1f) return;

                    float worldPerPixel = 2f * (state.DistanceCm * 0.01f) * MathF.Tan(fovRad * 0.5f) / viewHeight;
                    float cmPerPixel = worldPerPixel * 100f;

                    Vector2 right = OrbitCameraDirectionUtil.RightFromYawDegrees(state.Yaw);
                    Vector2 fwd = OrbitCameraDirectionUtil.ForwardFromYawDegrees(state.Yaw);

                    state.TargetCm -= (right * delta.X + fwd * delta.Y) * cmPerPixel;
                }
            }
            else
            {
                _isDragging = false;
            }
        }
    }
}
