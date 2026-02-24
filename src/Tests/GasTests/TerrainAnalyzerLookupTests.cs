using Ludots.Core.Navigation.Analysis;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Terrain
{
    [TestFixture]
    public class TerrainAnalyzerLookupTests
    {
        private sealed class TestLookup : ITerrainTypeLookup
        {
            private readonly TerrainTypeProperties[] _props = new TerrainTypeProperties[256];

            public TestLookup()
            {
                for (int i = 0; i < _props.Length; i++)
                {
                    _props[i] = new TerrainTypeProperties(isBlocked: false, isWater: false);
                }
            }

            public void Set(byte id, TerrainTypeProperties props) => _props[id] = props;

            public TerrainTypeProperties Get(byte terrainType) => _props[terrainType];
        }

        [Test]
        public void AnalyzeTriangle_UsesLookupForBlocked()
        {
            var lookup = new TestLookup();
            lookup.Set(2, new TerrainTypeProperties(isBlocked: true, isWater: false));

            var result = TerrainAnalyzer.AnalyzeTriangle(0, 2, 0, 0, 0, 0, lookup);
            That(result, Is.EqualTo(TerrainWalkability.Blocked));
        }

        [Test]
        public void AnalyzeTriangle_UsesLookupForWater()
        {
            var lookup = new TestLookup();
            lookup.Set(1, new TerrainTypeProperties(isBlocked: false, isWater: true));

            var result = TerrainAnalyzer.AnalyzeTriangle(0, 1, 0, 0, 0, 0, lookup);
            That(result, Is.EqualTo(TerrainWalkability.Water));
        }

        [Test]
        public void AnalyzeTriangle_FallsBackToSlopeRulesWhenLookupAllows()
        {
            var lookup = new TestLookup();

            var result = TerrainAnalyzer.AnalyzeTriangle(0, 0, 0, 0, 2, 0, lookup);
            That(result, Is.EqualTo(TerrainWalkability.Cliff));
        }
    }
}

