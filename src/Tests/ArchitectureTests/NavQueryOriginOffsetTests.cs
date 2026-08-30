using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// NavTileGrid 显式 OriginXcm/OriginZcm 贯通回归：注册表与查询服务的
    /// LocateTile 必须以声明网格原点为参考系定位瓦片——非零负原点、负世界坐标、零边界
    /// 都必须命中声明的瓦片槽；零原点调用保持既有契约。热路径不得分配。
    /// </summary>
    [TestFixture]
    public sealed class NavQueryOriginOffsetTests
    {
        private const int CellSizeCm = 250;
        private const int ChunkSizeCells = 64;
        private const int TileSizeCm = CellSizeCm * ChunkSizeCells;

        [Test]
        public void TryProject_NonZeroNegativeOrigin_ResolvesDeclaredTile()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(
                originXcm,
                originZcm,
                CreateTile(0, 0, originXcm, originZcm),
                CreateTile(2, 1, originXcm, originZcm),
                CreateTile(3, 1, originXcm, originZcm));

            // 世界点 (25000,13000) 相对原点 (3000,17000) → tile (2,1)，local (1000,1000)
            Assert.That(service.TryProject(25000, 13000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(2, 1, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryProject_NegativeWorldCoords_InsideGrid_ResolvesTile00()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(originXcm, originZcm, CreateTile(0, 0, originXcm, originZcm));

            // 负世界坐标但仍在网格内：(-5000,-3000) 相对原点 (3000,1000) → tile (0,0)，local (3000,1000)
            Assert.That(service.TryProject(-5000, -3000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(3000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryProject_WorldBelowGridOrigin_ClampsToTile00()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(originXcm, originZcm, CreateTile(0, 0, originXcm, originZcm));

            // 世界坐标低于网格原点：相对原点为负，按既有契约钳到 tile (0,0)，不越界不抛错
            Assert.That(service.TryProject(-12000, -9000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
        }

        [Test]
        public void TryProject_ZeroBoundary_WorldEqualsGridOrigin_MapsToTile00()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(originXcm, originZcm, CreateTile(0, 0, originXcm, originZcm));

            Assert.That(service.TryProject(originXcm, originZcm, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(0));
            Assert.That(loc.LocalZcm, Is.EqualTo(0));
        }

        [Test]
        public void TryProject_TileEdgeBoundary_ResolvesHigherTile()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(
                originXcm,
                originZcm,
                CreateTile(0, 0, originXcm, originZcm),
                CreateTile(2, 1, originXcm, originZcm));

            // x == 原点 + 2*tileSize 恰好落在 tile 1 与 tile 2 的公共边上，floor 语义归属高 tile
            int edgeXcm = originXcm + 2 * TileSizeCm;
            Assert.That(service.TryProject(edgeXcm, 13000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(2, 1, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(0));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryProject_ZeroOriginLegacyConstructor_KeepsExistingContract()
        {
            // 三参构造（零原点）必须与修复前行为一致：113000 → tile (7,5)
            NavQueryService service = CreateQueryService(originXcm: 0, originZcm: 0, CreateTile(7, 5, 0, 0));

            Assert.That(service.TryProject(113000, 81000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryProject_ZeroOriginExplicitConstructor_MatchesLegacyContract()
        {
            // 五参构造显式传 0 原点必须与三参构造等价
            NavQueryService service = CreateQueryService(originXcm: 0, originZcm: 0, CreateTile(7, 5, 0, 0));

            Assert.That(service.TryProject(113000, 81000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryFindPath_NonZeroNegativeOrigin_CrossesTileBorder()
        {
            const int originXcm = -8000;
            const int originZcm = -4000;
            NavQueryService service = CreateQueryService(
                originXcm,
                originZcm,
                CreateTile(2, 1, originXcm, originZcm),
                CreateTile(3, 1, originXcm, originZcm));

            // tile (2,1) → tile (3,1) 跨界寻路，起终点必须原样返回
            NavPathResult result = service.TryFindPath(25000, 13000, 43000, 13000);

            Assert.That(result.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(result.PathXcm.Length, Is.GreaterThan(0));
            Assert.That(result.PathXcm[0], Is.EqualTo(25000));
            Assert.That(result.PathZcm[0], Is.EqualTo(13000));
            Assert.That(result.PathXcm[result.PathXcm.Length - 1], Is.EqualTo(43000));
            Assert.That(result.PathZcm[result.PathZcm.Length - 1], Is.EqualTo(13000));
        }

        [Test]
        public void Registry_ExposesDeclaredGridOrigin()
        {
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>(),
                TileSizeCm,
                TileSizeCm,
                originXcm: -8000,
                originZcm: -4000);

            Assert.That(registry.OriginXcm, Is.EqualTo(-8000));
            Assert.That(registry.OriginZcm, Is.EqualTo(-4000));
            Assert.That(registry.TileWidthCm, Is.EqualTo(TileSizeCm));
            Assert.That(registry.TileHeightCm, Is.EqualTo(TileSizeCm));
        }

        private static NavQueryService CreateQueryService(int originXcm, int originZcm, params NavTile[] tiles)
        {
            var blobs = new Dictionary<NavTileId, byte[]>();
            foreach (NavTile tile in tiles)
            {
                using var ms = new MemoryStream();
                NavTileBinary.Write(ms, tile);
                blobs[tile.TileId] = ms.ToArray();
            }

            var store = new NavTileStore(id => new MemoryStream(blobs[id], writable: false));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore> { [new NavQueryServiceKey(0, 0)] = store },
                TileSizeCm,
                TileSizeCm,
                originXcm,
                originZcm);
            Assert.That(registry.TryCreateQuery(0, 0, null!, out NavQueryService service), Is.True);
            return service;
        }

        /// <summary>
        /// 平面基准瓦片，原点按声明网格原点平移：tile (cx,cy) 的世界原点 =
        /// gridOrigin + (cx*tileSize, cy*tileSize)，与烘焙时 terrain 世界坐标给出的原点一致。
        /// </summary>
        private static NavTile CreateTile(int chunkX, int chunkY, int gridOriginXcm, int gridOriginZcm)
        {
            NavTile flat = DefaultGridNavTileFactory.CreateFlatTile(
                chunkX,
                chunkY,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: ChunkSizeCells,
                cellSizeCm: CellSizeCm);
            return ShiftOrigin(flat, gridOriginXcm, gridOriginZcm);
        }

        private static NavTile ShiftOrigin(NavTile tile, int dxCm, int dzCm)
        {
            return new NavTile(
                tile.TileId,
                tile.TileVersion,
                tile.BuildConfigHash,
                tile.Checksum,
                checked(tile.OriginXcm + dxCm),
                checked(tile.OriginZcm + dzCm),
                tile.VertexXcm,
                tile.VertexYcm,
                tile.VertexZcm,
                tile.TriA,
                tile.TriB,
                tile.TriC,
                tile.N0,
                tile.N1,
                tile.N2,
                tile.TriAreaIds,
                tile.Portals);
        }
    }
}
