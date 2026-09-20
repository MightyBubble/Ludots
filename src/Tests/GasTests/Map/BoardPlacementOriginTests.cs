using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
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
