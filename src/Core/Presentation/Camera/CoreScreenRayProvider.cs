using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;
using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Core implementation of <see cref="IScreenRayProvider"/>. Mirrors
    /// <see cref="CoreScreenProjector"/> so gameplay input can use the same
    /// smoothed render camera math as presentation when available.
    /// </summary>
    public sealed class CoreScreenRayProvider : IScreenRayProvider
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter? _presenter;

        public CoreScreenRayProvider(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            CameraRenderState3D camera;
            if (_presenter != null)
            {
                camera = _presenter.SmoothedRenderState;
            }
            else
            {
                var state = _cameraManager.State;
                if (state == null)
                {
                    return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
                }

                camera = CameraViewportUtil.StateToRenderState(state);
            }

            return CameraViewportUtil.ScreenToRay(
                screenPosition,
                camera,
                _view.Resolution,
                _view.AspectRatio);
        }
    }
}
