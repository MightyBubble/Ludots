using System;
using DotRecast.Detour;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// 查询侧 Detour 网格缓存：按 (store.LoadedVersion, layer, tile 尺寸) 复用装配好的 <see cref="DtNavMesh"/>，
    /// 避免每次寻路全量重建（每 tile DtMeshData + BVH 构建是 O(已加载 tile 数) 的大开销）。
    /// 归属查询注册表而非单个 NavQueryService——AgentBridge 每请求新建 service 实例，缓存必须跨实例共享。
    /// 并发模型：单飞构建（构建期间持锁，其余线程等待同一结果，杜绝重复构建）；
    /// <see cref="DtNavMesh"/> 构建后只读共享，<see cref="DtNavMeshQuery"/> 每次查询独立创建。
    /// </summary>
    public sealed class DetourQueryMeshCache
    {
        private readonly object _gate = new object();
        private DtNavMesh? _mesh;
        private uint _loadedVersion;
        private int _layer;
        private int _tileWidthCm;
        private int _tileHeightCm;

        /// <summary>实际执行网格装配的次数（含并发竞争下的重复），供契约测试断言缓存命中。</summary>
        public long BuildCount { get; private set; }

        public NavPathResult FindPath(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            int tileWidthCm,
            int tileHeightCm,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            DtNavMesh mesh = GetOrBuild(store, layer, tileWidthCm, tileHeightCm);
            if (mesh == null)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Ludots.Core.Mathematics.FixedPoint.Fix64.Zero);
            }

            return DetourNavQueryEngine.FindPath(mesh, areaCosts, startXcm, startZcm, goalXcm, goalZcm, maxPortals);
        }

        private DtNavMesh GetOrBuild(NavTileStore store, int layer, int tileWidthCm, int tileHeightCm)
        {
            // 单飞：装配在锁内执行——并发查询等待同一结果，不做重复构建。
            // 锁序恒为 cache → store（store 代码不反向触碰缓存），无死锁。
            lock (_gate)
            {
                uint version = store.LoadedVersion;
                if (_mesh != null &&
                    version == _loadedVersion &&
                    layer == _layer &&
                    tileWidthCm == _tileWidthCm &&
                    tileHeightCm == _tileHeightCm)
                {
                    return _mesh;
                }

                NavTile[] tiles = store.SnapshotLoadedTiles();
                DtNavMesh? built = DetourNavQueryEngine.BuildDetourNavMesh(tiles, layer, tileWidthCm, tileHeightCm);
                BuildCount++;
                if (built != null && store.LoadedVersion == version)
                {
                    _mesh = built;
                    _loadedVersion = version;
                    _layer = layer;
                    _tileWidthCm = tileWidthCm;
                    _tileHeightCm = tileHeightCm;
                }

                // built == null（无可查 tile）或装配期间版本又变（下次查询按新版本重建）都不发布缓存
                return built!;
            }
        }
    }
}
