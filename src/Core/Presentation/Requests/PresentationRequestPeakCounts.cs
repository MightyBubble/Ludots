namespace Ludots.Core.Presentation.Requests
{
    internal readonly record struct PresentationRequestPeakCounts(
        int VisualProxy,
        int GroundOverlay,
        int WorldHud,
        int SplineRibbon,
        int SurfaceSource,
        int Removal,
        int ClearTransient,
        int Total);
}
