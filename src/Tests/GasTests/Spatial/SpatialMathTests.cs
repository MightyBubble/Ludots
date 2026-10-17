using NUnit.Framework;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace GasTests
{
    [TestFixture]
    public class SpatialMathTests
    {
        [Test]
        public void FloorDiv_PositiveValues_ReturnsCorrect()
        {
            Assert.That(MathUtil.FloorDiv(7, 3), Is.EqualTo(2));
            Assert.That(MathUtil.FloorDiv(10, 5), Is.EqualTo(2));
            Assert.That(MathUtil.FloorDiv(100, 100), Is.EqualTo(1));
        }

        [Test]
        public void FloorDiv_NegativeNumerator_RoundsDown()
        {
            Assert.That(MathUtil.FloorDiv(-7, 3), Is.EqualTo(-3));
            Assert.That(MathUtil.FloorDiv(-1, 100), Is.EqualTo(-1));
        }

        [Test]
        public void FloorDiv_NegativeDivisor_RoundsDown()
        {
            Assert.That(MathUtil.FloorDiv(7, -3), Is.EqualTo(-3));
        }

        [Test]
        public void FloorDiv_ExactDivision_NoRounding()
        {
            Assert.That(MathUtil.FloorDiv(9, 3), Is.EqualTo(3));
            Assert.That(MathUtil.FloorDiv(-9, 3), Is.EqualTo(-3));
            Assert.That(MathUtil.FloorDiv(0, 5), Is.EqualTo(0));
        }

        [Test]
        public void FloorDiv_ZeroNumerator_ReturnsZero()
        {
            Assert.That(MathUtil.FloorDiv(0, 1), Is.EqualTo(0));
            Assert.That(MathUtil.FloorDiv(0, 100), Is.EqualTo(0));
            Assert.That(MathUtil.FloorDiv(0, -5), Is.EqualTo(0));
        }

        [Test]
        public void CeilDiv_PositiveValues_ReturnsCorrect()
        {
            Assert.That(MathUtil.CeilDiv(7, 3), Is.EqualTo(3));
            Assert.That(MathUtil.CeilDiv(1, 100), Is.EqualTo(1));
        }

        [Test]
        public void CeilDiv_NegativeNumerator_RoundsUp()
        {
            Assert.That(MathUtil.CeilDiv(-7, 3), Is.EqualTo(-2));
        }

        [Test]
        public void CeilDiv_ExactDivision_NoRounding()
        {
            Assert.That(MathUtil.CeilDiv(9, 3), Is.EqualTo(3));
            Assert.That(MathUtil.CeilDiv(-9, 3), Is.EqualTo(-3));
            Assert.That(MathUtil.CeilDiv(0, 5), Is.EqualTo(0));
        }

        [Test]
        public void WorldAabbToCellRect_StandardCase()
        {
            var aabb = new WorldAabbCm(0, 0, 200, 200);
            var rect = MathUtil.WorldAabbToCellRect(in aabb, 100);
            Assert.That(rect.X, Is.EqualTo(0));
            Assert.That(rect.Y, Is.EqualTo(0));
            Assert.That(rect.Width, Is.EqualTo(2));
            Assert.That(rect.Height, Is.EqualTo(2));
        }

        [Test]
        public void WorldAabbToCellRect_NegativeOrigin()
        {
            var aabb = new WorldAabbCm(-200, -200, 100, 100);
            var rect = MathUtil.WorldAabbToCellRect(in aabb, 100);
            Assert.That(rect.X, Is.EqualTo(-2));
            Assert.That(rect.Y, Is.EqualTo(-2));
            Assert.That(rect.Width, Is.EqualTo(1));
            Assert.That(rect.Height, Is.EqualTo(1));
        }

        [Test]
        public void WorldAabbToCellRect_CrossesOrigin()
        {
            // min = floor(-50/100)=-1, max = ceil(50/100)=1 → width=2
            var aabb = new WorldAabbCm(-50, -50, 100, 100);
            var rect = MathUtil.WorldAabbToCellRect(in aabb, 100);
            Assert.That(rect.X, Is.EqualTo(-1));
            Assert.That(rect.Y, Is.EqualTo(-1));
            Assert.That(rect.Width, Is.EqualTo(2));
            Assert.That(rect.Height, Is.EqualTo(2));
        }
    }
}
