namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Cardinal neighbor direction for layered-span walk links (XZ grid).
    /// North is min-Z, South is max-Z, West is min-X, East is max-X.
    /// </summary>
    public enum LayeredSpanNeighborDirection : byte
    {
        West = 0,
        East = 1,
        North = 2,
        South = 3
    }
}
