namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Contour ring polarity relative to its chart: outer (CCW, positive signed area2) or hole (CW).
    /// </summary>
    public enum LayeredSpanContourRingKind : byte
    {
        Outer = 0,
        Hole = 1
    }
}
