using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
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
        public void Update(CameraState state, float deltaTime)
        {
            if (state == null) return;

            // 1. Calculate Target Position (Visual World Space)
            Vector3 targetPos = new Vector3(WorldUnits.CmToM(state.TargetCm.X), 0f, WorldUnits.CmToM(state.TargetCm.Y));
            CurrentTargetPosition = targetPos;

            // 2. Calculate Camera Offset (Spherical Coordinates)
            // We assume Y-Up convention for the Visual Space (Standard for Unity/Godot)
            float yawRad = ToRadians(state.Yaw);
            float pitchRad = ToRadians(state.Pitch);

            // Calculate horizontal and vertical distances
            // Pitch: 0 = Horizontal, 90 = Top-down
            float distanceM = WorldUnits.CmToM(state.DistanceCm);
            float hDist = distanceM * (float)System.Math.Cos(pitchRad);
            float vDist = distanceM * (float)System.Math.Sin(pitchRad);

            // Calculate offset vector
            // Yaw: 0 = Looking along +Z (North)
            // Camera position is "behind" the target direction
            float offsetX = hDist * (float)System.Math.Sin(yawRad);
            float offsetZ = -hDist * (float)System.Math.Cos(yawRad);

            Vector3 offset = new Vector3(offsetX, vDist, offsetZ);
            Vector3 desiredPos = targetPos + offset;

            Vector3 forward = Vector3.Normalize(targetPos - desiredPos);
            Vector3 up = Vector3.UnitY;
            if (System.Math.Abs(Vector3.Dot(forward, up)) > 0.99f)
            {
                up = Vector3.UnitZ;
            }

            // 4. Apply Smoothing
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

            // 5. Send to Adapter
            _adapter.UpdateCamera(new CameraRenderState3D(_currentPosition, _currentTarget, _currentUp, state.FovYDeg));
        }

        private float ToRadians(float degrees) => degrees * (float)System.Math.PI / 180.0f;
    }
}
