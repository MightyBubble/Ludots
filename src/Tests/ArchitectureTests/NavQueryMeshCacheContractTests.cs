using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// 查询侧 Detour 网格缓存契约（#1129 查询性能债）：
    /// 同一已加载 tile 集合的重复寻路只装配一次网格；tile 集合任何变化（含惰性加载）都必须重建。
    /// </summary>
    [TestFixture]
    public sealed class NavQueryMeshCacheContractTests
    {
        private const int CellSizeCm = 250;
        private const int ChunkSizeCells = 64;
        private const int TileSizeCm = CellSizeCm * ChunkSizeCells;

        [Test]
        public void MeshCache_RepeatedQueriesAndCrossServiceInstances_BuildOnce()
        {
            NavTileStore store = CreateStore(CreateTile(0, 0), CreateTile(1, 0));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                TileSizeCm,
                TileSizeCm);

            registry.TryCreateQuery(0, 0, null!, out NavQueryService first);
            for (int i = 0; i < 5; i++)
            {
                NavPathResult path = first.TryFindPath(13000, 1000, 17000, 1000);
                Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), $"query {i}");
            }

            // AgentBridge 每请求新建 service 实例——缓存必须在 store 粒度跨实例共享
            registry.TryCreateQuery(0, 0, null!, out NavQueryService second);
            Assert.That(second.TryFindPath(13000, 1000, 17000, 1000).Status, Is.EqualTo(NavPathStatus.Ok));

            Assert.That(registry.TryGetMeshCache(0, 0, out DetourQueryMeshCache cache), Is.True);
            Assert.That(cache.BuildCount, Is.EqualTo(1), "稳定 tile 集合上重复查询与跨 service 实例都应命中同一份网格");
        }

        [Test]
        public void MeshCache_PublishedTileChange_RebuildsOnce()
        {
            NavTileStore store = CreateStore(CreateTile(0, 0));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                TileSizeCm,
                TileSizeCm);
            registry.TryGetMeshCache(0, 0, out DetourQueryMeshCache cache);

            registry.TryCreateQuery(0, 0, null!, out NavQueryService service);
            Assert.That(service.TryFindPath(13000, 1000, 15000, 3000).Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(cache.BuildCount, Is.EqualTo(1));

            store.Replace(CreateTile(0, 0));

            Assert.That(service.TryFindPath(13000, 1000, 15000, 3000).Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(cache.BuildCount, Is.EqualTo(2), "Replace 推进 LoadedVersion，下一次查询必须重建网格");
        }

        [Test]
        public void MeshCache_LazyTileLoad_RebuildsOnce()
        {
            NavTileStore store = CreateStore(CreateTile(0, 0), CreateTile(1, 0));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                TileSizeCm,
                TileSizeCm);
            registry.TryGetMeshCache(0, 0, out DetourQueryMeshCache cache);

            registry.TryCreateQuery(0, 0, null!, out NavQueryService service);

            // 首查只触达 tile(0,0)（惰性加载，不推 Revision 只推 LoadedVersion）
            Assert.That(service.TryProject(13000, 1000, out _), Is.True);
            Assert.That(service.TryFindPath(13000, 1000, 15000, 1000).Status, Is.EqualTo(NavPathStatus.Ok));
            long buildsAfterFirstTile = cache.BuildCount;
            Assert.That(buildsAfterFirstTile, Is.GreaterThanOrEqualTo(1));

            // 跨 tile 查询触发 tile(1,0) 惰性加载 → LoadedVersion 变化 → 缓存失效重建
            Assert.That(service.TryFindPath(13000, 1000, 17000, 1000).Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(cache.BuildCount, Is.GreaterThan(buildsAfterFirstTile),
                "惰性装入新 tile 不推 Revision，但 LoadedVersion 必须让缓存重建，否则网格缺 tile");
        }

        private static NavTileStore CreateStore(params NavTile[] tiles)
        {
            var blobs = new Dictionary<NavTileId, byte[]>();
            foreach (NavTile tile in tiles)
            {
                using var ms = new MemoryStream();
                NavTileBinary.Write(ms, tile);
                blobs[tile.TileId] = ms.ToArray();
            }

            return new NavTileStore(id => new MemoryStream(blobs[id], writable: false));
        }

        private static NavTile CreateTile(int chunkX, int chunkY)
            => DefaultGridNavTileFactory.CreateFlatTile(chunkX, chunkY, layer: 0, tileVersion: 1, chunkSizeCells: ChunkSizeCells, cellSizeCm: CellSizeCm);
    }
}
