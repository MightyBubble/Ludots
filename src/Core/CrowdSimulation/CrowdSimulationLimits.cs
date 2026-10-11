namespace Ludots.Core.CrowdSimulation;

/// <summary>
/// 层号和净空各占一个字节，所以导航上下文键的层步长是 2^8。
/// 同量级的目录容量、查询堆和子步上限沿用 2^8 / 2^6，调用点只写名字。
/// </summary>
public static class CrowdSimulationLimits
{
    public const int NavContextLayerStride = 1 << 8;
    public const int MaxCatalogIds = 1 << 8;
    public const int MaxRelationKinds = 1 << 8;
    public const int MaxFogSlotCacheCapacity = 1 << 8;
    public const int MaxTerrainTypeIds = 1 << 8;
    public const int NavMeshQueryHeapCapacity = 1 << 8;
    public const int RoutePolylineStackLimit = 1 << 8;

    public const int MaxSubSteps = 1 << 6;
    public const int MaxParallelWorkers = 1 << 6;
    public const int OpenCellNavIdScratch = 1 << 6;
    public const int FogPolygonOrdinateScratch = 1 << 6;
    public const int AdvanceStallGuardFactor = 1 << 6;

    public const int WireframeRouteVisualId = 0x64;
}
