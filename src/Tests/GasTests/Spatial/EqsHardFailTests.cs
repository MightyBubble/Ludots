using System;
using Arch.Core;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial.Eqs;
using Ludots.Core.Spatial.Eqs.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public class EqsHardFailTests
    {
        [Test]
        public void InfluenceTest_MissingRegistry_Throws()
        {
            using World world = World.Create();
            var test = new InfluenceTest("threat", preferLow: true);
            var item = new EqsItem(new WorldCmInt2(0, 0));
            var ctx = new EqsContext(new WorldCmInt2(0, 0), world, influenceFields: null);

            Assert.Throws<InvalidOperationException>(() => test.Score(in ctx, ref item));
        }

        [Test]
        public void InfluenceTest_UnregisteredField_Throws()
        {
            using World world = World.Create();
            var registry = new InfluenceFieldRegistry();
            registry.GetOrCreate("other", new FieldGridSpec2D(50, 8));
            var test = new InfluenceTest("threat", preferLow: true);
            var item = new EqsItem(new WorldCmInt2(0, 0));
            var ctx = new EqsContext(new WorldCmInt2(0, 0), world, influenceFields: registry);

            Assert.Throws<InvalidOperationException>(() => test.Score(in ctx, ref item));
        }

        [Test]
        public void OverlapTest_MissingSpatialQueries_Throws()
        {
            using World world = World.Create();
            var test = new OverlapTest(OverlapShape.Radius, extentCm: 100, preferMore: true);
            var item = new EqsItem(new WorldCmInt2(0, 0));
            var ctx = new EqsContext(new WorldCmInt2(0, 0), world, spatialQueries: null);

            Assert.Throws<InvalidOperationException>(() => test.Score(in ctx, ref item));
        }

        [Test]
        public void OverlapTest_UnknownShape_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new OverlapTest((OverlapShape)255, extentCm: 100, preferMore: true));
        }

        [Test]
        public void InfluenceTest_NonPositiveNormalizeScale_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new InfluenceTest("threat", preferLow: true, normalizeScale: 0f));
        }
    }
}
