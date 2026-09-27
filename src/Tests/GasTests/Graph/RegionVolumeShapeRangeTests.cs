using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Region volumes judge containment and swept crossings with squared lengths.
    /// Q31.32 multiplication wraps past about 46340 centimeters, so a point a
    /// kilometer away can compare as inside a city-sized ring. These cases sit
    /// past that threshold and on the inclusive boundary.
    /// </summary>
    [TestFixture]
    public sealed class RegionVolumeShapeRangeTests
    {
        [Test]
        public void Circle_PointPastFixedSquareRange_StaysOutside()
        {
            RegionVolumeShape ring = Circle(2400);
            Fix64Vec2 origin = At(0, 0);
            Assert.That(ring.Contains(At(140_000, 0), origin), Is.False,
                "1.4 km from a 24 m ring is outside; a wrapped square used to say inside.");
            Assert.That(ring.Contains(At(-140_000, 0), origin), Is.False);
            Assert.That(ring.Contains(At(0, 140_000), origin), Is.False);
            Assert.That(ring.Contains(At(99_000, 99_000), origin), Is.False);

            Fix64Vec2 anchor = At(8_000_000, -3_500_000);
            for (int distance = 46_341; distance <= 200_000; distance += 97)
            {
                Assert.That(ring.Contains(anchor + At(distance, 0), anchor), Is.False, $"east {distance}");
                Assert.That(ring.Contains(anchor + At(0, -distance), anchor), Is.False, $"south {distance}");
                Assert.That(
                    ring.IntersectsPath(anchor + At(distance, 3_000), anchor + At(distance + 80, 3_000), anchor),
                    Is.False,
                    $"a short step {distance} cm out must not count as crossing the ring");
            }
        }

        [Test]
        public void Circle_BoundaryAndLargeRadius_StayInclusive()
        {
            RegionVolumeShape ring = Circle(2400);
            Fix64Vec2 origin = At(0, 0);
            Assert.That(ring.Contains(At(2400, 0), origin), Is.True);
            Assert.That(ring.Contains(At(2401, 0), origin), Is.False);
            Assert.That(ring.Contains(At(1697, 1697), origin), Is.True);
            Assert.That(ring.Contains(At(1698, 1698), origin), Is.False);
            Assert.That(ring.IntersectsPath(At(2400, 10_000), At(2400, 0), origin), Is.True,
                "A step that ends on the radius still crosses.");
            Assert.That(ring.IntersectsPath(At(2401, 10_000), At(2401, 0), origin), Is.False);

            RegionVolumeShape wide = Circle(100_000);
            Assert.That(wide.Contains(origin, origin), Is.True);
            Assert.That(wide.Contains(At(100_000, 0), origin), Is.True);
            Assert.That(wide.Contains(At(100_001, 0), origin), Is.False);
            Assert.That(wide.Contains(At(140_000, 0), origin), Is.False);
        }

        [Test]
        public void Circle_LongSweep_HitsOnlyWhenTheSegmentReachesTheRing()
        {
            RegionVolumeShape ring = Circle(2400);
            Fix64Vec2 origin = At(0, 0);
            Assert.That(ring.IntersectsPath(At(-1_000_000, 0), At(1_000_000, 0), origin), Is.True);
            Assert.That(ring.IntersectsPath(At(-1_000_000, 2_000), At(1_000_000, 2_000), origin), Is.True);
            Assert.That(ring.IntersectsPath(At(-1_000_000, 10_000), At(1_000_000, 10_000), origin), Is.False);
        }

        [Test]
        public void Segment_LongerThanFixedSquareRange_UsesTrueDistance()
        {
            var wall = new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Segment,
                SegmentA = At(0, 0),
                SegmentB = At(1_000_000, 0),
                HalfThickness = Fix64.FromInt(100),
            };
            Fix64Vec2 origin = At(0, 0);

            Assert.That(wall.Contains(At(500_000, 0), origin), Is.True);
            Assert.That(wall.Contains(At(500_000, 100), origin), Is.True);
            Assert.That(wall.Contains(At(500_000, 101), origin), Is.False);
            Assert.That(wall.Contains(At(500_000, 140_000), origin), Is.False);
            Assert.That(wall.IntersectsPath(At(500_000, -200_000), At(500_000, 200_000), origin), Is.True);
            Assert.That(wall.IntersectsPath(At(140_000, -200), At(140_080, -200), origin), Is.False);
        }

        [Test]
        public void PolygonAndRect_FarFromAnchor_StayOutside()
        {
            var polygon = new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Polygon,
                PolygonPoints = new[]
                {
                    At(0, 0), At(10_000, 0), At(10_000, 10_000), At(0, 10_000),
                },
            };
            Fix64Vec2 origin = At(0, 0);
            Assert.That(polygon.Contains(At(5_000, 5_000), origin), Is.True);
            Assert.That(polygon.Contains(At(5_000, 0), origin), Is.True, "A point on an edge counts as inside.");
            Assert.That(polygon.Contains(At(429_630, 5_000), origin), Is.False);
            Assert.That(polygon.Contains(At(-10, 5_000), origin), Is.False);

            var rect = new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Rect,
                HalfWidth = Fix64.FromInt(40),
                HalfHeight = Fix64.FromInt(40),
            };
            Assert.That(rect.Contains(At(40, 40), origin), Is.True);
            Assert.That(rect.Contains(At(41, 0), origin), Is.False);
            Assert.That(rect.Contains(At(140_000, 0), origin), Is.False);
            Assert.That(rect.IntersectsPath(At(0, -1_000_000), At(0, 1_000_000), origin), Is.True);
            Assert.That(rect.IntersectsPath(At(-1_000_000, 40), At(1_000_000, 40), origin), Is.True,
                "A long sweep along the top edge still touches it.");
            Assert.That(rect.IntersectsPath(At(-1_000_000, 5_000), At(1_000_000, 5_000), origin), Is.False);
            Assert.That(rect.IntersectsPath(At(140_000, 100), At(140_080, 100), origin), Is.False);
        }

        private static RegionVolumeShape Circle(int radiusCm)
        {
            return new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Circle,
                Radius = Fix64.FromInt(radiusCm),
            };
        }

        private static Fix64Vec2 At(int x, int y) => Fix64Vec2.FromInt(x, y);
    }
}
