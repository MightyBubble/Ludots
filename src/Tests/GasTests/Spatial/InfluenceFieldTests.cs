using System;
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
            Assert.That(field.Sample(new WorldCmInt2(50, 0)), Is.EqualTo(5f).Within(0.01f));
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
        public void Stamp_UnknownFalloff_Throws()
        {
            var field = new InfluenceField("test", Grid);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 1f, (FalloffKind)255));
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
        public void Decay_MoreThan256NonDefaultCells_ScalesEntireFieldAndTerminates()
        {
            var field = new InfluenceField("wide", Grid);
            // cellSize=50, radius=1000 → cellRadius≈20 → >256 non-default cells
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 1000, peak: 8f, FalloffKind.Constant);
            int beforeCount = field.CellCount;
            Assert.That(beforeCount, Is.GreaterThan(256));

            field.Decay(0.5f);

            Assert.That(field.CellCount, Is.EqualTo(beforeCount));
            Assert.That(field.Sample(new WorldCmInt2(0, 0)), Is.EqualTo(4f).Within(0.01f));
            Assert.That(field.Sample(new WorldCmInt2(500, 0)), Is.EqualTo(4f).Within(0.01f));
            Assert.That(field.Sample(new WorldCmInt2(900, 0)), Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Decay_FactorZero_ClearsField()
        {
            var field = new InfluenceField("test", Grid);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 10f, FalloffKind.Constant);
            field.Decay(0f);
            Assert.That(field.CellCount, Is.EqualTo(0));
            Assert.That(field.Sample(new WorldCmInt2(0, 0)), Is.EqualTo(0f));
        }

        [Test]
        public void Decay_InvalidFactor_Throws()
        {
            var field = new InfluenceField("test", Grid);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 100, peak: 10f, FalloffKind.Constant);
            Assert.Throws<ArgumentOutOfRangeException>(() => field.Decay(-0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.Decay(1.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.Decay(float.NaN));
        }

        [Test]
        public void Decay_WarmPath_AllocatesZeroAfterWarmup()
        {
            var field = new InfluenceField("alloc", Grid);
            field.Stamp(new WorldCmInt2(0, 0), radiusCm: 1000, peak: 8f, FalloffKind.Constant);
            Assert.That(field.CellCount, Is.GreaterThan(256));
            field.Decay(0.99f);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocated = MeasureDecayAllocations(field);
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void Registry_RejectsIncompatibleGridSpec()
        {
            var registry = new InfluenceFieldRegistry();
            registry.GetOrCreate("threat", new FieldGridSpec2D(50, 8));

            Assert.Throws<InvalidOperationException>(() =>
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

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureDecayAllocations(InfluenceField field)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                field.Decay(0.999f);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
