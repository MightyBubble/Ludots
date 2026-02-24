namespace Ludots.Platform.Abstractions
{
    public interface IRenderBackend
    {
        void BeginFrame();
        void EndFrame();
    }
}
