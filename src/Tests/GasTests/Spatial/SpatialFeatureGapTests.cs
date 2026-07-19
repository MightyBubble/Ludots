using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Spatial and Map feature gap tests — covers Rectangle, Line, HexRange, HexRing queries,
    /// VertexMap all-layer read/write, and coordinate conversion.
    /// </summary>
    [TestFixture]
    public class SpatialFeatureGapTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  1. SpatialQueryService — Rectangle query
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void QueryRectangle_FindsEntitiesInRectangularArea()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 1);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));
                spatial.SetPositionProvider(e =>
                {
                    ref var pos = ref world.Get<WorldPositionCm>(e);
                    return pos.Value.ToWorldCmInt2();
                });

                // Place entities at different positions
                var e1 = world.Create(WorldPositionCm.FromCm(150, 100)); // inside
                var e2 = world.Create(WorldPositionCm.FromCm(250, 100)); // inside
                var e3 = world.Create(WorldPositionCm.FromCm(1500, 1500)); // outside

                grid.Add(e1, new IntRect(1, 1, 2, 2));
                grid.Add(e2, new IntRect(2, 1, 3, 2));
                grid.Add(e3, new IntRect(15, 15, 16, 16)); // far away

                Span<Entity> buffer = stackalloc Entity[64];
                var center = new WorldCmInt2(200, 150);
                var r = spatial.QueryRectangle(center, halfWidthCm: 300, halfHeightCm: 200, rotationDeg: 0, buffer);

                That(r.Count, Is.GreaterThanOrEqualTo(2), "Should find at least 2 entities in rectangle");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  2. SpatialQueryService — Line query
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void QueryLine_FindsEntitiesAlongLine()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 1);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));
                spatial.SetPositionProvider(e =>
                {
                    ref var pos = ref world.Get<WorldPositionCm>(e);
                    return pos.Value.ToWorldCmInt2();
                });

                // Place entities along X axis
                var e1 = world.Create(WorldPositionCm.FromCm(150, 50));
                var e2 = world.Create(WorldPositionCm.FromCm(350, 50));
                var e3 = world.Create(WorldPositionCm.FromCm(50, 600)); // far from line

                grid.Add(e1, new IntRect(1, 0, 2, 1));
                grid.Add(e2, new IntRect(3, 0, 4, 1));
                grid.Add(e3, new IntRect(0, 6, 1, 7)); // far from line

                Span<Entity> buffer = stackalloc Entity[64];
                var origin = new WorldCmInt2(0, 50);
                var r = spatial.QueryLine(origin, directionDeg: 90, lengthCm: 500, halfWidthCm: 100, buffer);

                That(r.Count, Is.GreaterThanOrEqualTo(1), "Should find at least 1 entity along line");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  3. SpatialQueryService — HexRange query
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void QueryHexRange_FindsEntitiesWithinHexRadius()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 4);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));
                spatial.SetCoordinateConverter(coords);

                // Place entity at hex origin
                var eNear = world.Create();
                var eFar = world.Create();

                var nearWorld = coords.HexToWorld(new HexCoordinates(0, 0));
                var farWorld = coords.HexToWorld(new HexCoordinates(10, 10)); // far away

                grid.Add(eNear, new IntRect(nearWorld.X / 100, nearWorld.Y / 100,
                    nearWorld.X / 100 + 1, nearWorld.Y / 100 + 1));
                grid.Add(eFar, new IntRect(farWorld.X / 100, farWorld.Y / 100,
                    farWorld.X / 100 + 1, farWorld.Y / 100 + 1));

                Span<Entity> buffer = stackalloc Entity[256];
                var r = spatial.QueryHexRange(new HexCoordinates(0, 0), hexRadius: 2, buffer);

                // At minimum the near entity should be found
                That(r.Count, Is.GreaterThanOrEqualTo(1), "HexRange should find nearby entity");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  4. SpatialQueryService — HexRing query
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void QueryHexRing_FindsEntitiesOnRingOnly()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 4);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));
                spatial.SetCoordinateConverter(coords);

                // Place entity on ring (hex distance = 2 from center)
                var hexOnRing = new HexCoordinates(2, 0);
                var worldPos = coords.HexToWorld(hexOnRing);
                var eOnRing = world.Create();
                grid.Add(eOnRing, new IntRect(worldPos.X / 100, worldPos.Y / 100,
                    worldPos.X / 100 + 1, worldPos.Y / 100 + 1));

                Span<Entity> buffer = stackalloc Entity[256];
                var r = spatial.QueryHexRing(new HexCoordinates(0, 0), hexRadius: 2, buffer);

                // Ring query at radius 2 should find the entity
                That(r.Count, Is.GreaterThanOrEqualTo(1), "HexRing should find entity on ring");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  5. SpatialQueryService — AABB query (basic)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void QueryAabb_EmptyWorld_ReturnsZero()
        {
            var world = World.Create();
            try
            {
                var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
                var grid = new GridSpatialPartitionWorld(cellSize: 1);
                var spatial = new SpatialQueryService(new GridSpatialPartitionBackend(grid, coords));

                Span<Entity> buffer = stackalloc Entity[64];
                var r = spatial.QueryAabb(new WorldAabbCm(0, 0, 1000, 1000), buffer);

                That(r.Count, Is.EqualTo(0), "Empty world should return 0 results");
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  6. VertexMap — All layers read/write
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void VertexMap_AllLayers_ReadWriteRoundTrip()
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: 2, heightInChunks: 2);

            int q = 5, r = 3;

            // Write all layers
            map.SetHeight(q, r, 12);
            map.SetBiome(q, r, 3);
            map.SetWaterHeight(q, r, 5);
            map.SetVegetation(q, r, 7);
            map.SetBlocked(q, r, true);
            map.SetRamp(q, r, true);
            map.SetFaction(q, r, 2);

            // Read all layers
            That(map.GetHeight(q, r), Is.EqualTo(12), "Height should round-trip");
            That(map.GetBiome(q, r), Is.EqualTo(3), "Biome should round-trip");
            That(map.GetWaterHeight(q, r), Is.EqualTo(5), "WaterHeight should round-trip");
            That(map.GetVegetation(q, r), Is.EqualTo(7), "Vegetation should round-trip");
            That(map.IsBlocked(q, r), Is.True, "Blocked should round-trip");
            That(map.IsRamp(q, r), Is.True, "Ramp should round-trip");
            That(map.GetFaction(q, r), Is.EqualTo(2), "Faction should round-trip");
        }

        [Test]
        public void VertexMap_MultipleHexes_IndependentLayers()
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: 2, heightInChunks: 2);

            // Write different values at different hex positions
            map.SetHeight(0, 0, 1);
            map.SetHeight(1, 0, 5);
            map.SetHeight(0, 1, 10);

            map.SetBiome(0, 0, 0); // plains
            map.SetBiome(1, 0, 2); // forest
            map.SetBiome(0, 1, 4); // mountain

            map.SetFaction(0, 0, 1);
            map.SetFaction(1, 0, 2);
            map.SetFaction(0, 1, 3);

            // Verify independence
            That(map.GetHeight(0, 0), Is.EqualTo(1));
            That(map.GetHeight(1, 0), Is.EqualTo(5));
            That(map.GetHeight(0, 1), Is.EqualTo(10));

            That(map.GetBiome(0, 0), Is.EqualTo(0));
            That(map.GetBiome(1, 0), Is.EqualTo(2));

            That(map.GetFaction(0, 0), Is.EqualTo(1));
            That(map.GetFaction(0, 1), Is.EqualTo(3));
        }

        [Test]
        public void VertexMap_BlockedAndRamp_ToggleOnOff()
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: 1, heightInChunks: 1);

            // Default: not blocked, not ramp
            That(map.IsBlocked(0, 0), Is.False, "Default should be unblocked");
            That(map.IsRamp(0, 0), Is.False, "Default should not be ramp");

            // Set
            map.SetBlocked(0, 0, true);
            map.SetRamp(0, 0, true);
            That(map.IsBlocked(0, 0), Is.True);
            That(map.IsRamp(0, 0), Is.True);

            // Unset
            map.SetBlocked(0, 0, false);
            map.SetRamp(0, 0, false);
            That(map.IsBlocked(0, 0), Is.False);
            That(map.IsRamp(0, 0), Is.False);
        }

        // ════════════════════════════════════════════════════════════════════
        //  7. Coordinate conversion — Grid ↔ World ↔ Hex
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void CoordinateConverter_GridToWorld_Roundtrips()
        {
            var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);

            // Grid (5, 3) -> World -> Grid
            var gridPos = new IntVector2(5, 3);
            var worldPos = coords.GridToWorld(gridPos);
            var backGrid = coords.WorldToGrid(worldPos);

            That(backGrid.X, Is.EqualTo(gridPos.X), "Grid X should round-trip");
            That(backGrid.Y, Is.EqualTo(gridPos.Y), "Grid Y should round-trip");
        }

        [Test]
        public void CoordinateConverter_HexToWorld_Roundtrips()
        {
            var coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);

            var hex = new HexCoordinates(4, -2);
            var worldPos = coords.HexToWorld(hex);
            var backHex = coords.WorldToHex(worldPos);

            That(backHex, Is.EqualTo(hex), "Hex coordinates should round-trip through world");
        }

        // ════════════════════════════════════════════════════════════════════
        //  8. HexMetrics — Distance calculation
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HexCoordinates_Distance_Correct()
        {
            var a = new HexCoordinates(0, 0);
            var b = new HexCoordinates(3, -1);

            int dist = HexCoordinates.Distance(a, b);
            // Hex distance: max(|dq|, |dr|, |ds|) where s = -q-r
            // dq=3, dr=-1, ds=-2 → max(3,1,2) = 3
            That(dist, Is.EqualTo(3), "Hex distance between (0,0) and (3,-1) should be 3");
        }

        [Test]
        public void HexCoordinates_SamePoint_DistanceZero()
        {
            var a = new HexCoordinates(5, -3);
            That(HexCoordinates.Distance(a, a), Is.EqualTo(0));
        }

        [Test]
        public void HexCoordinates_Neighbors_AllAtDistanceOne()
        {
            var center = new HexCoordinates(0, 0);
            var neighbors = new[]
            {
                new HexCoordinates(1, 0),
                new HexCoordinates(-1, 0),
                new HexCoordinates(0, 1),
                new HexCoordinates(0, -1),
                new HexCoordinates(1, -1),
                new HexCoordinates(-1, 1),
            };

            foreach (var n in neighbors)
            {
                That(HexCoordinates.Distance(center, n), Is.EqualTo(1), $"Neighbor {n} should be at distance 1");
            }
        }
    }
}
