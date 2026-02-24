using System.Numerics;
using Ludots.Core.Presentation.Camera;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibViewController : IViewController
    {
        private readonly RaylibCameraAdapter _camera;

        public RaylibViewController(RaylibCameraAdapter camera)
        {
            _camera = camera;
        }

        public Vector2 Resolution => new Vector2(Rl.GetScreenWidth(), Rl.GetScreenHeight());
        public float Fov => _camera.Camera.fovy;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }
}
