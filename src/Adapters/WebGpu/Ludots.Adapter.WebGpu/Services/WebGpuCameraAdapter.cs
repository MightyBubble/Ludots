using System.Numerics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.WebGpu.Services
{
    public sealed class WebGpuCameraAdapter : ICameraAdapter
    {
        public CameraRenderState3D CurrentState { get; private set; } =
            new CameraRenderState3D(
                new Vector3(10f, 10f, 10f),
                Vector3.Zero,
                Vector3.UnitY,
                60f);

        public float FovYDeg => CurrentState.FovYDeg;

        public void UpdateCamera(in CameraRenderState3D state)
        {
            CurrentState = state;
        }
    }
}
