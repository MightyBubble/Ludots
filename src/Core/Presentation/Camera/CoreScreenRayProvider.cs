using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    public sealed class CoreScreenRayProvider : IScreenRayProvider
    {
        private const float NearPlane = 0.1f;
        private const float FarPlane = 10000f;

        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter _presenter;

        public CoreScreenRayProvider(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            if (!TryResolveCamera(out var camera))
            {
                return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
            }

            float resolutionX = Math.Max(_view.Resolution.X, 1f);
            float resolutionY = Math.Max(_view.Resolution.Y, 1f);
            float ndcX = (screenPosition.X / resolutionX) * 2f - 1f;
            float ndcY = 1f - (screenPosition.Y / resolutionY) * 2f;

            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            float fovYRad = camera.FovYDeg * (float)(Math.PI / 180.0);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, _view.AspectRatio, NearPlane, FarPlane);
            var viewProjection = view * projection;
            if (!Matrix4x4.Invert(viewProjection, out var inverseViewProjection))
            {
                return new ScreenRay(camera.Position, Vector3.Normalize(camera.Target - camera.Position));
            }

            Vector3 nearPoint = Unproject(new Vector3(ndcX, ndcY, 0f), inverseViewProjection);
            Vector3 farPoint = Unproject(new Vector3(ndcX, ndcY, 1f), inverseViewProjection);
            Vector3 direction = farPoint - nearPoint;
            if (direction.LengthSquared() <= 1e-6f)
            {
                direction = camera.Target - camera.Position;
            }

            return new ScreenRay(nearPoint, Vector3.Normalize(direction));
        }

        private bool TryResolveCamera(out CameraRenderState3D camera)
        {
            if (_presenter != null)
            {
                camera = _presenter.SmoothedRenderState;
                if (camera.FovYDeg > 0f)
                {
                    return true;
                }
            }

            var state = _cameraManager.State;
            if (state == null)
            {
                camera = default;
                return false;
            }

            camera = CameraViewportUtil.StateToRenderState(state);
            return true;
        }

        private static Vector3 Unproject(Vector3 ndc, Matrix4x4 inverseViewProjection)
        {
            Vector4 clip = new Vector4(ndc, 1f);
            Vector4 world = Vector4.Transform(clip, inverseViewProjection);
            if (Math.Abs(world.W) > 1e-6f)
            {
                world /= world.W;
            }

            return new Vector3(world.X, world.Y, world.Z);
        }
    }
}
