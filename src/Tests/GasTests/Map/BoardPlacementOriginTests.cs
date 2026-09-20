using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Platform.Abstractions;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Ludots.Core.Map.Hex;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1567 slice 2b: declared board placement. Satellites anchor at a world min-corner,
    /// the root board stays centered, anchored AABBs must sit fully inside the root frame,
    /// and centered boards keep the legacy grid frame bit-for-bit (zero drift).
    /// </summary>
    [TestFixture]
    public sealed class BoardPlacementOriginTests
    {
        private static MapConfig ConfigWithBoards(params BoardConfig[] boards)
        {
            return new MapConfig { Id = "dual-board-test", Boards = new List<BoardConfig>(boards) };
        }

        private static BoardConfig Board(string name, string type, int widthCells, int heightCells,
            int cellSizeCm = 100, int? originX = null, int? originY = null)
        {
            return new BoardConfig
            {
                Name = name,
                SpatialType = type,
                WidthCells = widthCells,
                HeightCells = heightCells,
                GridCellSizeCm = cellSizeCm,
                OriginXCm = originX,
                OriginYcm = originY,
                LoadedChunkCapacity = 4096,
            };
        }

        [Test]
        public void RootBoardWithOriginIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100, originX: -5000, originY: -5000));
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("anchors the centered world"));
        }

        [Test]
        public void SingleAxisOriginIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40, originX: 0));
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("together"));
        }

        [Test]
        public void AnchoredSatelliteOutsideRootFrameIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40, originX: 7000, originY: 0));
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("exits the root board frame"));
        }

        [Test]
        public void DisjointGridAndHexSatellitesValidate()
        {
            var config = ConfigWithBoards(
                Board("world", "Grid", 8000, 8000, cellSizeCm: 100),
                Board("arena", "Grid", 40, 40, originX: -100_000, originY: 200_000),
                Board("harbor", "HexGrid", 24, 24, originX: 320_000, originY: -140_000));
            MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test"));
        }

        [Test]
        public void AnchoredGridBoardPinsGridFrameToDeclaredOrigin()
        {
            var config = Board("arena", "Grid", 40, 40, cellSizeCm: 100, originX: 320_000, originY: -140_000);
            var board = new GridBoard(new BoardId("arena"), "arena", config);

            That(board.WorldSize.Bounds.Left, Is.EqualTo(320_000));
            That(board.WorldSize.Bounds.Top, Is.EqualTo(-140_000));

            var world = board.CoordinateConverter.GridToWorld(new IntVector2(0, 0));
            That(world.X, Is.EqualTo(320_050));
            That(world.Y, Is.EqualTo(-139_950));
            var back = board.CoordinateConverter.WorldToGrid(world);
            That(back.X, Is.EqualTo(0));
            That(back.Y, Is.EqualTo(0));
        }

        [Test]
        public void AnchoredHexBoardOffsetsHexFrame()
        {
            var config = Board("harbor", "HexGrid", 24, 24, cellSizeCm: 100, originX: 320_000, originY: -140_000);
            var board = new HexGridBoard(new BoardId("harbor"), "harbor", config);

            var world = board.CoordinateConverter.HexToWorld(default(HexCoordinates));
            That(world.X, Is.EqualTo(320_000));
            That(world.Y, Is.EqualTo(-140_000));
            var back = board.CoordinateConverter.WorldToHex(new WorldCmInt2(320_000, -140_000));
            That(back, Is.EqualTo(default(HexCoordinates)));
        }

        [Test]
        public void MultipleGridAndHexSatellitesCoexist()
        {
            var config = ConfigWithBoards(
                Board("world", "Grid", 10000, 10000, cellSizeCm: 100),
                Board("arena_west", "Grid", 40, 30, originX: -400_000, originY: 300_000),
                Board("arena_east", "Grid", 64, 64, cellSizeCm: 200, originX: 200_000, originY: 350_000),
                Board("harbor_south", "HexGrid", 24, 24, originX: 320_000, originY: -140_000),
                Board("harbor_north", "HexGrid", 16, 32, cellSizeCm: 50, originX: -350_000, originY: -200_000));
            MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test"));

            var aabbByBoard = new System.Collections.Generic.Dictionary<string, WorldAabbCm>();
            foreach (BoardConfig boardConfig in config.Boards)
            {
                var board = BoardFactory.Create(boardConfig, new BoardIdRegistry());
                aabbByBoard[boardConfig.Name] = board.WorldSize.Bounds;
            }

            // 根板是画布：四块卫星全部落在根板内；卫星两两不重叠（本配置如此摆放）。
            WorldAabbCm root = aabbByBoard["world"];
            var satellites = new[] { "arena_west", "arena_east", "harbor_south", "harbor_north" };
            foreach (string name in satellites)
            {
                WorldAabbCm a = aabbByBoard[name];
                That(a.Left >= root.Left && a.Top >= root.Top &&
                     a.Left + (long)a.Width <= root.Left + (long)root.Width &&
                     a.Top + (long)a.Height <= root.Top + (long)root.Height,
                    Is.True, $"{name} must sit inside the root board frame");
            }
            for (int i = 0; i < satellites.Length; i++)
            {
                for (int j = i + 1; j < satellites.Length; j++)
                {
                    WorldAabbCm a = aabbByBoard[satellites[i]];
                    WorldAabbCm b = aabbByBoard[satellites[j]];
                    bool disjoint = a.Left + (long)a.Width <= b.Left || b.Left + (long)b.Width <= a.Left ||
                                    a.Top + (long)a.Height <= b.Top || b.Top + (long)b.Height <= a.Top;
                    That(disjoint, Is.True, $"{satellites[i]} and {satellites[j]} must not overlap as placed");
                }
            }

            // 异构度量共存：两块 grid 板不同 cell、两块 hex 板不同 cell，各自换算各自闭环。
            var west = aabbByBoard["arena_west"];
            That(west.Width, Is.EqualTo(4000));
            var east = aabbByBoard["arena_east"];
            That(east.Width, Is.EqualTo(12800));
            That(east.Left, Is.EqualTo(200_000));
        }

        [Test]
        public void HexBoardAuthoredInHexesDerivesNonSquareFootprint()
        {
            var config = Board("harbor", "HexGrid", 999, 999, cellSizeCm: 100);
            config.WidthHexes = 24;
            config.HeightHexes = 10;
            config.HexEdgeLengthCm = 400;
            var board = new HexGridBoard(new BoardId("harbor"), "harbor", config);

            // √3·400·(24 + 9/2) ≈ 19746cm → 198 cells；1.5·400·10 + 200 = 6200cm。
            That(board.WorldSize.Bounds.Width, Is.EqualTo(19_800));
            That(board.WorldSize.Bounds.Height, Is.EqualTo(6_200));
            That(board.BoardExtent.WidthCm, Is.GreaterThan(19_700));
            That(board.BoardExtent.HeightCm, Is.EqualTo(6_200));
        }

        [Test]
        public void HexBoardLegacyCellAuthoringStaysZeroDrift()
        {
            var config = Board("harbor", "HexGrid", 24, 10, cellSizeCm: 100);
            var board = new HexGridBoard(new BoardId("harbor"), "harbor", config);
            That(board.WorldSize.Bounds.Width, Is.EqualTo(2_400));
            That(board.WorldSize.Bounds.Height, Is.EqualTo(1_000));
        }

        [Test]
        public void HexMetricsOnNonHexBoardAreRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40));
            config.Boards[1].WidthHexes = 10;
            config.Boards[1].HeightHexes = 10;
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("HexGrid-only"));
        }

        [Test]
        public void SingleAxisHexAuthoringIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("harbor", "HexGrid", 40, 40));
            config.Boards[1].WidthHexes = 24;
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("together"));
        }

        [Test]
        public void CenteredBoardsKeepLegacyFrameZeroDrift()
        {
            var config = Board("default", "Grid", 64, 32, cellSizeCm: 100);
            var board = new GridBoard(new BoardId("default"), "default", config);

            That(board.WorldSize.Bounds.Left, Is.EqualTo(-3_200));
            That(board.WorldSize.Bounds.Top, Is.EqualTo(-1_600));
            That(board.WorldSize.Bounds.Width, Is.EqualTo(6_400));

            var world = board.CoordinateConverter.GridToWorld(new IntVector2(0, 0));
            That(world.X, Is.EqualTo(50));
            That(world.Y, Is.EqualTo(50));
            var negative = board.CoordinateConverter.GridToWorld(new IntVector2(-32, -16));
            That(negative.X, Is.EqualTo(-3_150));
            That(negative.Y, Is.EqualTo(-1_550));
        }
    }
}
