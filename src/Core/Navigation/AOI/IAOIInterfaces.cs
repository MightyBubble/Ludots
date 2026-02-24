namespace Ludots.Core.Navigation.AOI
{
    public interface IAOISource
    {
        /// <summary>
        /// AOI 中心位置（厘米坐标，X=cm,Z=cm）
        /// </summary>
        int CenterXcm { get; }
        int CenterZcm { get; }

        /// <summary>
        /// AOI 半径（厘米）
        /// </summary>
        int RadiusCm { get; }
    }

    public interface IAOIListener
    {
        void OnChunkEnter(long chunkKey);
        void OnChunkExit(long chunkKey);
    }
}
