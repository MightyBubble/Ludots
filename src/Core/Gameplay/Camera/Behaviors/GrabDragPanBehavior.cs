using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.Behaviors
{
    internal sealed class GrabDragPanBehavior : ICameraBehavior
    {
        private readonly string _holdActionId;
        private readonly string _pointerDeltaActionId;

        public GrabDragPanBehavior(string holdActionId, string pointerDeltaActionId)
        {
            _holdActionId = holdActionId ?? "OrbitRotateHold";
            _pointerDeltaActionId = pointerDeltaActionId ?? "PointerDelta";
        }

        public void Update(CameraState state, CameraBehaviorContext ctx, float dt)
        {
            if (state.IsFollowing || dt <= 0f) return;

            bool hold = ctx.Input.ReadAction<bool>(_holdActionId);
            if (!hold)
            {
                return;
            }

            Vector2 delta = ctx.Input.ReadAction<Vector2>(_pointerDeltaActionId);
            if (MathF.Abs(delta.X) < 0.01f && MathF.Abs(delta.Y) < 0.01f)
            {
                return;
            }

            float fovRad = state.FovYDeg * (MathF.PI / 180f);
            float viewHeight = ctx.Viewport.Resolution.Y;
            if (viewHeight < 1f) return;

            float worldPerPixel = 2f * (state.DistanceCm * 0.01f) * MathF.Tan(fovRad * 0.5f) / viewHeight;
            float cmPerPixel = worldPerPixel * 100f;

            Vector2 right = OrbitCameraDirectionUtil.RightFromYawDegrees(state.Yaw);
            Vector2 fwd = OrbitCameraDirectionUtil.ForwardFromYawDegrees(state.Yaw);

            state.TargetCm += (right * delta.X + fwd * delta.Y) * cmPerPixel;
        }
    }
}
