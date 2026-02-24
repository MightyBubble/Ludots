using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Interface for platform-specific view properties.
    /// Used by core systems (like Culling) to understand the screen/window dimensions.
    /// </summary>
    public interface IViewController
    {
        /// <summary>
        /// Screen or Window resolution in pixels.
        /// </summary>
        Vector2 Resolution { get; }

        /// <summary>
        /// Field of View in degrees (vertical).
        /// </summary>
        float Fov { get; }

        /// <summary>
        /// Aspect Ratio (Width / Height).
        /// </summary>
        float AspectRatio { get; }
    }
}
