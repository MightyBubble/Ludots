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
    public sealed class CoreScreenProjector : IScreenProjector, IProjectionRevisionProvider
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter _presenter;
        private int _projectionRevision = 1;
        private int _lastProjectionHash;

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

        public int ProjectionRevision
        {
            get
            {
                CameraRenderState3D camera = ResolveCamera();
                var resolution = _view.Resolution;
                int hash = HashCode.Combine(
                    camera.Position,
                    camera.Target,
                    camera.Up,
                    camera.FovYDeg,
                    resolution.X,
                    resolution.Y,
                    _view.AspectRatio);
                if (hash != _lastProjectionHash)
                {
                    _lastProjectionHash = hash;
                    _projectionRevision++;
                }

                return _projectionRevision;
            }
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            CameraRenderState3D camera = ResolveCamera();

            return CameraViewportUtil.WorldToScreen(
                worldPosition,
                camera,
                _view.Resolution,
                _view.AspectRatio);
        }

        private CameraRenderState3D ResolveCamera()
        {
            if (_presenter != null)
            {
                return _presenter.SmoothedRenderState;
            }

            var state = _cameraManager.State;
            return state == null
                ? new CameraRenderState3D(new Vector3(float.NaN), new Vector3(float.NaN), Vector3.UnitY, 60f)
                : CameraViewportUtil.StateToRenderState(state);
        }
    }
}
