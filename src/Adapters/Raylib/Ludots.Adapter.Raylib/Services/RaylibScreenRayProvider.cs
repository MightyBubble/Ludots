using System.Numerics;
using Ludots.Platform.Abstractions;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibScreenRayProvider : IScreenRayProvider
    {
        private readonly RaylibCameraAdapter _camera;

        public RaylibScreenRayProvider(RaylibCameraAdapter camera)
        {
            _camera = camera;
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            int w = Rl.GetScreenWidth();
            int h = Rl.GetScreenHeight();
            if (w <= 0 || h <= 0)
            {
                return new ScreenRay(Vector3.Zero, Vector3.UnitZ);
            }

            float ndcX = (screenPosition.X / w) * 2f - 1f;
            float ndcY = 1f - (screenPosition.Y / h) * 2f;

            var cam = _camera.Camera;
            var view = Matrix4x4.CreateLookAt(cam.position, cam.target, cam.up);
            float aspect = w / (float)h;
            float fovRad = cam.fovy * (MathF.PI / 180f);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspect, 0.01f, 1000f);

            var viewProj = view * projection;
            if (!Matrix4x4.Invert(viewProj, out var invViewProj))
            {
                return new ScreenRay(cam.position, Vector3.Normalize(cam.target - cam.position));
            }

            var nearClip = new Vector4(ndcX, ndcY, 0f, 1f);
            var farClip = new Vector4(ndcX, ndcY, 1f, 1f);

            var nearWorld4 = Vector4.Transform(nearClip, invViewProj);
            var farWorld4 = Vector4.Transform(farClip, invViewProj);

            if (MathF.Abs(nearWorld4.W) < 1e-6f || MathF.Abs(farWorld4.W) < 1e-6f)
            {
                return new ScreenRay(cam.position, Vector3.Normalize(cam.target - cam.position));
            }

            nearWorld4 /= nearWorld4.W;
            farWorld4 /= farWorld4.W;

            var nearWorld = new Vector3(nearWorld4.X, nearWorld4.Y, nearWorld4.Z);
            var farWorld = new Vector3(farWorld4.X, farWorld4.Y, farWorld4.Z);

            var dir = Vector3.Normalize(farWorld - nearWorld);
            return new ScreenRay(nearWorld, dir);
        }
    }
}
