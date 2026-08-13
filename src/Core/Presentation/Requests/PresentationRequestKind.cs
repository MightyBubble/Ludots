namespace Ludots.Core.Presentation.Requests
{
    public enum PresentationRequestKind : byte
    {
        VisualProxy = 1,
        Prefab = 2,
        GroundOverlay = 3,
        WorldHud = 4,
        SplineRibbon = 5,
        SurfaceSource = 6,
        RemoveGroundOverlay = 7,
        RemoveWorldHud = 8,
        RemoveSplineRibbon = 9,
        RemoveSurfaceSource = 10,
        ClearTransientVisualProjection = 11,
    }
}
