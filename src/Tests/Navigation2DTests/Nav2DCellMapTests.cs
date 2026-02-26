using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public class Nav2DCellMapTests
    {
        [Test]
        public void CollectNeighbors_SparseAgents_ReturnsAll()
        {
            var cellSize = Fix64.FromInt(100);
            using var map = new Nav2DCellMap(cellSize, 16, 16);

            // 4 agents spread across different cells
            var positions = new Fix64Vec2[]
            {
                Fix64Vec2.FromInt(50, 50),    // agent 0 — center
                Fix64Vec2.FromInt(150, 50),   // agent 1
                Fix64Vec2.FromInt(50, 150),   // agent 2
                Fix64Vec2.FromInt(-50, 50),   // agent 3
            };

            map.Build(positions);

            Span<int> neighbors = stackalloc int[16];
            int count = map.CollectNeighbors(
                selfIndex: 0,
                selfPosCm: positions[0],
                radiusCm: Fix64.FromInt(200),
                positionsCm: positions,
                neighborsOut: neighbors);

            Assert.That(count, Is.EqualTo(3), "Should find all 3 other agents in sparse scenario");
        }

        [Test]
        public void CollectNeighbors_ExcludesSelf()
        {
            var cellSize = Fix64.FromInt(100);
            using var map = new Nav2DCellMap(cellSize, 8, 8);

            var positions = new Fix64Vec2[]
            {
                Fix64Vec2.FromInt(50, 50),
                Fix64Vec2.FromInt(55, 55),
            };

            map.Build(positions);

            Span<int> neighbors = stackalloc int[16];
            int count = map.CollectNeighbors(
                selfIndex: 0,
                selfPosCm: positions[0],
                radiusCm: Fix64.FromInt(200),
                positionsCm: positions,
                neighborsOut: neighbors);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(neighbors[0], Is.EqualTo(1), "Should return agent 1, not self");
        }

        [Test]
        public void CollectNeighbors_EmptyGrid_ReturnsZero()
        {
            var cellSize = Fix64.FromInt(100);
            using var map = new Nav2DCellMap(cellSize, 8, 8);

            var positions = new Fix64Vec2[]
            {
                Fix64Vec2.FromInt(50, 50),
            };

            map.Build(positions);

            Span<int> neighbors = stackalloc int[16];
            int count = map.CollectNeighbors(
                selfIndex: 0,
                selfPosCm: positions[0],
                radiusCm: Fix64.FromInt(500),
                positionsCm: positions,
                neighborsOut: neighbors);

            Assert.That(count, Is.EqualTo(0), "Only self exists, should return 0 neighbors");
        }
    }
}
