using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Satellite boards carry no consumers until BoardRef dispatch lands (#1567 slice 2):
    /// their partition/streaming/query state must stay unallocated at construction, and
    /// first access must build exactly one instance that the board keeps exposing.
    /// </summary>
    [TestFixture]
    public sealed class SatelliteBoardLazyInfrastructureTests
    {
        private static BoardConfig SatelliteBoard()
        {
            return new BoardConfig
            {
                Name = "arena",
                SpatialType = "Grid",
                WidthCm = 4_000,
                HeightCm = 3_000,
                Grid = new BoardGridAuthoring { CellSizeCm = 100 },
                Anchor = new BoardAnchor { WorldXCm = -400_000, WorldYCm = 300_000 },
                LoadedChunkCapacity = 4096,
            };
        }

        [Test]
        public void ConstructionAllocatesNoPartitionOrStreaming()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var board = new GridBoard(new BoardId("arena"), "arena", SatelliteBoard());
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            board.Dispose();

            Assert.That(delta, Is.LessThan(24_000),
                "board construction must stay at config-math cost; partition/streaming allocation is lazy");
        }

        [Test]
        public void FirstAccessBuildsSingleStableInstance()
        {
            var board = new GridBoard(new BoardId("arena"), "arena", SatelliteBoard());
            try
            {
                Assert.That(board.SpatialPartition, Is.SameAs(board.SpatialPartition));
                Assert.That(board.QueryService, Is.SameAs(board.QueryService));
                Assert.That(board.LoadedChunks, Is.SameAs(board.LoadedChunksSource));
            }
            finally
            {
                board.Dispose();
            }
        }

        [Test]
        public void HexBoardKeepsHexSemanticsOnLazyQueryService()
        {
            var config = SatelliteBoard();
            config.SpatialType = "HexGrid";
            config.Hex = new BoardHexAuthoring { EdgeLengthCm = 400 };
            var board = new HexGridBoard(new BoardId("arena"), "arena", config);
            try
            {
                Assert.That(board.QueryService, Is.SameAs(board.QueryService));
                Assert.That(board.WorldSize.Bounds.Width, Is.GreaterThan(0));
            }
            finally
            {
                board.Dispose();
            }
        }
    }
}
