using Ludots.Core.Fields;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public class InfluenceFieldTests
    {
        private static FieldGridSpec2D Grid => new FieldGridSpec2D(cellSizeCm: 50, chunkSizeCells: 8);

        [Test]
        public void Stamp_ConstantFalloff_UniformWithinRadius()
        {
            var field = new InfluenceField("test", Grid);
            var center = new WorldCmInt2(0, 0);
            field.Stamp(center, radiusCm: 100, peak: 5f, FalloffKind.Constant);

            Assert.That(field.Sample(center), Is.EqualTo(5f).Within(0.01f));
            // Point within radius still full value
            Assert.That(field.Sample(new WorldCmInt2(50, 0)), Is.EqualTo(5f).Within(0.01f));
            // Point outside radius is zero
            Assert.That(field.Sample(new WorldCmInt2(300, 0)), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Stamp_LinearFalloff_DecreasesWithDistance()
        {
            var field = new InfluenceField("test", Grid);
            var center = new WorldCmInt2(0, 0);
            field.Stamp(center, radiusCm: 200, peak: 10f, FalloffKind.Linear);

            float atCenter = field.Sample(center);
            float atMid = field.Sample(new WorldCmInt2(100, 0));
            float atEdge = field.Sample(new WorldCmInt2(190, 0));

            Assert.That(atCenter, Is.GreaterThan(atMid));
            Assert.That(atMid, Is.GreaterThan(atEdge));
        }

        [Test]
        public void Stamp_Additive_AccumulatesOverlappingSources()
        {
            var field = new InfluenceField("test", Grid);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 3f, FalloffKind.Constant);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 4f, FalloffKind.Constant);

            Assert.That(field.Sample(new WorldCmInt2(0, 0)), Is.EqualTo(7f).Within(0.01f));
        }

        [Test]
        public void Decay_ReducesAllValues()
        {
            var field = new InfluenceField("test", Grid);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 10f, FalloffKind.Constant);
            float before = field.Sample(new WorldCmInt2(0, 0));

            field.Decay(0.5f);
            float after = field.Sample(new WorldCmInt2(0, 0));

            Assert.That(after, Is.EqualTo(before * 0.5f).Within(0.1f));
        }

        [Test]
        public void Registry_RejectsIncompatibleGridSpec()
        {
            var registry = new InfluenceFieldRegistry();
            registry.GetOrCreate("threat", new FieldGridSpec2D(50, 8));

            Assert.Throws<System.InvalidOperationException>(() =>
                registry.GetOrCreate("threat", new FieldGridSpec2D(100, 8)));
        }

        [Test]
        public void Registry_ReusesSameFieldForSameKey()
        {
            var registry = new InfluenceFieldRegistry();
            var a = registry.GetOrCreate("threat", Grid);
            var b = registry.GetOrCreate("threat", Grid);
            Assert.That(a, Is.SameAs(b));
        }
    }
}
