using System.Numerics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebViewController : IViewController
    {
        private int _width = 1280;
        private int _height = 720;

        public Vector2 Resolution => new Vector2(_width, _height);
        public float Fov => 60f;
        public float AspectRatio => _height > 0 ? (float)_width / _height : 1f;

        public void SetResolution(int width, int height)
        {
            if (width > 0) _width = width;
            if (height > 0) _height = height;
        }
    }
}
