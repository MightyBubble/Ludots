using System.Numerics;

namespace Ludots.Core.Presentation.Camera
{
    /// <summary>
    /// Interface for platform-specific camera implementations.
    /// The Presentation layer calls this to update the actual engine camera.
    /// Adapters for Unity, Godot, etc. will implement this.
    /// </summary>
    public interface ICameraAdapter
    {
        /// <summary>
        /// Updates the camera transform.
        /// </summary>
        void UpdateCamera(in CameraRenderState3D state);
    }
}
