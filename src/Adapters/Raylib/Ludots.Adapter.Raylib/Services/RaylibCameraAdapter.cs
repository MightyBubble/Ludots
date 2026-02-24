using System.Numerics;
using Ludots.Core.Presentation.Camera;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Services
{
    public sealed class RaylibCameraAdapter : ICameraAdapter
    {
        public Camera3D Camera { get; private set; }

        public RaylibCameraAdapter(Camera3D initialCamera)
        {
            Camera = initialCamera;
        }

        public void UpdateCamera(in CameraRenderState3D state)
        {
            Camera3D cam = Camera;
            cam.position = state.PositionCm * 0.01f;
            cam.target = state.TargetCm * 0.01f;
            cam.up = state.Up;
            cam.fovy = state.FovYDeg;

            Camera = cam;
        }

        public void DrawGizmos(Vector3 targetVisual)
        {
            Rl.DrawLine3D(targetVisual, targetVisual + new Vector3(2.0f, 0, 0), Color.RED);
            Rl.DrawLine3D(targetVisual, targetVisual + new Vector3(0, 0, 2.0f), Color.BLUE);
            Rl.DrawLine3D(targetVisual, targetVisual + new Vector3(0, 2.0f, 0), Color.GREEN);

            Vector3 camForward = Vector3.Normalize(Camera.target - Camera.position);
            Rl.DrawLine3D(targetVisual, targetVisual + camForward * 3.0f, Color.YELLOW);
            Rl.DrawSphere(targetVisual, 0.2f, Color.WHITE);
        }
    }
}
