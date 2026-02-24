using System;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Interface for camera control strategies.
    /// Mods can implement this to define how the camera moves and behaves.
    /// </summary>
    public interface ICameraController
    {
        /// <summary>
        /// Updates the camera state based on the controller's logic (input, following target, etc.).
        /// </summary>
        /// <param name="state">The mutable camera state.</param>
        /// <param name="dt">Delta time in seconds.</param>
        void Update(CameraState state, float dt);
    }
}
