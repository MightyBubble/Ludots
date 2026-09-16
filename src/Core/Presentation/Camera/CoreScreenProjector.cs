using System;
using System.Numerics;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Core implementation of IScreenProjector. Platform-agnostic projection.
    /// Uses the smoothed render state from <see cref="CameraPresenter"/> when available,
    /// ensuring HUD projection matches the actual 3D camera (no smoothing desync).
    /// Falls back to computing from logical <see cref="CameraState"/> if no presenter is set.
    /// 实现 IProjectionRevisionProvider：相机姿态哈希未变时 revision 稳定，
    /// WorldHudToScreenSystem 的三档轻路径（0 成本早退/内容增量/位置增量）得以激活——
    /// 静止相机帧不再全量重建 2 万条 HUD 条目。
    /// </summary>
    public sealed class CoreScreenProjector : IScreenProjector, IProjectionSnapshotProvider, IProjectionRevisionProvider, IPresentationCameraSnapshotScope
    {
        private CameraManager _cameraManager;
        private IViewController _view;
        private CameraPresenter _presenter;
        private Func<float>? _presentationAlphaProvider;
        private bool _presentationFrameActive;
        private int _projectionRevision = 1;
        private long _lastProjectionHash;
        private Matrix4x4 _viewProjection;
        private Vector2 _cachedResolution;

        public CoreScreenProjector(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        public void Rebind(CameraManager cameraManager, IViewController view)
        {
            // 每帧例行的 present 管线同步也会走这里，且 PresentBindingSurface 每帧新建实例——
            // 引用比较恒不等。只有相机实例或表面语义（binding/fov）真正变化时才作废投影缓存，
            // 否则 revision 每帧 +1，WorldHudToScreenSystem 的轻路径永不驻留（2 万条 HUD
            // 条目每帧全量重建 4.6ms 的隐性根因）。
            bool cameraChanged = !ReferenceEquals(_cameraManager, cameraManager);
            bool viewChanged = !ViewSemanticallyEqual(view);
            if (!cameraChanged && !viewChanged)
            {
                return;
            }

            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _lastProjectionHash = 0;
        }

        private bool ViewSemanticallyEqual(IViewController view)
        {
            if (ReferenceEquals(_view, view))
            {
                return true;
            }

            if (_view is PresentBindingSurface current &&
                view is PresentBindingSurface next &&
                ReferenceEquals(current.Binding, next.Binding) &&
                current.Fov == next.Fov)
            {
                return true;
            }

            return _view != null &&
                view != null &&
                _view.Resolution == view.Resolution &&
                _view.Fov == view.Fov;
        }

        /// <summary>
        /// Bind a <see cref="CameraPresenter"/> so projection uses the smoothed camera
        /// that matches the 3D render camera exactly.
        /// </summary>
        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public void BindPresentationAlphaProvider(Func<float> presentationAlphaProvider)
        {
            _presentationAlphaProvider = presentationAlphaProvider ?? throw new ArgumentNullException(nameof(presentationAlphaProvider));
        }

        void IPresentationCameraSnapshotScope.BeginPresentationFrame()
        {
            _presentationFrameActive = true;
        }

        void IPresentationCameraSnapshotScope.EndPresentationFrame()
        {
            _presentationFrameActive = false;
        }

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

            snapshot = new ProjectionSnapshot(_viewProjection, _cachedResolution, ResolveCamera().Position);
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
            // 姿态量化到厘米/0.01°：逻辑相机静止时，插值 alpha 与高度自适应的亚厘米浮点
            // 漂移不再 bump revision——WorldHudToScreenSystem 的轻路径得以驻留（此前每帧
            // 全量重建 2 万条 HUD 条目）。量化误差 ≤1cm，屏幕上不可见。
            long hash = ((long)MathF.Round(camera.Position.X * 100f) * 397L)
                ^ (((long)MathF.Round(camera.Position.Y * 100f) * 397L) << 1)
                ^ (((long)MathF.Round(camera.Position.Z * 100f) * 397L) << 2)
                ^ (((long)MathF.Round(camera.Target.X * 100f) * 397L) << 3)
                ^ (((long)MathF.Round(camera.Target.Y * 100f) * 397L) << 4)
                ^ (((long)MathF.Round(camera.Target.Z * 100f) * 397L) << 5)
                ^ (((long)MathF.Round(camera.Up.X * 10000f) * 397L) << 6)
                ^ (((long)MathF.Round(camera.Up.Y * 10000f) * 397L) << 7)
                ^ (((long)MathF.Round(camera.Up.Z * 10000f) * 397L) << 8)
                ^ (((long)MathF.Round(camera.FovYDeg * 100f) * 397L) << 9)
                ^ (((long)MathF.Round(resolution.X * 0.1f) * 397L) << 10)
                ^ (((long)MathF.Round(resolution.Y * 0.1f) * 397L) << 11)
                ^ (((long)MathF.Round(_view.AspectRatio * 10000f) * 397L) << 12);
            if (hash == _lastProjectionHash)
            {
                return;
            }

            if (Environment.GetEnvironmentVariable("LUDOTS_HUD_GATE_TRACE") is "1" or "true")
            {
                Ludots.Core.Diagnostics.Log.Info(
                    in Ludots.Core.Diagnostics.LogChannels.Presentation,
                    $"[projhash] rev {_projectionRevision + 1} hash={hash} prev={_lastProjectionHash} pos=({camera.Position.X:F4},{camera.Position.Y:F4},{camera.Position.Z:F4}) tgt=({camera.Target.X:F4},{camera.Target.Y:F4},{camera.Target.Z:F4}) up=({camera.Up.X:F6},{camera.Up.Y:F6},{camera.Up.Z:F6}) fov={camera.FovYDeg:F4} res={resolution.X:F2}x{resolution.Y:F2} aspect={_view.AspectRatio:F6}");
            }

            _lastProjectionHash = hash;
            _projectionRevision++;
            _cachedResolution = resolution;

            var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
            float fovYRad = WorldPlane2D.DegToRadValue(camera.FovYDeg);
            CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in camera);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovYRad, _view.AspectRatio, clipPlanes.NearMeters, clipPlanes.FarMeters);
            _viewProjection = view * projection;
        }

        private CameraRenderState3D ResolveCamera()
        {
            if (_presentationFrameActive && _presentationAlphaProvider != null)
            {
                CameraStateSnapshot interpolatedState = _cameraManager.GetInterpolatedState(_presentationAlphaProvider());
                return CameraViewportUtil.StateToRenderState(in interpolatedState);
            }

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
