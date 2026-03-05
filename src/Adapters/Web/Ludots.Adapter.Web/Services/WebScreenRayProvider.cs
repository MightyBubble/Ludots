using System;
using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebScreenRayProvider : IScreenRayProvider
    {
        private readonly WebCameraAdapter _camera;
        private readonly WebViewController _view;

        public WebScreenRayProvider(WebCameraAdapter camera, WebViewController view)
        {
            _camera = camera;
            _view = view;
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            var cam = _camera.CurrentState;
            float fovRad = cam.FovYDeg * MathF.PI / 180f;
            float halfH = MathF.Tan(fovRad * 0.5f);
            float halfW = halfH * _view.AspectRatio;

            var res = _view.Resolution;
            float ndcX = (2f * screenPosition.X / res.X) - 1f;
            float ndcY = 1f - (2f * screenPosition.Y / res.Y);

            Vector3 forward = Vector3.Normalize(cam.Target - cam.Position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, cam.Up));
            Vector3 up = Vector3.Cross(right, forward);

            Vector3 dir = Vector3.Normalize(
                forward + right * (ndcX * halfW) + up * (ndcY * halfH));

            return new ScreenRay(cam.Position, dir);
        }
    }
}
