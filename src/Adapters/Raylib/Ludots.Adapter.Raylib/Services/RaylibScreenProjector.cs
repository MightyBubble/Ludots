using System.Numerics;
using Ludots.Platform.Abstractions;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibScreenProjector : IScreenProjector
    {
        private readonly RaylibCameraAdapter _camera;

        public RaylibScreenProjector(RaylibCameraAdapter camera)
        {
            _camera = camera;
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            return Rl.GetWorldToScreen(worldPosition, _camera.Camera);
        }
    }
}
