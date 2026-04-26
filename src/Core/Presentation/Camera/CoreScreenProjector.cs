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
    public sealed class CoreScreenProjector : IScreenProjector, IProjectionSnapshotProvider
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;
        private CameraPresenter _presenter;
        private int _projectionRevision = 1;
        private int _lastProjectionHash;
        private Matrix4x4 _viewProjection;
        private Vector2 _cachedResolution;

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
                EnsureProjectionCache();
                return _projectionRevision;
            }
        }

        public bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot)
        {
            EnsureProjectionCache();
            if (float.IsNaN(_cachedResolution.X) || float.IsNaN(_cachedResolution.Y) ||
                _cachedResolution.X <= 0f || _cachedResolution.Y <= 0f)
            {
                snapshot = default;
                return false;
            }

            snapshot = new ProjectionSnapshot(_viewProjection, _cachedResolution);
            return true;
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            EnsureProjectionCache();

            var clip = Vector4.Transform(new Vector4(worldPosition, 1f), _viewProjection);
            if (clip.W <= 0.001f)
            {
                return new Vector2(float.NaN, float.NaN);
            }

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f)
            {
                return new Vector2(float.NaN, float.NaN);
            }

            float screenX = (ndcX + 1f) * 0.5f * _cachedResolution.X;
            float screenY = (1f - ndcY) * 0.5f * _cachedResolution.Y;
            return new Vector2(screenX, screenY);
        }

        private void EnsureProjectionCache()
        {
            CameraRenderState3D camera = ResolveCamera();
            Vector2 resolution = _view.Resolution;
            int hash = HashCode.Combine(
                camera.Position,
                camera.Target,
                camera.Up,
                camera.FovYDeg,
                resolution.X,
                resolution.Y,
                _view.AspectRatio);
            if (hash == _lastProjectionHash)
            {
                return;
            }

            _lastProjectionHash = hash;
            _projectionRevision++;
            _cachedResolution = resolution;

            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            float fovYRad = camera.FovYDeg * (float)(Math.PI / 180.0);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, _view.AspectRatio, 0.1f, 10000f);
            _viewProjection = view * projection;
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
