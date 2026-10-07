using System;
using System.Collections.Generic;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// #1567 slice 2 board-scoped query contract: one shared world partition, per-board
    /// query facades. A board facade must see every world-indexed entity (board-blind
    /// index), answer in the board's own frame semantics (hex metrics, extent), and stay
    /// stable per board name.
    /// </summary>
    [TestFixture]
    public sealed class BoardScopedQueryFacadeTests
    {
        private static SpatialQueryService BuildFacade(
            ChunkedGridSpatialPartitionWorld sharedPartition, IBoard board)
        {
            var service = new SpatialQueryService(
                new ChunkedGridSpatialPartitionBackend(sharedPartition, board.WorldSize));
            service.SetCoordinateConverter(board.CoordinateConverter);
            if (board is HexGridBoard hexBoard)
            {
                service.SetHexMetrics(hexBoard.HexMetrics);
            }
            return service;
        }

        private static BoardConfig Board(string name, string type, int w, int h, int worldX = 0, int worldY = 0)
        {
            var config = new BoardConfig
            {
                Name = name,
                SpatialType = type,
                WidthCm = w * 100,
                HeightCm = h * 100,
                Grid = new BoardGridAuthoring { CellSizeCm = 100 },
                Anchor = new BoardAnchor { WorldXCm = worldX, WorldYCm = worldY },
                LoadedChunkCapacity = 4096,
            };
            if (type is "HexGrid" or "Hex")
            {
                config.Hex = new BoardHexAuthoring { EdgeLengthCm = 400 };
            }

            return config;
        }

        [Test]
        public void BoardFacadeSeesWorldIndexedEntitiesInsideItsExtent()
        {
            // 世界索引 = 根板分区；实体全按世界 cm 落格（引擎语义）。
            var root = new GridBoard(new BoardId("world"), "world", Board("world", "Grid", 8000, 8000));
            ChunkedGridSpatialPartitionWorld partition = (ChunkedGridSpatialPartitionWorld)root.SpatialPartition;

            var world = Arch.Core.World.Create();
            try
            {
                var near = world.Create();
                var far = world.Create();
                partition.Add(near, cellX: 3200, cellY: -1399);   // 华南海域板内（世界 32万,-14万 cm 一带）
                partition.Add(far, cellX: -4000, cellY: 3000);    // 华东擂台板内

                var harbor = new HexGridBoard(
                    new BoardId("harbor"), "harbor",
                    Board("harbor", "HexGrid", 198, 62, 320_000, -140_000));
                var facade = BuildFacade(partition, harbor);

                // 板域查询：范围内实体可见（共享索引，板盲正确）。
                Span<Arch.Core.Entity> buffer = stackalloc Arch.Core.Entity[16];
                SpatialQueryResult result = facade.QueryRadius(
                    new WorldCmInt2(320_050, -139_950), 5_000, buffer);
                That(result.Count >= 1, Is.True, "harbor facade must see the world-indexed entity inside its extent");
                int seen = 0;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result.Count > 0 && buffer[i].Equals(near)) seen++;
                }
                That(seen, Is.EqualTo(1), "the in-extent entity is the one returned");

                // 板域语义：facade 挂的是 hex 板的度量与转换器。
                That(((SpatialCoordinateConverter)harbor.CoordinateConverter).OriginXCm, Is.EqualTo(320_000));
            }
            finally
            {
                world.Dispose();
                root.Dispose();
            }
        }

        [Test]
        public void GridSatelliteFacadeUsesItsOwnOriginFrame()
        {
            var root = new GridBoard(new BoardId("world"), "world", Board("world", "Grid", 8000, 8000));
            var arena = new GridBoard(
                new BoardId("arena"), "arena",
                Board("arena", "Grid", 40, 30, -400_000, 300_000));
            var facade = BuildFacade((ChunkedGridSpatialPartitionWorld)root.SpatialPartition, arena);

            That(arena.CoordinateConverter.GridToWorld(new IntVector2(0, 0)).X,
                Is.EqualTo(-399_950));
            That(facade, Is.Not.SameAs(root.QueryService));
            root.Dispose();
            arena.Dispose();
        }
    }
}
