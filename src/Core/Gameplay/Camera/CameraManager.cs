using System;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Manages the active camera state and controller.
    /// Acts as the central service for camera logic within the GameSession.
    /// </summary>
    public class CameraManager
    {
        /// <summary>
        /// The current state of the camera (position, rotation, zoom).
        /// </summary>
        public CameraState State { get; private set; } = new CameraState();

        /// <summary>
        /// The active controller that drives the camera state.
        /// </summary>
        public ICameraController Controller { get; private set; }

        /// <summary>
        /// Sets the active camera controller.
        /// </summary>
        public void SetController(ICameraController controller)
        {
            Controller = controller;
        }

        /// <summary>
        /// Updates the camera state using the active controller.
        /// Should be called once per frame by the GameSession.
        /// </summary>
        public void Update(float dt)
        {
            if (Controller != null)
            {
                Controller.Update(State, dt);
            }
        }
    }
}
