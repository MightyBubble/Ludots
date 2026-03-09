using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Spatial;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Handles the visual representation of the camera.
    /// Calculates the 3D position and rotation based on the logical CameraState.
    /// </summary>
    public class CameraPresenter
    {
        private readonly ICameraAdapter _adapter;
        private readonly ISpatialCoordinateConverter _coords;

        private Vector3 _currentPosition;
        private Vector3 _currentTarget;
        private Vector3 _currentUp;
        private float _currentFovYDeg;
        private bool _isFirstUpdate = true;

        /// <summary>
        /// Smoothing speed for camera movement.
        /// </summary>
        public float SmoothSpeed { get; set; } = 10.0f;

        /// <summary>
        /// The current logical target position of the camera in visual world space.
        /// Exposed for AOI systems.
        /// </summary>
        public Vector3 CurrentTargetPosition { get; private set; }

        /// <summary>
        /// The smoothed render-state that matches the actual 3D camera used for rendering.
        /// HUD projection should use this to stay in sync with 3D meshes.
        /// </summary>
        public CameraRenderState3D SmoothedRenderState { get; private set; }

        public bool HasSmoothedRenderState { get; private set; }

        public CameraPresenter(ISpatialCoordinateConverter coords, ICameraAdapter adapter)
        {
            _coords = coords;
            _adapter = adapter;
        }

        /// <summary>
        /// Updates the visual camera transform.
        /// Should be called in the engine's LateUpdate or Render loop.
        /// </summary>
        /// <param name="state">The player's camera state.</param>
        /// <param name="deltaTime">Time elapsed since last frame.</param>
        /// <param name="cameraDebug">Optional debug overrides (pullback, offset). Null = no override.</param>
        public void Update(CameraState state, float deltaTime, RenderCameraDebugState cameraDebug = null)
        {
            if (state == null) return;

            Vector3 targetPos = new Vector3(WorldUnits.CmToM(state.TargetCm.X), 0f, WorldUnits.CmToM(state.TargetCm.Y));
            CurrentTargetPosition = targetPos;

            float yawRad = ToRadians(state.Yaw);
            float pitchRad = ToRadians(state.Pitch);

            float distanceM = WorldUnits.CmToM(state.DistanceCm);

            if (cameraDebug is { Enabled: true })
                distanceM += cameraDebug.PullBackMeters;

            float hDist = distanceM * (float)System.Math.Cos(pitchRad);
            float vDist = distanceM * (float)System.Math.Sin(pitchRad);

            float offsetX = hDist * (float)System.Math.Sin(yawRad);
            float offsetZ = -hDist * (float)System.Math.Cos(yawRad);

            Vector3 offset = new Vector3(offsetX, vDist, offsetZ);
            Vector3 desiredPos = targetPos + offset;

            if (cameraDebug is { Enabled: true })
                desiredPos += cameraDebug.PositionOffsetMeters;

            Vector3 forward = Vector3.Normalize(targetPos - desiredPos);
            Vector3 up = Vector3.UnitY;
            if (System.Math.Abs(Vector3.Dot(forward, up)) > 0.99f)
            {
                up = Vector3.UnitZ;
            }

            if (_isFirstUpdate)
            {
                _currentPosition = desiredPos;
                _currentTarget = targetPos;
                _currentUp = up;
                _isFirstUpdate = false;
            }
            else if (deltaTime > 0)
            {
                float t = Math.Clamp(SmoothSpeed * deltaTime, 0, 1);
                _currentPosition = Vector3.Lerp(_currentPosition, desiredPos, t);
                _currentTarget = Vector3.Lerp(_currentTarget, targetPos, t);
                _currentUp = Vector3.Normalize(Vector3.Lerp(_currentUp, up, t));
            }

            _currentFovYDeg = state.FovYDeg;
            SmoothedRenderState = new CameraRenderState3D(_currentPosition, _currentTarget, _currentUp, _currentFovYDeg);
            HasSmoothedRenderState = true;
            _adapter.UpdateCamera(SmoothedRenderState);
        }

        private float ToRadians(float degrees) => degrees * (float)System.Math.PI / 180.0f;
    }
}
