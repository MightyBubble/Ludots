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

        /// <summary>
        /// 群体模拟的 .navsurface（地形类型栅格）。不是高度文件，也不是 Board.DataFile。
        /// 临时用“某块板声明了它”识别群体地图，随后会改成地图上的显式求解器字段。
        /// </summary>
        public string SurfaceAsset { get; set; }
    }
}
