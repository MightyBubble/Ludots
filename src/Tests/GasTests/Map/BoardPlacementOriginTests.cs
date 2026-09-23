using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class BoardPlacementOriginTests
    {
        private static MapConfig ConfigWithBoards(params BoardConfig[] boards)
        {
            return new MapConfig { Id = "dual-board-test", Boards = new List<BoardConfig>(boards) };
        }

        private static BoardConfig Board(string name, string type, int widthCells, int heightCells,
            int cellSizeCm = 100, int worldX = 0, int worldY = 0, int localX = 0, int localY = 0)
        {
            var config = new BoardConfig
            {
                Name = name,
                SpatialType = type,
                WidthCm = widthCells * cellSizeCm,
                HeightCm = heightCells * cellSizeCm,
                Grid = new BoardGridAuthoring { CellSizeCm = cellSizeCm },
                Anchor = new BoardAnchor
                {
                    LocalXCm = localX,
                    LocalYCm = localY,
                    WorldXCm = worldX,
                    WorldYCm = worldY,
                },
                LoadedChunkCapacity = 4096,
            };
            if (type is "HexGrid" or "Hex")
            {
                config.Hex = new BoardHexAuthoring { EdgeLengthCm = 400 };
            }

            return config;
        }

        [Test]
        public void RootBoardWorldAnchorMustBeLudotsOrigin()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100, worldX: -5000, worldY: -5000));
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("Ludots origin"));
        }

        [Test]
        public void SatelliteMissingAnchorIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40));
            config.Boards[1].Anchor = null;
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("Anchor"));
        }

        [Test]
        public void SatelliteOutsideRootFrameIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40, worldX: 7000, worldY: 0));
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("exits the root board frame"));
        }

        [Test]
        public void DisjointGridAndHexSatellitesValidate()
        {
            var config = ConfigWithBoards(
                Board("world", "Grid", 8000, 8000, cellSizeCm: 100),
                Board("arena", "Grid", 40, 40, worldX: 10_000, worldY: 200_000),
                Board("harbor", "HexGrid", 24, 24, worldX: 320_000, worldY: 140_000));
            MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test"));
        }

        [Test]
        public void AnchorPinsGridCornerAtWorldMinusLocal()
        {
            var config = Board("arena", "Grid", 40, 40, cellSizeCm: 100, worldX: 320_000, worldY: -140_000);
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
            var config = Board("harbor", "HexGrid", 24, 24, cellSizeCm: 100, worldX: 320_000, worldY: -140_000);
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
                Board("arena_west", "Grid", 40, 30, worldX: 10_000, worldY: 300_000),
                Board("arena_east", "Grid", 64, 64, cellSizeCm: 200, worldX: 200_000, worldY: 350_000),
                Board("harbor_south", "HexGrid", 24, 24, worldX: 320_000, worldY: 10_000),
                Board("harbor_north", "HexGrid", 16, 32, cellSizeCm: 50, worldX: 10_000, worldY: 400_000));
            MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test"));

            var aabbByBoard = new Dictionary<string, WorldAabbCm>();
            foreach (BoardConfig boardConfig in config.Boards)
            {
                var board = BoardFactory.Create(boardConfig, new BoardIdRegistry());
                aabbByBoard[boardConfig.Name] = board.WorldSize.Bounds;
            }

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

            var west = aabbByBoard["arena_west"];
            That(west.Width, Is.EqualTo(4000));
            var east = aabbByBoard["arena_east"];
            That(east.Width, Is.EqualTo(12800));
            That(east.Left, Is.EqualTo(200_000));
        }

        [Test]
        public void HexRectangleFloorsToWholeHexes()
        {
            var config = Board("harbor", "HexGrid", 1, 1, cellSizeCm: 100);
            config.WidthCm = 19_800;
            config.HeightCm = 6_200;
            config.Hex = new BoardHexAuthoring { EdgeLengthCm = 400 };
            var board = new HexGridBoard(new BoardId("harbor"), "harbor", config);

            That(board.WorldSize.Bounds.Width, Is.EqualTo(19_800));
            That(board.WorldSize.Bounds.Height, Is.EqualTo(6_200));
            That(board.HexMetrics.TryCountFittingHexes(19_800, 6_200, out int widthHexes, out int heightHexes), Is.True);
            That(widthHexes, Is.EqualTo(24));
            That(heightHexes, Is.EqualTo(10));
        }

        [Test]
        public void GridCellCountFloorsRemainderOntoTheFarSide()
        {
            var config = Board("arena", "Grid", 1, 1, cellSizeCm: 100);
            config.WidthCm = 4_050;
            config.HeightCm = 4_050;
            var board = new GridBoard(new BoardId("arena"), "arena", config);

            That(board.BoardExtent.WidthCells, Is.EqualTo(40));
            That(board.WorldSize.Bounds.Width, Is.EqualTo(4_050));
            That(board.WorldSize.Bounds.Left, Is.EqualTo(0));
        }

        [Test]
        public void HexOnGridBoardIsRejected()
        {
            var config = ConfigWithBoards(
                Board("default", "Grid", 100, 100),
                Board("arena", "Grid", 40, 40));
            config.Boards[1].Hex = new BoardHexAuthoring { EdgeLengthCm = 400 };
            var ex = Throws<InvalidOperationException>(() => MapManager.ValidateSpatialDeclaration(config, new MapId("dual-board-test")));
            That(ex!.Message, Does.Contain("HexGrid-only"));
        }

        [Test]
        public void CellCornerStaysAtLudotsOriginWhenAnchorIsZero()
        {
            var config = Board("default", "Grid", 64, 32, cellSizeCm: 100);
            var board = new GridBoard(new BoardId("default"), "default", config);

            That(board.WorldSize.Bounds.Left, Is.EqualTo(0));
            That(board.WorldSize.Bounds.Top, Is.EqualTo(0));
            That(board.WorldSize.Bounds.Width, Is.EqualTo(6_400));

            var world = board.CoordinateConverter.GridToWorld(new IntVector2(0, 0));
            That(world.X, Is.EqualTo(50));
            That(world.Y, Is.EqualTo(50));
        }
    }
}
