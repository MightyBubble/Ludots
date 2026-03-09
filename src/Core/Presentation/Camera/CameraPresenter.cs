using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Spatial;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Converts the fixed-step logic camera into the render-frame camera.
    /// Presentation smoothing comes from interpolation between PreviousState and State.
    /// </summary>
    public class CameraPresenter
    {
        private readonly ICameraAdapter _adapter;

        /// <summary>
        /// The current interpolated target position of the camera in visual world space.
        /// Exposed for AOI systems.
        /// </summary>
        public Vector3 CurrentTargetPosition { get; private set; }

        /// <summary>
        /// The render-state that matches the actual 3D camera used for rendering.
        /// HUD projection should use this to stay in sync with 3D meshes.
        /// </summary>
        public CameraRenderState3D SmoothedRenderState { get; private set; }

        public CameraPresenter(ISpatialCoordinateConverter coords, ICameraAdapter adapter)
        {
            _ = coords ?? throw new ArgumentNullException(nameof(coords));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        /// <summary>
        /// Updates the visual camera from the interpolated logic camera state.
        /// </summary>
        public void Update(CameraManager cameraManager, float interpolationAlpha, RenderCameraDebugState cameraDebug = null)
        {
            if (cameraManager == null)
            {
                return;
            }

            CameraStateSnapshot state = cameraManager.GetInterpolatedState(interpolationAlpha);
            CurrentTargetPosition = new Vector3(WorldUnits.CmToM(state.TargetCm.X), 0f, WorldUnits.CmToM(state.TargetCm.Y));
            SmoothedRenderState = CameraViewportUtil.StateToRenderState(state, cameraDebug);
            _adapter.UpdateCamera(SmoothedRenderState);
        }
    }
}
