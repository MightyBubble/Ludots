using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Nav tile granularity declared by the nav side (#1346 decoupling): tile size in
    /// world centimeters, independent of board cells and terrain chunks. Tile counts
    /// derive from the board extent divided by these sizes.
    /// </summary>
    public sealed class NavTileGridConfig
    {
        public int TileWorldWidthCm { get; set; }

        public int TileWorldHeightCm { get; set; }
    }
}
