using System.Numerics;
using Ludots.Core.Presentation.Camera;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibViewController : IViewController
    {
        private readonly RaylibCameraAdapter _camera;
        private readonly int? _fixedWidth;
        private readonly int? _fixedHeight;

        public RaylibViewController(RaylibCameraAdapter camera, int? fixedWidth = null, int? fixedHeight = null)
        {
            _camera = camera;
            _fixedWidth = fixedWidth;
            _fixedHeight = fixedHeight;
        }

        public Vector2 Resolution
        {
            get
            {
                if (_fixedWidth.HasValue && _fixedHeight.HasValue)
                {
                    return new Vector2(_fixedWidth.Value, _fixedHeight.Value);
                }

                return new Vector2(Rl.GetScreenWidth(), Rl.GetScreenHeight());
            }
        }

        public float Fov => _camera.Camera.fovy;
        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }
}
