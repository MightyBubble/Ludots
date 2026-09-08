using System.Collections.Generic;
using System.IO;
using Ludots.Core.Navigation.NavMesh;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// 每板寻址合同（issue #1346）：查询身份必须带 boardId，瓦片定位必须减 board origin，
    /// 同局部坐标在不同板互不覆盖。这些是运行时寻址的前置条件，不涉及烘焙管线。
    /// </summary>
    [TestFixture]
    public sealed class NavBoardAddressingContractTests
    {
        private const int CellSizeCm = 250;
        private const int ChunkSizeCells = 64;
        private const int TileSizeCm = CellSizeCm * ChunkSizeCells;
        private const int HexTileWidthCm = 44340;
        private const int HexTileHeightCm = 38400;

        [Test]
        public void QueryKey_DistinguishesBoardsAtSameLayerAndProfile()
        {
            var mainland = new NavQueryServiceKey("mainland", 0, 0);
            var harbor = new NavQueryServiceKey("harbor", 0, 0);

            Assert.That(mainland, Is.Not.EqualTo(harbor));
            Assert.That(mainland.GetHashCode(), Is.Not.EqualTo(harbor.GetHashCode()));
        }

        [Test]
        public void Registry_ResolvesStorePerBoard_WithoutCrossingBoards()
        {
            NavTileStore mainlandStore = CreateStore(CreateTile(0, 0));
            NavTileStore harborStore = CreateStore(CreateTile(0, 0));

            NavQueryServiceRegistry registry = CreateRegistry(
                ("mainland", mainlandStore),
                ("harbor", harborStore));

            Assert.That(registry.TryGetStore("mainland", 0, 0, out NavTileStore resolvedMainland), Is.True);
            Assert.That(registry.TryGetStore("harbor", 0, 0, out NavTileStore resolvedHarbor), Is.True);
            Assert.That(resolvedMainland, Is.SameAs(mainlandStore));
            Assert.That(resolvedHarbor, Is.SameAs(harborStore));
        }

        [Test]
        public void BoardScopedRegistry_WithoutBoardGeometry_FailsClosedOnLookup()
        {
            var stores = new Dictionary<NavQueryServiceKey, NavTileStore>
            {
                [new NavQueryServiceKey("mainland", 0, 0)] = CreateStore(CreateTile(0, 0))
            };

            var registry = new NavQueryServiceRegistry(stores, TileSizeCm, TileSizeCm);

            Assert.That(registry.TryGetBoardGeometry("mainland", out _), Is.False,
                "a board-scoped store without declared geometry must not borrow a global tile size");
            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out _), Is.False,
                "query creation must fail closed rather than guess the board geometry");
            Assert.That(
                () => registry.RequireBoardGeometry("mainland"),
                Throws.InvalidOperationException.With.Message.Contains("mainland"));
        }

        [Test]
        public void Registry_UnknownBoard_FailsClosedInsteadOfFallingBack()
        {
            NavQueryServiceRegistry registry = CreateRegistry(
                ("mainland", CreateStore(CreateTile(0, 0))));

            Assert.That(registry.TryGetBoardGeometry("harbor", out _), Is.False);
            Assert.That(
                () => registry.RequireBoardGeometry("harbor"),
                Throws.InvalidOperationException.With.Message.Contains("harbor"));
        }

        [Test]
        public void NonZeroBoardOrigin_LocatesBoardLocalTileInsteadOfZero()
        {
            const int originXcm = -3_200_000;
            const int originZcm = -1_800_000;
            var store = CreateStore(CreateTile(7, 5));

            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = store
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, originXcm, originZcm, 16, 16)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService service), Is.True);

            int worldXcm = originXcm + 7 * TileSizeCm + 1000;
            int worldZcm = originZcm + 5 * TileSizeCm + 1000;
            Assert.That(service.TryProject(worldXcm, worldZcm, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(1000));
        }

        [Test]
        public void QueryOutsideDeclaredBoardExtent_FailsClosed()
        {
            var store = CreateStore(CreateTile(0, 0), CreateTile(1, 0));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = store
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0, 4, 4)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService service), Is.True);

            Assert.That(service.TryProject(1 * TileSizeCm + 100, 100, out _), Is.True,
                "tile (1,0) is inside the declared 4x4 extent");
            Assert.That(service.TryProject(9 * TileSizeCm + 100, 100, out _), Is.False,
                "tile (9,0) is outside the declared 4x4 extent and must fail closed");
        }

        [Test]
        public void TwoBoards_SameLocalTileCoordinate_DoNotOverlap()
        {
            var mainlandStore = CreateStore(CreateTile(0, 0));
            var harborStore = CreateStore(CreateTile(0, 0));

            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = mainlandStore,
                    [new NavQueryServiceKey("harbor", 0, 0)] = harborStore
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0, 4, 4),
                    ["harbor"] = new NavBoardTileGeometry(HexTileWidthCm, HexTileHeightCm, 120_000, 45_000, 4, 4)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService mainland), Is.True);
            Assert.That(registry.TryCreateQuery("harbor", 0, 0, null!, out NavQueryService harbor), Is.True);

            Assert.That(mainland.TryProject(1000, 1000, out NavLocation mainlandLoc), Is.True);
            Assert.That(harbor.TryProject(121_000, 46_000, out NavLocation harborLoc), Is.True);

            Assert.That(mainlandLoc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(harborLoc.TileId, Is.EqualTo(new NavTileId(0, 0, 0)));
            Assert.That(mainland.TryProject(121_000, 46_000, out NavLocation mainlandMiss), Is.False,
                "mainland must not resolve a coordinate that belongs to the harbor board's address space");
        }

        [Test]
        public void TwoAxisTileGeometry_IsNotCollapsedToSquare()
        {
            var geometry = new NavBoardTileGeometry(HexTileWidthCm, HexTileHeightCm, 0, 0);

            Assert.That(geometry.TileWidthCm, Is.EqualTo(HexTileWidthCm));
            Assert.That(geometry.TileHeightCm, Is.EqualTo(HexTileHeightCm));
            Assert.That(geometry.TileWidthCm, Is.Not.EqualTo(geometry.TileHeightCm));
        }

        [Test]
        public void SingleBoardConstructor_StillUsesZeroOrigin()
        {
            var store = CreateStore(CreateTile(7, 5));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                },
                TileSizeCm,
                TileSizeCm);

            Assert.That(registry.TryCreateQuery(0, 0, null!, out NavQueryService service), Is.True);
            Assert.That(service.OriginXcm, Is.EqualTo(0));
            Assert.That(service.OriginZcm, Is.EqualTo(0));
            Assert.That(service.TryProject(113000, 81000, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
        }

        [Test]
        public void ArtifactPath_BoardScoped_IsolatesSameLocalTileCoordinate()
        {
            string mainland = NavAssetPaths.GetNavTileRelativePath("coastline", "mainland", 0, "infantry", 0, 0);
            string harbor = NavAssetPaths.GetNavTileRelativePath("coastline", "harbor", 0, "infantry", 0, 0);

            Assert.That(mainland, Is.Not.EqualTo(harbor));
            Assert.That(mainland, Is.EqualTo("assets/Data/Nav/coastline/board_mainland/layer0/profile_infantry/x00/navtile_0_0.ntil"));
            Assert.That(harbor, Is.EqualTo("assets/Data/Nav/coastline/board_harbor/layer0/profile_infantry/x00/navtile_0_0.ntil"));
        }

        [Test]
        public void ArtifactPath_SingleBoard_KeepsHistoricalShape()
        {
            string path = NavAssetPaths.GetNavTileRelativePath("navmesh_debug_grid", 0, "Large", 0, 1);

            Assert.That(
                path,
                Is.EqualTo("assets/Data/Nav/navmesh_debug_grid/layer0/profile_Large/x00/navtile_0_1.ntil"),
                "already-baked single-board artifacts must stay loadable at their historical path");
        }

        [Test]
        public void NegativeBoardLocalCoordinate_LocatesExactlyTheFloorTile()
        {
            var store = CreateStore(CreateTile(-1, -1));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = store
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService service), Is.True);
            Assert.That(service.TryProject(-1000, -1000, out NavLocation loc), Is.True);

            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(-1, -1, 0)),
                "-1000cm floors to tile -1; double-flooring would wrongly yield tile -2");
            Assert.That(loc.LocalXcm, Is.EqualTo(TileSizeCm - 1000));
            Assert.That(loc.LocalZcm, Is.EqualTo(TileSizeCm - 1000));
        }

        [Test]
        public void UnscopedStoreLookup_OnBoardScopedRegistry_FailsClosed()
        {
            NavQueryServiceRegistry registry = CreateRegistry(
                ("mainland", CreateStore(CreateTile(0, 0))),
                ("harbor", CreateStore(CreateTile(0, 0))));

            Assert.That(
                () => registry.TryGetStore(0, 0, out _),
                Throws.InvalidOperationException.With.Message.Contains("boardId"),
                "a multi-board registry must not silently pick one board's store");
        }

        [Test]
        public void SingleBoardRegistry_WithNonZeroOrigin_AppliesThatOrigin()
        {
            const int originXcm = -3_200_000;
            const int originZcm = -1_800_000;
            var store = CreateStore(CreateTile(7, 5));

            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = store
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    [string.Empty] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, originXcm, originZcm, 16, 16)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery(0, 0, null!, out NavQueryService service), Is.True);
            Assert.That(service.OriginXcm, Is.EqualTo(originXcm),
                "a single-board map with a non-zero origin must still use that origin");

            int worldXcm = originXcm + 7 * TileSizeCm + 1000;
            int worldZcm = originZcm + 5 * TileSizeCm + 1000;
            Assert.That(service.TryProject(worldXcm, worldZcm, out NavLocation loc), Is.True);
            Assert.That(loc.TileId, Is.EqualTo(new NavTileId(7, 5, 0)));
            Assert.That(loc.LocalXcm, Is.EqualTo(1000));
        }

        [Test]
        public void NonZeroOriginBoard_FindsPathInWorldCoordinates()
        {
            const int originXcm = -3_200_000;
            const int originZcm = -1_800_000;
            var store = CreateStore(CreateTile(0, 0), CreateTile(1, 0));

            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = store
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, originXcm, originZcm, 4, 4)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreateQuery("mainland", 0, 0, null!, out NavQueryService service), Is.True);

            int startX = originXcm + 1000;
            int startZ = originZcm + 1000;
            int goalX = originXcm + TileSizeCm + 1000;
            int goalZ = originZcm + 1000;

            NavPathResult result = service.TryFindPath(startX, startZ, goalX, goalZ);

            Assert.That(result.Status, Is.EqualTo(NavPathStatus.Ok),
                "a board-rooted query must translate endpoints into board-local space before the Detour search");
            Assert.That(result.PathXcm[0], Is.EqualTo(startX),
                "result waypoints must be translated back to world coordinates");
            Assert.That(result.PathZcm[0], Is.EqualTo(startZ));
            Assert.That(result.PathXcm[result.PathXcm.Length - 1], Is.EqualTo(goalX));
            Assert.That(result.PathZcm[result.PathZcm.Length - 1], Is.EqualTo(goalZ));
        }

        [Test]
        public void PrimaryBoard_IsDefaultNamedBoard_OtherwiseFirstRegistered()
        {
            NavQueryServiceRegistry withDefault = CreateRegistry(
                ("mainland", CreateStore(CreateTile(0, 0))),
                ("default", CreateStore(CreateTile(0, 0))));
            Assert.That(withDefault.PrimaryBoardId, Is.EqualTo("default"));

            NavQueryServiceRegistry withoutDefault = CreateRegistry(
                ("mainland", CreateStore(CreateTile(0, 0))),
                ("harbor", CreateStore(CreateTile(0, 0))));
            Assert.That(withoutDefault.PrimaryBoardId, Is.Not.Null.And.Not.Empty);

            NavQueryServiceRegistry single = new(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey(0, 0)] = CreateStore(CreateTile(0, 0))
                },
                TileSizeCm,
                TileSizeCm);
            Assert.That(single.PrimaryBoardId, Is.Null,
                "a single-board registry has no ambiguity and needs no primary board");
        }

        [Test]
        public void PrimaryQuery_OnBoardScopedRegistry_ResolvesThePrimaryBoard()
        {
            var mainland = CreateStore(CreateTile(0, 0));
            var defaultBoard = CreateStore(CreateTile(0, 0));
            var registry = new NavQueryServiceRegistry(
                new Dictionary<NavQueryServiceKey, NavTileStore>
                {
                    [new NavQueryServiceKey("mainland", 0, 0)] = mainland,
                    [new NavQueryServiceKey("default", 0, 0)] = defaultBoard
                },
                new Dictionary<string, NavBoardTileGeometry>
                {
                    ["mainland"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0),
                    ["default"] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0)
                },
                fallbackWidthCm: TileSizeCm,
                fallbackHeightCm: TileSizeCm);

            Assert.That(registry.TryCreatePrimaryQuery(0, 0, null!, out NavQueryService service), Is.True);
            Assert.That(registry.TryGetPrimaryStore(0, 0, out NavTileStore store), Is.True);
            Assert.That(store, Is.SameAs(defaultBoard),
                "boardless callers must land on the 'default' board, not an arbitrary one");
        }

        [Test]
        public void IsBoardScoped_MatchesTheLoaderRule()
        {
            Assert.That(NavAssetPaths.IsBoardScoped(new[] { true }), Is.False);
            Assert.That(NavAssetPaths.IsBoardScoped(new[] { true, false }), Is.False);
            Assert.That(NavAssetPaths.IsBoardScoped(new[] { true, true }), Is.True,
                "two boards with grids must both be addressed by board id");
            Assert.That(NavAssetPaths.IsBoardScoped(new[] { false, false }), Is.False);
        }

        private static NavQueryServiceRegistry CreateRegistry(params (string BoardId, NavTileStore Store)[] boards)
        {
            var stores = new Dictionary<NavQueryServiceKey, NavTileStore>();
            var geometry = new Dictionary<string, NavBoardTileGeometry>();
            foreach ((string boardId, NavTileStore store) in boards)
            {
                stores[new NavQueryServiceKey(boardId, 0, 0)] = store;
                geometry[boardId] = new NavBoardTileGeometry(TileSizeCm, TileSizeCm, 0, 0);
            }

            return new NavQueryServiceRegistry(stores, geometry, TileSizeCm, TileSizeCm);
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

            return new NavTileStore(id => new MemoryStream(
                blobs.TryGetValue(id, out byte[]? blob)
                    ? blob
                    : throw new FileNotFoundException($"No nav tile baked for {id}."),
                writable: false));
        }

        private static NavTile CreateTile(int chunkX, int chunkY)
            => DefaultGridNavTileFactory.CreateFlatTile(
                chunkX,
                chunkY,
                layer: 0,
                tileVersion: 1,
                chunkSizeCells: ChunkSizeCells,
                cellSizeCm: CellSizeCm);
    }
}
