using System;
using System.Numerics;
using Ludots.Core.Map.Hex;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Terrain
{
    [TestFixture]
    public class VertexMapInterpolationTests
    {
        [Test]
        public void GetLogicHeight_InterpolatesWithinTriangle()
        {
            var map = new VertexMap();
            map.Initialize(widthInChunks: 4, heightInChunks: 4);

            map.SetHeight(0, 0, 0);
            map.SetHeight(1, 0, 10);
            map.SetHeight(0, 1, 20);

            Vector3 p0 = AxialToWorld(q: 0f, r: 0f);
            Vector3 p1 = AxialToWorld(q: 1f, r: 0f);
            Vector3 p2 = AxialToWorld(q: 0f, r: 1f);
            Vector3 worldPos = (p0 + p1 + p2) / 3f;
            float h = map.GetLogicHeight(worldPos);
            float expected = (0f + 10f + 15f) / 3f;

            That(h, Is.EqualTo(expected).Within(1e-4f));
        }

        private static Vector3 AxialToWorld(float q, float r)
        {
            float x = HexCoordinates.EdgeLength * 1.7320508f * (q + r / 2.0f);
            float z = HexCoordinates.EdgeLength * 1.5f * r;
            return new Vector3(x, 0, z);
        }
    }
}
