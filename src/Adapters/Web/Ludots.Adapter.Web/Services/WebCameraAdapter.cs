using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebCameraAdapter : ICameraAdapter
    {
        public CameraRenderState3D CurrentState { get; private set; }

        public void UpdateCamera(in CameraRenderState3D state)
        {
            CurrentState = state;
        }
    }
}
