using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.UE5
{
    public sealed class UE5ScreenRayProvider : IScreenRayProvider
    {
        private readonly UE5SharedCameraState _state;

        public UE5ScreenRayProvider(UE5SharedCameraState state) => _state = state;

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            float w = _state.ViewportWidth;
            float h = _state.ViewportHeight;
            if (w <= 0f || h <= 0f)
            {
                return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
            }

            var cam = _state.ReadCameraState();
            if (!_IsRenderableCamera(cam))
            {
                float fallbackFov = _state.ReadCameraFovYDeg();
                cam = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, fallbackFov);
            }

            return CameraViewportUtil.ScreenToRay(
                screenPosition,
                cam,
                new Vector2(w, h),
                w / MathF.Max(1f, h));
        }

        private static bool _IsRenderableCamera(in CameraRenderState3D state)
        {
            if (!float.IsFinite(state.FovYDeg) || state.FovYDeg <= 1f || state.FovYDeg >= 179f)
            {
                return false;
            }

            if (!_IsFiniteVector(state.Position) || !_IsFiniteVector(state.Target) || !_IsFiniteVector(state.Up))
            {
                return false;
            }

            if (Vector3.DistanceSquared(state.Position, state.Target) < 1e-6f)
            {
                return false;
            }

            return state.Up.LengthSquared() >= 1e-6f;
        }

        private static bool _IsFiniteVector(in Vector3 value)
        {
            return float.IsFinite(value.X)
                && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
        }
    }
}
