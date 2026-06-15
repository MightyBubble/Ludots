namespace Ludots.Core.Presentation.Camera
{
    internal interface IPresentationCameraSnapshotScope
    {
        void BeginPresentationFrame();
        void EndPresentationFrame();
    }
}
