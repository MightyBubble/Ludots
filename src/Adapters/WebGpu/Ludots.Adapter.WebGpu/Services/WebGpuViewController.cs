using System.Numerics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.WebGpu.Services
{
    public sealed class WebGpuViewController : IViewController
    {
        private readonly WebGpuCameraAdapter _camera;
        private int _width;
        private int _height;

        public WebGpuViewController(WebGpuCameraAdapter camera, int width, int height)
        {
            _camera = camera;
            SetResolution(width, height);
        }

        public Vector2 Resolution => new Vector2(_width, _height);
        public float Fov => _camera.FovYDeg;
        public float AspectRatio => _height <= 0 ? 1f : (float)_width / _height;

        public void SetResolution(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    $"WebGpuViewController requires positive resolution, got {width}x{height}.");
            }

            _width = width;
            _height = height;
        }
    }
}
