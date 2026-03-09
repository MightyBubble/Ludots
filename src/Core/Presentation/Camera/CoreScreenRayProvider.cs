using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Pure-core screen ray provider that derives rays from the logical camera state.
    /// Useful before any renderer-specific presenter has published a render camera.
    /// </summary>
    public sealed class CoreScreenRayProvider : IScreenRayProvider
    {
        private readonly CameraManager _camera;
        private readonly IViewController _view;

        public CoreScreenRayProvider(CameraManager camera, IViewController view)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            var renderState = CameraViewportUtil.StateToRenderState(_camera.State);
            return CameraViewportUtil.ScreenToRay(screenPosition, renderState, _view.Resolution, _view.AspectRatio);
        }
    }
}
