using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Generators;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public class EqsGeneratorTests
    {
        private static readonly WorldCmInt2 Origin = new WorldCmInt2(1000, 1000);

        [Test]
        public void GridGenerator_ProducesCenteredSquareGrid()
        {
            var gen = new GridGenerator(extentCm: 100, cellSizeCm: 50);
            Span<EqsItem> buf = stackalloc EqsItem[64];
            int count = gen.Generate(Origin, buf);

            // steps = 100/50 = 2 => (2*2+1)^2 = 25
            Assert.That(count, Is.EqualTo(25));
            // Center candidate exists
            bool hasCenter = false;
            for (int i = 0; i < count; i++)
            {
                if (buf[i].Position == Origin) hasCenter = true;
            }
            Assert.That(hasCenter, Is.True, "Grid should include origin cell");
        }

        [Test]
        public void RingGenerator_ProducesPointsAtRadius()
        {
            var gen = new RingGenerator(radiusCm: 300, count: 8);
            Span<EqsItem> buf = stackalloc EqsItem[8];
            int count = gen.Generate(Origin, buf);

            Assert.That(count, Is.EqualTo(8));
            for (int i = 0; i < count; i++)
            {
                long dx = buf[i].Position.X - Origin.X;
                long dy = buf[i].Position.Y - Origin.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                Assert.That(dist, Is.EqualTo(300).Within(2), $"Point {i} should be ~300cm from origin");
            }
        }

        [Test]
        public void DonutGenerator_ExcludesInnerRadius()
        {
            var gen = new DonutGenerator(innerCm: 100, outerCm: 200, cellSizeCm: 50);
            Span<EqsItem> buf = stackalloc EqsItem[128];
            int count = gen.Generate(Origin, buf);

            Assert.That(count, Is.GreaterThan(0));
            long innerSq = 100L * 100L;
            long outerSq = 200L * 200L;
            for (int i = 0; i < count; i++)
            {
                long dx = buf[i].Position.X - Origin.X;
                long dy = buf[i].Position.Y - Origin.Y;
                long distSq = dx * dx + dy * dy;
                Assert.That(distSq, Is.InRange(innerSq, outerSq),
                    $"Point {i} distance² {distSq} should be within annulus [{innerSq}, {outerSq}]");
            }
        }

        [Test]
        public void CircleGenerator_AllPointsWithinRadius()
        {
            var gen = new CircleGenerator(radiusCm: 150, cellSizeCm: 50);
            Span<EqsItem> buf = stackalloc EqsItem[64];
            int count = gen.Generate(Origin, buf);

            Assert.That(count, Is.GreaterThan(0));
            long radiusSq = 150L * 150L;
            for (int i = 0; i < count; i++)
            {
                long dx = buf[i].Position.X - Origin.X;
                long dy = buf[i].Position.Y - Origin.Y;
                Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(radiusSq));
            }
        }

        [Test]
        public void Generator_RespectsBufferCapacity()
        {
            var gen = new GridGenerator(extentCm: 1000, cellSizeCm: 10); // would be huge
            Span<EqsItem> buf = stackalloc EqsItem[16];
            int count = gen.Generate(Origin, buf);
            Assert.That(count, Is.LessThanOrEqualTo(16), "Generator must not overflow buffer");
        }
    }
}
