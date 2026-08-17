namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// 渲染侧网格资产读取合同；Core 的 MeshAssetRegistry 实现并注入渲染器，
    /// 引擎画廊等无 Core 场景由宿主直接提供实现。
    /// </summary>
    public interface IRenderMeshAssets
    {
        bool TryGetDescriptor(int meshAssetId, out MeshAssetDescriptor descriptor);

        bool TryGetPrimitiveKind(int meshAssetId, out PrimitiveMeshKind kind);

        int GetId(string key);

        string GetName(int id);
    }

    /// <summary>
    /// 渲染侧材质资产读取合同；Core 的 PresentationMaterialRegistry 实现并注入渲染器。
    /// </summary>
    public interface IRenderMaterialAssets
    {
        bool TryGet(int id, out MaterialAssetDescriptor descriptor);

        int GetId(string key);

        string GetName(int id);
    }

    /// <summary>
    /// 渲染侧资产 URI 解析合同（替代渲染器对 Core IVirtualFileSystem 的直接依赖）。
    /// </summary>
    public interface IRenderAssetPathResolver
    {
        bool TryResolveFullPath(string uri, out string fullPath);
    }

    /// <summary>
    /// 渲染侧图元绘制快照（只读）；Core 的 PrimitiveDrawBuffer 实现并注入渲染器。
/// </summary>
public interface IPrimitiveDrawSnapshot
{
    int Count { get; }
    int Revision { get; }
    int ProjectionGeneration { get; }
    int StaticMeshGeometryRevision { get; }
    int StaticMeshDeltaBaseRevision { get; }
    int StaticMeshLaneItemCount { get; }
    int SkinnedLaneItemCount { get; }
    int StaticMeshDeltaItemCount { get; }
    int StaticMeshRemovedStableIdCount { get; }
    ReadOnlySpan<PrimitiveDrawItem> GetSpan();
    ReadOnlySpan<PrimitiveDrawItem> GetStaticMeshDeltaItems();
    ReadOnlySpan<int> GetStaticMeshRemovedStableIds();
}

    /// <summary>
    /// 渲染侧蒙皮批次快照（只读）；Core 的 SkinnedVisualBatchBuffer 实现并注入渲染器。
    /// </summary>
    public interface ISkinnedVisualBatchSnapshot
    {
        int Count { get; }
        ReadOnlySpan<SkinnedVisualBatchItem> GetSpan();
    }
}
