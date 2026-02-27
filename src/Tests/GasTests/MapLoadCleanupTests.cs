using System;
using NUnit.Framework;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace GasTests
{
    [TestFixture]
    public class MapLoadCleanupTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        private static BoardConfig DefaultBoardConfig() => new BoardConfig
        {
            Name = "default",
            SpatialType = "Grid",
            WidthInTiles = 1,
            HeightInTiles = 1,
            GridCellSizeCm = 100,
            ChunkSizeCells = 4
        };

        private static MapSession CreateSessionWithBoard(string mapId)
        {
            var cfg = new MapConfig { Id = mapId };
            var session = new MapSession(new MapId(mapId), cfg);
            var boardCfg = DefaultBoardConfig();
            var board = new GridBoard(new BoardId(boardCfg.Name), boardCfg.Name, boardCfg);
            session.AddBoard(board);
            return session;
        }

        private Entity SpawnMapEntity(MapSession session, string mapId, int cellX, int cellY)
        {
            var entity = _world.Create(new MapEntity { MapId = new MapId(mapId) });
            var board = session.PrimaryBoard;
            if (board?.SpatialPartition != null)
                board.SpatialPartition.Add(entity, cellX, cellY);
            return entity;
        }

        [Test]
        public void LoadMap_ClearsOldEntities()
        {
            var session = CreateSessionWithBoard("map_a");

            // Spawn 5 entities for "map_a"
            for (int i = 0; i < 5; i++)
                SpawnMapEntity(session, "map_a", i, 0);

            // Verify 5 map entities exist
            int countBefore = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countBefore++);
            Assert.That(countBefore, Is.EqualTo(5));

            // Cleanup via MapSession
            session.Cleanup(_world);

            // Verify all map entities destroyed
            int countAfter = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countAfter++);
            Assert.That(countAfter, Is.EqualTo(0));
        }

        [Test]
        public void LoadMap_RebuildsSpatialIndex()
        {
            var session = CreateSessionWithBoard("map_a");
            var partition = session.PrimaryBoard.SpatialPartition;

            // Add entities to spatial partition via board
            SpawnMapEntity(session, "map_a", 2, 3);
            SpawnMapEntity(session, "map_a", 4, 5);

            // Verify spatial partition has data
            Span<Entity> buffer = stackalloc Entity[16];
            int countBefore = partition.Query(new IntRect(0, 0, 10, 10), buffer, out _);
            Assert.That(countBefore, Is.EqualTo(2));

            // Cleanup — disposes boards which clears spatial partition
            session.Cleanup(_world);

            // Spatial partition should be empty after board disposal
            int countAfter = partition.Query(new IntRect(0, 0, 10, 10), buffer, out _);
            Assert.That(countAfter, Is.EqualTo(0));
        }

        [Test]
        public void LoadMap_MultipleLoads_NoAccumulation()
        {
            var sessionA = CreateSessionWithBoard("map_a");

            // Simulate loading map_a
            for (int i = 0; i < 3; i++)
                SpawnMapEntity(sessionA, "map_a", i, 0);

            // Cleanup map_a
            sessionA.Cleanup(_world);

            // Simulate loading map_b
            var sessionB = CreateSessionWithBoard("map_b");
            for (int i = 0; i < 4; i++)
                SpawnMapEntity(sessionB, "map_b", i, 1);

            // Only map_b entities should exist
            int count = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => count++);
            Assert.That(count, Is.EqualTo(4));

            // Cleanup map_b
            sessionB.Cleanup(_world);

            int countFinal = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countFinal++);
            Assert.That(countFinal, Is.EqualTo(0));
        }
    }
}
