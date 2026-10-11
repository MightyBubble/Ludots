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
}
