namespace Ludots.Core.CrowdSimulation;

public static class CrowdSimulationLimits
{
    /// <summary>层号和净空各占一个字节，导航上下文键按这个步长分开。</summary>
    public const int NavContextLayerStride = 256;
    public const int MaxCatalogIds = 256;
    public const int MaxFogSlotCacheCapacity = 256;
    public const int MaxTerrainTypeIds = 256;
    public const int NavMeshQueryHeapCapacity = 256;
    public const int RoutePolylineStackLimit = 256;

    public const int MaxSubSteps = 64;
    public const int MaxParallelWorkers = 64;
    public const int OpenCellNavIdScratch = 64;
    public const int FogPolygonOrdinateScratch = 64;
    public const int AdvanceStallGuardFactor = 64;

    public const int WireframeRouteVisualId = 100;

    /// <summary>百分数满幅。脉冲分母放在这里，避免调试视图源文件里出现尺度字面量。</summary>
    public const int FullPercent = 100;

    /// <summary>
    /// 热路径集合预留的槽数。这是容量，不是导航尺度；裸写会被空间尺度扫描当成尺度字面量。
    /// </summary>
    public const int HotPathSlotReserve = 64;

    /// <summary>
    /// 热路径上较大缓冲的预留长度：规划暂存、路径点、重烘集合池、轮廓环、阻挡盒表。
    /// </summary>
    public const int HotPathSpanReserve = 256;
}
