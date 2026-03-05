using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Core implementation of IScreenProjector. Platform-agnostic projection using CameraState + IViewController.
    /// </summary>
    public sealed class CoreScreenProjector : IScreenProjector
    {
        private readonly CameraManager _cameraManager;
        private readonly IViewController _view;

        public CoreScreenProjector(CameraManager cameraManager, IViewController view)
        {
            _cameraManager = cameraManager ?? throw new System.ArgumentNullException(nameof(cameraManager));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            var state = _cameraManager.State;
            if (state == null)
                return new Vector2(float.NaN, float.NaN);

            var camera = CameraViewportUtil.StateToRenderState(state);
            return CameraViewportUtil.WorldToScreen(
                worldPosition,
                camera,
                _view.Resolution,
                _view.AspectRatio);
        }
    }
}
