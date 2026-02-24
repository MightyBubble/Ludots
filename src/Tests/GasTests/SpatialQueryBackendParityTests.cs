using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Physics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class SpatialQueryBackendParityTests
    {
        [Test]
        public void QueryAabb_And_QueryRadius_MatchBetweenPhysicsAndGridBackends()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);

                var physicsWorld = new PhysicsWorld();
                var gridWorld = new GridSpatialPartitionWorld(cellSize: PhysicsWorld.CellSize);

                var physicsService = new SpatialQueryService(new PhysicsWorldSpatialBackend(physicsWorld, coords));
                var gridService = new SpatialQueryService(new GridSpatialPartitionBackend(gridWorld, coords));

                var rng = new Random(12345);
                const int entityCount = 200;
                for (int i = 0; i < entityCount; i++)
                {
                    var e = world.Create();
                    int x = rng.Next(0, 512);
                    int y = rng.Next(0, 512);
                    int w = rng.Next(0, 4);
                    int h = rng.Next(0, 4);
                    var rect = new IntRect(x, y, w, h);
                    physicsWorld.Add(e, rect);
                    gridWorld.Add(e, rect);
                }

                for (int q = 0; q < 20; q++)
                {
                    int x = rng.Next(0, 512);
                    int y = rng.Next(0, 512);
                    int w = rng.Next(1, 64);
                    int h = rng.Next(1, 64);
                    var bounds = new WorldAabbCm(x * 100, y * 100, w * 100, h * 100);

                    Span<Entity> a = stackalloc Entity[256];
                    Span<Entity> b = stackalloc Entity[256];
                    var ra = physicsService.QueryAabb(bounds, a);
                    var rb = gridService.QueryAabb(bounds, b);

                    That(rb.Count, Is.EqualTo(ra.Count));
                    That(rb.Dropped, Is.EqualTo(ra.Dropped));
                    for (int i = 0; i < ra.Count; i++)
                    {
                        That(b[i], Is.EqualTo(a[i]));
                    }
                }

                for (int q = 0; q < 20; q++)
                {
                    int cx = rng.Next(0, 512);
                    int cy = rng.Next(0, 512);
                    int radiusCells = rng.Next(1, 64);
                    WorldCmInt2 center = coords.GridToWorld(new IntVector2(cx, cy));
                    int radiusCm = radiusCells * coords.GridCellSizeCm;

                    Span<Entity> a = stackalloc Entity[256];
                    Span<Entity> b = stackalloc Entity[256];
                    var ra = physicsService.QueryRadius(center, radiusCm, a);
                    var rb = gridService.QueryRadius(center, radiusCm, b);

                    That(rb.Count, Is.EqualTo(ra.Count));
                    That(rb.Dropped, Is.EqualTo(ra.Dropped));
                    for (int i = 0; i < ra.Count; i++)
                    {
                        That(b[i], Is.EqualTo(a[i]));
                    }
                }
            }
            finally
            {
                world.Dispose();
            }
        }

        /// <summary>
        /// Verify Grid and Chunked backends produce identical results through the ISpatialPartitionWorld interface.
        /// </summary>
        [Test]
        public void ISpatialPartitionWorld_GridAndChunked_QueryParity()
        {
            var world = World.Create();
            try
            {
                ISpatialPartitionWorld gridWorld = new GridSpatialPartitionWorld(cellSize: 1, initialCellCapacity: 256);
                ISpatialPartitionWorld chunkedWorld = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 4, initialChunkCapacity: 64);

                var rng = new Random(54321);
                var entities = new List<Entity>();
                var positions = new List<(int x, int y)>();

                // Insert 100 entities at random cell positions
                for (int i = 0; i < 100; i++)
                {
                    var e = world.Create();
                    int cx = rng.Next(0, 64);
                    int cy = rng.Next(0, 64);
                    gridWorld.Add(e, cx, cy);
                    chunkedWorld.Add(e, cx, cy);
                    entities.Add(e);
                    positions.Add((cx, cy));
                }

                // Query various regions and compare
                for (int q = 0; q < 30; q++)
                {
                    int x = rng.Next(0, 50);
                    int y = rng.Next(0, 50);
                    int w = rng.Next(1, 15);
                    int h = rng.Next(1, 15);
                    var rect = new IntRect(x, y, w, h);

                    Span<Entity> gridBuf = stackalloc Entity[128];
                    Span<Entity> chunkedBuf = stackalloc Entity[128];
                    int gridCount = gridWorld.Query(in rect, gridBuf, out int gridDropped);
                    int chunkedCount = chunkedWorld.Query(in rect, chunkedBuf, out int chunkedDropped);

                    // Both should return same entities (sort for deterministic comparison)
                    var gridSet = new HashSet<Entity>();
                    var chunkedSet = new HashSet<Entity>();
                    for (int i = 0; i < gridCount; i++) gridSet.Add(gridBuf[i]);
                    for (int i = 0; i < chunkedCount; i++) chunkedSet.Add(chunkedBuf[i]);

                    That(chunkedSet.Count, Is.EqualTo(gridSet.Count), $"Query {q}: count mismatch");
                    That(chunkedSet.SetEquals(gridSet), Is.True, $"Query {q}: entity set mismatch");
                }

                // Test Remove parity
                for (int i = 0; i < 10; i++)
                {
                    gridWorld.Remove(entities[i], positions[i].x, positions[i].y);
                    chunkedWorld.Remove(entities[i], positions[i].x, positions[i].y);
                }

                // Verify post-removal query parity
                for (int q = 0; q < 10; q++)
                {
                    int x = rng.Next(0, 50);
                    int y = rng.Next(0, 50);
                    var rect = new IntRect(x, y, 10, 10);

                    Span<Entity> gridBuf = stackalloc Entity[128];
                    Span<Entity> chunkedBuf = stackalloc Entity[128];
                    int gridCount = gridWorld.Query(in rect, gridBuf, out _);
                    int chunkedCount = chunkedWorld.Query(in rect, chunkedBuf, out _);

                    var gridSet = new HashSet<Entity>();
                    var chunkedSet = new HashSet<Entity>();
                    for (int i = 0; i < gridCount; i++) gridSet.Add(gridBuf[i]);
                    for (int i = 0; i < chunkedCount; i++) chunkedSet.Add(chunkedBuf[i]);

                    That(chunkedSet.Count, Is.EqualTo(gridSet.Count), $"Post-remove query {q}: count mismatch");
                    That(chunkedSet.SetEquals(gridSet), Is.True, $"Post-remove query {q}: entity set mismatch");
                }
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}

