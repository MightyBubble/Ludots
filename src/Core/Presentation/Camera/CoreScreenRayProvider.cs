using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    public sealed class CoreScreenRayProvider : IScreenRayProvider
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter _presenter;

        public CoreScreenRayProvider(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            if (!TryResolveCamera(out var camera))
            {
                return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
            }

            return CameraViewportUtil.ScreenToRay(
                screenPosition,
                camera,
                _view.Resolution,
                _view.AspectRatio);
        }

        private bool TryResolveCamera(out CameraRenderState3D camera)
        {
            if (_presenter is { HasSmoothedRenderState: true })
            {
                camera = _presenter.SmoothedRenderState;
                return true;
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
    }
}
