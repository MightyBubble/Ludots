using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Core implementation of IScreenProjector. Platform-agnostic projection.
    /// Uses the smoothed render state from <see cref="CameraPresenter"/> when available,
    /// ensuring HUD projection matches the actual 3D camera (no smoothing desync).
    /// Falls back to computing from logical <see cref="CameraState"/> if no presenter is set.
    /// </summary>
    public sealed class CoreScreenProjector : IScreenProjector
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter _presenter;

        public CoreScreenProjector(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        /// <summary>
        /// Bind a <see cref="CameraPresenter"/> so projection uses the smoothed camera
        /// that matches the 3D render camera exactly.
        /// </summary>
        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            if (!TryResolveCamera(out var camera))
            {
                return new Vector2(float.NaN, float.NaN);
            }

            return CameraViewportUtil.WorldToScreen(
                worldPosition,
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
