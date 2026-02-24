using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Pure data state for the player's camera.
    /// This should be serializable to support cross-platform persistence.
    /// </summary>
    public class CameraState
    {
        /// <summary>
        /// The world position (centimeters) the camera is focusing on.
        /// Using Vector2 for smooth movement.
        /// </summary>
        public Vector2 TargetCm { get; set; }

        /// <summary>
        /// Horizontal rotation in degrees (or fixed point).
        /// </summary>
        public float Yaw { get; set; } = 45.0f;

        /// <summary>
        /// Vertical rotation in degrees.
        /// </summary>
        public float Pitch { get; set; } = 45.0f;

        /// <summary>
        /// Distance from the target (centimeters).
        /// </summary>
        public float DistanceCm { get; set; } = 2000.0f;

        /// <summary>
        /// Current zoom level index (if discrete zooming is used).
        /// </summary>
        public int ZoomLevel { get; set; } = 5;

        public float FovYDeg { get; set; } = 60.0f;
    }
}
