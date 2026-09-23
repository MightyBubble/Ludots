using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Headless Stopwatch evidence for the #1567 slice-2 board configuration chain:
    /// multi-board placement validation + board construction (grid cells, hex footprint,
    /// anchored origins) + per-board coordinate round-trips. Fences are loose
    /// anti-catastrophe guards; the measured numbers go to the test log.
    /// </summary>
    [TestFixture]
    public sealed class BoardPlacementConfigBenchTests
    {
        private static MapConfig DualDomainMap()
        {
            var config = new MapConfig { Id = "bench-dual-domain" };
            config.Boards = new List<BoardConfig>
            {
                new() { Name = "world", SpatialType = "Grid", WidthCm = 800_000, HeightCm = 800_000, Grid = new BoardGridAuthoring { CellSizeCm = 100 }, LoadedChunkCapacity = 4096 },
                new() { Name = "arena_west", SpatialType = "Grid", WidthCm = 4_000, HeightCm = 3_000, Grid = new BoardGridAuthoring { CellSizeCm = 100 }, Anchor = new BoardAnchor { WorldXCm = 10_000, WorldYCm = 300_000 }, LoadedChunkCapacity = 4096 },
                new() { Name = "arena_east", SpatialType = "Grid", WidthCm = 12_800, HeightCm = 12_800, Grid = new BoardGridAuthoring { CellSizeCm = 200 }, Anchor = new BoardAnchor { WorldXCm = 200_000, WorldYCm = 350_000 }, LoadedChunkCapacity = 4096 },
                new() { Name = "harbor_south", SpatialType = "HexGrid", WidthCm = 19_800, HeightCm = 6_200, Grid = new BoardGridAuthoring { CellSizeCm = 100 }, Hex = new BoardHexAuthoring { EdgeLengthCm = 400 }, Anchor = new BoardAnchor { WorldXCm = 320_000, WorldYCm = 10_000 }, LoadedChunkCapacity = 4096 },
                new() { Name = "harbor_north", SpatialType = "HexGrid", WidthCm = 12_800, HeightCm = 19_800, Grid = new BoardGridAuthoring { CellSizeCm = 50 }, Hex = new BoardHexAuthoring { EdgeLengthCm = 400 }, Anchor = new BoardAnchor { WorldXCm = 50_000, WorldYCm = 500_000 }, LoadedChunkCapacity = 4096 },
            };
            return config;
        }

        [Test]
        public void MultiBoardValidateAndConstruct_StaysMicrosecondsPerBoard()
        {
            MapConfig config = DualDomainMap();
            var registry = new BoardIdRegistry();
            MapManager.ValidateSpatialDeclaration(config, new MapId("bench-dual-domain"));

            var boards = new IBoard[config.Boards.Count];
            var sw = Stopwatch.StartNew();
            int iterations = 100;
            for (int i = 0; i < iterations; i++)
            {
                for (int b = 0; b < config.Boards.Count; b++)
                {
                    boards[b]?.Dispose();
                    boards[b] = BoardFactory.Create(config.Boards[b], registry);
                }
            }
            sw.Stop();
            foreach (var board in boards)
            {
                board?.Dispose();
            }

            double totalBoards = iterations * (double)config.Boards.Count;
            double microsPerBoard = sw.Elapsed.TotalMicroseconds / totalBoards;
            TestContext.Out.WriteLine($"multi-board construct: boards={config.Boards.Count}, iters={iterations}, total={sw.Elapsed.TotalMilliseconds:F2}ms, per-board={microsPerBoard:F1}us");
            Assert.That(microsPerBoard, Is.LessThan(500.0), "board construction must stay micro-scale, not drift into IO/allocation storms");
        }

        [Test]
        public void AnchoredConverterRoundTrips_StaySubMicrosecond()
        {
            MapConfig config = DualDomainMap();
            var board = new HexGridBoard(new BoardId("harbor_south"), "harbor_south", config.Boards[3]);
            var converter = board.CoordinateConverter;
            var world = new WorldCmInt2(321_234, -139_876);
            for (int i = 0; i < 1000; i++)
            {
                var grid = converter.WorldToGrid(world);
                _ = converter.GridToWorld(grid);
            }
            var sw = Stopwatch.StartNew();
            int n = 200_000;
            for (int i = 0; i < n; i++)
            {
                var grid = converter.WorldToGrid(world);
                _ = converter.GridToWorld(grid);
            }
            sw.Stop();
            double nanosPerRoundTrip = sw.Elapsed.TotalNanoseconds / n;
            TestContext.Out.WriteLine($"anchored converter round-trip: n={n}, total={sw.Elapsed.TotalMilliseconds:F2}ms, per-trip={nanosPerRoundTrip:F0}ns");
            Assert.That(nanosPerRoundTrip, Is.LessThan(2000.0), "origin-offset conversion must stay pure math cost");
        }
    }
}
