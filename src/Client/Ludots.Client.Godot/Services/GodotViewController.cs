using System.Numerics;
using Godot;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Client.Godot.Services
{
    /// <summary>
    /// Godot implementation of IViewController. Uses Camera3D for Fov, Viewport for Resolution.
    /// </summary>
    public sealed class GodotViewController : IViewController
    {
        private readonly global::Godot.Camera3D _camera;

        public GodotViewController(global::Godot.Camera3D camera)
        {
            _camera = camera;
        }

        public System.Numerics.Vector2 Resolution
        {
            get
            {
                var vp = _camera.GetViewport();
                if (vp == null) return new System.Numerics.Vector2(1280, 720);
                var size = vp.GetVisibleRect().Size;
                return new System.Numerics.Vector2((float)size.X, (float)size.Y);
            }
        }

        public float Fov => (float)_camera.Fov;

        public float AspectRatio
        {
            get
            {
                var r = Resolution;
                return r.Y > 0 ? r.X / r.Y : 16f / 9f;
            }
        }
    }
}
