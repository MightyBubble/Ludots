using System;
using NUnit.Framework;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace GasTests
{
    [TestFixture]
    public class MapLoadCleanupTests
    {
        private World _world;
        private ChunkedGridSpatialPartitionWorld _partition;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        private Entity SpawnMapEntity(string mapId, int cellX, int cellY)
        {
            var entity = _world.Create(new MapEntity { MapId = new MapId(mapId) });
            _partition.Add(entity, cellX, cellY);
            return entity;
        }

        [Test]
        public void LoadMap_ClearsOldEntities()
        {
            // Spawn 5 entities for "map_a"
            for (int i = 0; i < 5; i++)
                SpawnMapEntity("map_a", i, 0);

            // Verify 5 map entities exist
            int countBefore = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countBefore++);
            Assert.That(countBefore, Is.EqualTo(5));

            // Cleanup via MapSession
            var session = new MapSession(new MapId("map_a"), new MapConfig { Id = "map_a" });
            session.Cleanup(_world, _partition);

            // Verify all map entities destroyed
            int countAfter = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countAfter++);
            Assert.That(countAfter, Is.EqualTo(0));
        }

        [Test]
        public void LoadMap_RebuildsSpatialIndex()
        {
            // Add entities to spatial partition
            SpawnMapEntity("map_a", 2, 3);
            SpawnMapEntity("map_a", 4, 5);

            // Verify spatial partition has data
            Span<Entity> buffer = stackalloc Entity[16];
            int countBefore = _partition.Query(new IntRect(0, 0, 10, 10), buffer, out _);
            Assert.That(countBefore, Is.EqualTo(2));

            // Cleanup
            var session = new MapSession(new MapId("map_a"), new MapConfig { Id = "map_a" });
            session.Cleanup(_world, _partition);

            // Spatial partition should be empty
            int countAfter = _partition.Query(new IntRect(0, 0, 10, 10), buffer, out _);
            Assert.That(countAfter, Is.EqualTo(0));
        }

        [Test]
        public void LoadMap_MultipleLoads_NoAccumulation()
        {
            // Simulate loading map_a
            for (int i = 0; i < 3; i++)
                SpawnMapEntity("map_a", i, 0);

            // Cleanup map_a, then simulate loading map_b
            var sessionA = new MapSession(new MapId("map_a"), new MapConfig { Id = "map_a" });
            sessionA.Cleanup(_world, _partition);

            for (int i = 0; i < 4; i++)
                SpawnMapEntity("map_b", i, 1);

            // Only map_b entities should exist
            int count = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => count++);
            Assert.That(count, Is.EqualTo(4));

            // Cleanup map_b
            var sessionB = new MapSession(new MapId("map_b"), new MapConfig { Id = "map_b" });
            sessionB.Cleanup(_world, _partition);

            int countFinal = 0;
            _world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => countFinal++);
            Assert.That(countFinal, Is.EqualTo(0));
        }
    }
}
