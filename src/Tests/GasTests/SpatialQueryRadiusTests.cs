using System;
using NUnit.Framework;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;

namespace GasTests
{
    [TestFixture]
    public class SpatialQueryRadiusTests
    {
        private World _world;
        private ChunkedGridSpatialPartitionWorld _partition;
        private SpatialQueryService _service;
        private WorldSizeSpec _spec;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            int cellSize = 100; // 1m cells
            _spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), gridCellSizeCm: cellSize);
            _partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            _service = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(_partition, _spec));
            _service.SetPositionProvider(e =>
            {
                ref var pos = ref _world.Get<WorldPositionCm>(e);
                return pos.Value.ToWorldCmInt2();
            });
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        private Entity CreateEntityAt(int xCm, int yCm)
        {
            var entity = _world.Create(new WorldPositionCm { Value = new Fix64Vec2(xCm, yCm) });
            int cx = MathUtil.FloorDiv(xCm, _spec.GridCellSizeCm);
            int cy = MathUtil.FloorDiv(yCm, _spec.GridCellSizeCm);
            _partition.Add(entity, cx, cy);
            return entity;
        }

        [Test]
        public void QueryRadius_ReturnsOnlyEntitiesWithinCircle()
        {
            // Center at origin, radius 500cm (5m)
            var center = CreateEntityAt(0, 0);
            var inside = CreateEntityAt(300, 400); // dist = 500, at boundary
            var outside = CreateEntityAt(400, 400); // dist = 565, outside

            Span<Entity> buffer = stackalloc Entity[32];
            var result = _service.QueryRadius(new WorldCmInt2(0, 0), 500, buffer);

            // Should include center and inside, exclude outside
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void QueryRadius_ExcludesCornerEntities_OutsideCircle()
        {
            // Entity at corner of bounding square but outside circle
            // radius=100, entity at (90, 90) → dist = sqrt(16200) ≈ 127 > 100
            CreateEntityAt(90, 90);
            CreateEntityAt(0, 0); // At center

            Span<Entity> buffer = stackalloc Entity[32];
            var result = _service.QueryRadius(new WorldCmInt2(0, 0), 100, buffer);

            Assert.That(result.Count, Is.EqualTo(1)); // Only center
        }

        [Test]
        public void QueryRadius_AtExactBoundary_IsIncluded()
        {
            // Entity at exactly the radius distance: (300, 400) with radius=500
            CreateEntityAt(300, 400); // dist = sqrt(90000+160000) = 500

            Span<Entity> buffer = stackalloc Entity[32];
            var result = _service.QueryRadius(new WorldCmInt2(0, 0), 500, buffer);

            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void QueryRadius_ZeroRadius_ReturnsOnlyCenterEntity()
        {
            CreateEntityAt(0, 0);
            CreateEntityAt(1, 0); // 1cm away

            Span<Entity> buffer = stackalloc Entity[32];
            var result = _service.QueryRadius(new WorldCmInt2(0, 0), 0, buffer);

            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void QueryRadius_LargeRadius_AllInRange()
        {
            CreateEntityAt(100, 100);
            CreateEntityAt(-100, -100);
            CreateEntityAt(200, 0);

            Span<Entity> buffer = stackalloc Entity[32];
            var result = _service.QueryRadius(new WorldCmInt2(0, 0), 3000, buffer);

            Assert.That(result.Count, Is.EqualTo(3));
        }
    }
}
