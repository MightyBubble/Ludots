using System.Collections.Generic;
using System.IO;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// 非零 tile 坐标的运行时查询回归：Detour 基原点装配与查询注册表的地图 tile 尺寸传递（issue #1121）。
    /// </summary>
    [TestFixture]
    public sealed class NavQueryRuntimeNonZeroTileTests
    {
        private const int CellSizeCm = 250;
        private const int ChunkSizeCells = 64;
        private const int TileSizeCm = CellSizeCm * ChunkSizeCells;

        [Test]
        public void TryFindPath_NonZeroTile_ReturnsOk()
        {
            NavQueryService service = CreateQueryService(CreateTile(7, 5));

            NavPathResult result = service.TryFindPath(113000, 81000, 127000, 95000);

            Assert.That(result.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(result.PathXcm.Length, Is.GreaterThan(0));
            Assert.That(result.PathXcm[0], Is.EqualTo(113000));
            Assert.That(result.PathZcm[0], Is.EqualTo(81000));
            Assert.That(result.PathXcm[result.PathXcm.Length - 1], Is.EqualTo(127000));
            Assert.That(result.PathZcm[result.PathZcm.Length - 1], Is.EqualTo(95000));
        }

        [Test]
        public void TryFindPath_AdjacentNonZeroTiles_CrossesTileBorder()
        {
            NavQueryService service = CreateQueryService(CreateTile(7, 5), CreateTile(8, 5));

            NavPathResult result = service.TryFindPath(113000, 81000, 129000, 81000);

            Assert.That(result.Status, Is.EqualTo(NavPathStatus.Ok));
            Assert.That(result.PathXcm[0], Is.EqualTo(113000));
            Assert.That(result.PathXcm[result.PathXcm.Length - 1], Is.EqualTo(129000));
        }

        [Test]
        public void TryProject_LocatesNonZeroTile()
        {
            NavQueryService service = CreateQueryService(CreateTile(7, 5));

            Assert.That(service.TryProject(113000, 81000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void TryProject_UsesTerrainOriginWhenBoardIsCentered()
        {
            const int originXcm = -3_199_616;
            const int originZcm = -1_828_352;
            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                14,
                8,
                layer: 0,
                tileVersion: 1,
                TileSizeCm,
                TileSizeCm,
                ChunkSizeCells,
                ChunkSizeCells,
                boardOriginXcm: originXcm,
                boardOriginZcm: originZcm);
            var blobs = new Dictionary<NavTileId, byte[]>();
            using (var ms = new MemoryStream())
            {
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

            int worldXcm = originXcm + (14 * TileSizeCm) + 1000;
            int worldZcm = originZcm + (8 * TileSizeCm) + 1000;
            Assert.That(service.TryProject(worldXcm, worldZcm, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(14, 8, 0)));
        }

        [Test]
        public void GridTerrain_ChunkWorldSize_DerivesFromCellSizeAndChunkCells()
        {
            var terrain = new FlatGridLogicTerrainField(5440, 5440, cellSizeCm: CellSizeCm, chunkSizeCells: ChunkSizeCells);

            Assert.That(terrain.ChunkWidthCm, Is.EqualTo(16000));
            Assert.That(terrain.ChunkHeightCm, Is.EqualTo(16000));
        }

        [Test]
        public void HexTerrain_ChunkWorldSize_UsesHexSpacingNotEdgeLength()
        {
            var terrain = new VertexMapLogicTerrainField(new VertexMap());

            Assert.That(terrain.ChunkWidthCm, Is.EqualTo(44340));
            Assert.That(terrain.ChunkHeightCm, Is.EqualTo(38400));
        }

        private static NavQueryService CreateQueryService(params NavTile[] tiles)
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
                TileSizeCm);
            Assert.That(registry.TryCreateQuery(0, 0, null!, out NavQueryService service), Is.True);
            return service;
        }

        private static NavTile CreateTile(int chunkX, int chunkY)
            => DefaultGridNavTileFactory.CreateFlatTile(chunkX, chunkY, layer: 0, tileVersion: 1, chunkSizeCells: ChunkSizeCells, cellSizeCm: CellSizeCm);
    }
}
