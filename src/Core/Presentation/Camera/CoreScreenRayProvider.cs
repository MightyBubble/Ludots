using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;
using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Core implementation of <see cref="IScreenRayProvider"/>. Mirrors
    /// <see cref="CoreScreenProjector"/> so gameplay input can use the same
    /// smoothed render camera math as presentation when available.
    /// </summary>
    public sealed class CoreScreenRayProvider : IScreenRayProvider, IPresentationCameraSnapshotScope
    {
        private CameraManager _cameraManager;
        private IViewController _view;
        private CameraPresenter? _presenter;
        private Func<float>? _presentationAlphaProvider;
        private bool _presentationFrameActive;

        public CoreScreenRayProvider(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        public void Rebind(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void BindPresenter(CameraPresenter presenter) => _presenter = presenter;

        public void BindPresentationAlphaProvider(Func<float> presentationAlphaProvider)
        {
            _presentationAlphaProvider = presentationAlphaProvider ?? throw new System.ArgumentNullException(nameof(presentationAlphaProvider));
        }

        void IPresentationCameraSnapshotScope.BeginPresentationFrame()
        {
            _presentationFrameActive = true;
        }

        void IPresentationCameraSnapshotScope.EndPresentationFrame()
        {
            _presentationFrameActive = false;
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            CameraRenderState3D camera = ResolveCamera();

            return CameraViewportUtil.ScreenToRay(
                screenPosition,
                camera,
                _view.Resolution,
                _view.AspectRatio);
        }

        private CameraRenderState3D ResolveCamera()
        {
            if (_presentationFrameActive && _presentationAlphaProvider != null)
            {
                CameraStateSnapshot state = _cameraManager.GetInterpolatedState(_presentationAlphaProvider());
                return CameraViewportUtil.StateToRenderState(in state);
            }

            if (_presenter != null)
            {
                return _presenter.SmoothedRenderState;
            }

            var rawState = _cameraManager.State;
            return rawState == null
                ? new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f)
                : CameraViewportUtil.StateToRenderState(rawState);
        }
    }
}
