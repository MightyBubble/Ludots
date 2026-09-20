using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [TestFixture]
    public sealed class GroundOverlayGeometryTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void WriteCircleFill_ValidItem_ProducesUpwardFanAtGroundLift()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Circle,
                Center = new Vector3(2f, 0.5f, -3f),
                Radius = 4f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.CircleFillVertices];

            int written = GroundOverlayGeometry.WriteCircleFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.CircleFillVertices));
            Vector3 apex = new(item.Center.X, item.Center.Y + GroundOverlayGeometry.GroundLiftMeters, item.Center.Z);
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
                Assert.That(vertices[t], Is.EqualTo(apex).Using(Vec3Eq), "fan apex expected at lifted center");
                for (int v = 1; v < 3; v++)
                {
                    Vector3 offset = vertices[t + v] - item.Center;
                    Assert.That(offset.Y, Is.EqualTo(GroundOverlayGeometry.GroundLiftMeters).Within(Epsilon));
                    Assert.That(new Vector2(offset.X, offset.Z).Length(), Is.EqualTo(item.Radius).Within(Epsilon));
                }
            }
        }

        [Test]
        public void WriteCircleFill_NonPositiveRadius_WritesNothing()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Circle, Radius = 0f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.CircleFillVertices];

            Assert.That(GroundOverlayGeometry.WriteCircleFill(in item, vertices), Is.Zero);
        }

        [Test]
        public void WriteConeFill_ValidItem_ApexFanWithinAngleSpan()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Cone,
                Center = new Vector3(-1f, 0.2f, 4f),
                Radius = 5f,
                Angle = 0.4f,
                Rotation = 0.5f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.ConeFillVertices];

            int written = GroundOverlayGeometry.WriteConeFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.ConeFillVertices));
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
                Assert.That(vertices[t], Is.EqualTo(new Vector3(item.Center.X, item.Center.Y + GroundOverlayGeometry.GroundLiftMeters, item.Center.Z)).Using(Vec3Eq));
                for (int v = 1; v < 3; v++)
                {
                    Vector3 offset = vertices[t + v] - item.Center;
                    Assert.That(offset.Y, Is.EqualTo(GroundOverlayGeometry.GroundLiftMeters).Within(Epsilon));
                    float angle = MathF.Atan2(offset.Z, offset.X);
                    Assert.That(angle, Is.GreaterThanOrEqualTo(item.Rotation - item.Angle - Epsilon));
                    Assert.That(angle, Is.LessThanOrEqualTo(item.Rotation + item.Angle + Epsilon));
                }
            }
        }

        [Test]
        public void WriteConeFill_AngleBeyondHalfTurn_ClampsToFullCircle()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Cone, Radius = 2f, Angle = MathF.PI * 1.5f, Rotation = 0f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.ConeFillVertices];

            int written = GroundOverlayGeometry.WriteConeFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.ConeFillVertices));
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
            }
        }

        [Test]
        public void WriteRingFill_ValidItem_BandKeepsInnerHoleAndOuterEdge()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Ring,
                Center = new Vector3(3f, 0.1f, -2f),
                Radius = 6f,
                InnerRadius = 2f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.RingFillVertices];

            int written = GroundOverlayGeometry.WriteRingFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.RingFillVertices));
            float minRadius = float.MaxValue;
            float maxRadius = float.MinValue;
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
            }

            for (int v = 0; v < written; v++)
            {
                Vector3 offset = vertices[v] - item.Center;
                Assert.That(offset.Y, Is.EqualTo(GroundOverlayGeometry.GroundLiftMeters).Within(Epsilon));
                float radius = new Vector2(offset.X, offset.Z).Length();
                minRadius = MathF.Min(minRadius, radius);
                maxRadius = MathF.Max(maxRadius, radius);
            }

            Assert.That(minRadius, Is.EqualTo(item.InnerRadius).Within(Epsilon), "inner hole edge expected at InnerRadius");
            Assert.That(maxRadius, Is.EqualTo(item.Radius).Within(Epsilon), "outer edge expected at Radius");
        }

        [Test]
        public void WriteRingFill_InnerRadiusClampedToOuter_WritesNothing()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Ring, Radius = 3f, InnerRadius = 3f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.RingFillVertices];

            Assert.That(GroundOverlayGeometry.WriteRingFill(in item, vertices), Is.Zero);
        }

        [Test]
        public void WriteLineFill_ValidItem_QuadMatchesLengthWidthRotation()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(1f, 0.3f, 2f),
                Radius = 9f,
                Length = 7f,
                Width = 2f,
                Rotation = 0f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.LineFillVertices];

            int written = GroundOverlayGeometry.WriteLineFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.LineFillVertices));
            AssertTriFacesUp(vertices);
            for (int v = 0; v < written; v++)
            {
                Vector3 offset = vertices[v] - item.Center;
                Assert.That(offset.Y, Is.EqualTo(GroundOverlayGeometry.GroundLiftMeters).Within(Epsilon));
                Assert.That(MathF.Abs(offset.Z), Is.EqualTo(item.Width * 0.5f).Within(Epsilon), "lateral extent expected at half Width");
                Assert.That(offset.X, Is.GreaterThanOrEqualTo(-Epsilon));
                Assert.That(offset.X, Is.LessThanOrEqualTo(item.Length + Epsilon));
            }
        }

        [Test]
        public void WriteLineFill_ZeroLengthFallsBackToRadius()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Line,
                Center = Vector3.Zero,
                Radius = 5f,
                Length = 0f,
                Width = 2f,
                Rotation = 0f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.LineFillVertices];

            int written = GroundOverlayGeometry.WriteLineFill(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.LineFillVertices));
            Assert.That(vertices[1].X, Is.EqualTo(item.Radius).Within(Epsilon), "length falls back to Radius (producer zeroes Length for non-Line shapes)");
        }

        [Test]
        public void WriteLineFill_NonPositiveLengthAndRadius_WritesNothing()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Line, Length = 0f, Radius = 0f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.LineFillVertices];

            Assert.That(GroundOverlayGeometry.WriteLineFill(in item, vertices), Is.Zero);
        }

        [Test]
        public void WriteCircleBorder_ValidItem_BandCenteredOnRadius()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Circle,
                Center = Vector3.Zero,
                Radius = 4f,
                BorderWidth = 0.5f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.CircleFillVertices * 2];

            int written = GroundOverlayGeometry.WriteCircleBorder(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.CircleSegments * 3 * 2));
            for (int v = 0; v < written; v++)
            {
                Vector3 vertex = vertices[v];
                Assert.That(vertex.Y, Is.EqualTo(GroundOverlayGeometry.BorderLiftMeters).Within(Epsilon));
                float radius = new Vector2(vertex.X, vertex.Z).Length();
                Assert.That(radius, Is.GreaterThanOrEqualTo(item.Radius - (item.BorderWidth * 0.5f) - Epsilon));
                Assert.That(radius, Is.LessThanOrEqualTo(item.Radius + (item.BorderWidth * 0.5f) + Epsilon));
            }
        }

        [Test]
        public void WriteCircleBorder_HugeBorderWidth_ClampsInnerEdgeAtCenter()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Circle, Radius = 1f, BorderWidth = 8f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.CircleFillVertices * 2];

            int written = GroundOverlayGeometry.WriteCircleBorder(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.CircleSegments * 3 * 2));
            for (int v = 0; v < written; v++)
            {
                float radius = new Vector2(vertices[v].X, vertices[v].Z).Length();
                Assert.That(radius, Is.GreaterThanOrEqualTo(-Epsilon), "inner edge must not cross the center");
                Assert.That(radius, Is.LessThanOrEqualTo(item.Radius + (item.BorderWidth * 0.5f) + Epsilon));
            }
        }

        [Test]
        public void WriteRingBorder_ValidItem_TwoBandsClampedToHalfRingWidth()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Ring,
                Center = Vector3.Zero,
                Radius = 6f,
                InnerRadius = 4f,
                BorderWidth = 8f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.RingBorderVertices];

            int written = GroundOverlayGeometry.WriteRingBorder(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.RingBorderVertices));
            float halfExtent = MathF.Min(item.BorderWidth * 0.5f, (item.Radius - item.InnerRadius) * 0.5f);
            for (int v = 0; v < written; v++)
            {
                Vector3 vertex = vertices[v];
                Assert.That(vertex.Y, Is.EqualTo(GroundOverlayGeometry.BorderLiftMeters).Within(Epsilon));
                float radius = new Vector2(vertex.X, vertex.Z).Length();
                Assert.That(radius, Is.GreaterThanOrEqualTo(item.InnerRadius - halfExtent - Epsilon));
                Assert.That(radius, Is.LessThanOrEqualTo(item.Radius + halfExtent + Epsilon));
            }
        }

        [Test]
        public void WriteRingBorder_ZeroBorderWidth_WritesNothing()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Ring, Radius = 6f, InnerRadius = 4f, BorderWidth = 0f };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.RingBorderVertices];

            Assert.That(GroundOverlayGeometry.WriteRingBorder(in item, vertices), Is.Zero);
        }

        [Test]
        public void WriteConeBorder_ValidItem_ArcBandPlusTwoRadialEdges()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Cone,
                Center = Vector3.Zero,
                Radius = 5f,
                Angle = 0.4f,
                Rotation = 0.5f,
                BorderWidth = 0.2f,
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.ConeBorderVertices];

            int written = GroundOverlayGeometry.WriteConeBorder(in item, vertices);

            Assert.That(written, Is.EqualTo(GroundOverlayGeometry.ConeBorderVertices));
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
                for (int v = 0; v < 3; v++)
                {
                    Assert.That(vertices[t + v].Y, Is.EqualTo(GroundOverlayGeometry.BorderLiftMeters).Within(Epsilon));
                }
            }
        }

        [Test]
        public void WriteLineBorder_ValidItem_FourBandsAtBorderLift()
        {
            GroundOverlayItem item = new()
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(-2f, 0.4f, 1f),
                Length = 6f,
                Width = 2f,
                Rotation = 0.25f,
                BorderWidth = 0.3f,
            };
            Span<Vector3> vertices = stackalloc Vector3[4 * GroundOverlayGeometry.LineFillVertices];

            int written = GroundOverlayGeometry.WriteLineBorder(in item, vertices);

            Assert.That(written, Is.EqualTo(4 * GroundOverlayGeometry.LineFillVertices));
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
                for (int v = 0; v < 3; v++)
                {
                    Assert.That(vertices[t + v].Y, Is.EqualTo(item.Center.Y + GroundOverlayGeometry.BorderLiftMeters).Within(Epsilon));
                }
            }
        }

        [Test]
        public void WriteCircleFill_TooSmallSpan_Throws()
        {
            GroundOverlayItem item = new() { Shape = GroundOverlayShape.Circle, Radius = 1f };
            Span<Vector3> vertices = stackalloc Vector3[3];

            InvalidOperationException? error = null;
            try
            {
                GroundOverlayGeometry.WriteCircleFill(in item, vertices);
            }
            catch (InvalidOperationException ex)
            {
                error = ex;
            }

            Assert.That(error, Is.Not.Null, "insufficient span must fail loud, not truncate");
        }

        [Test]
        public void WriteRibbonStrip_StraightPath_TwoTrianglesPerSegmentAtLift()
        {
            Span<Vector3> path = stackalloc Vector3[3]
            {
                new(0f, 0f, 0f),
                new(4f, 0f, 0f),
                new(8f, 0f, 0f),
            };
            Span<Vector3> vertices = stackalloc Vector3[GroundOverlayGeometry.SplineRibbonStripVertices];

            int written = GroundOverlayGeometry.WriteRibbonStrip(path, 0f, 2f, GroundOverlayGeometry.GroundLiftMeters, vertices);

            Assert.That(written, Is.EqualTo(2 * 3 * 2), "two triangles per path segment");
            for (int t = 0; t < written; t += 3)
            {
                AssertTriFacesUp(vertices[t..(t + 3)]);
            }

            for (int v = 0; v < written; v++)
            {
                Assert.That(vertices[v].Y, Is.EqualTo(GroundOverlayGeometry.GroundLiftMeters).Within(Epsilon));
                Assert.That(MathF.Abs(vertices[v].Z), Is.EqualTo(1f).Within(Epsilon), "strip edges expected at half width");
            }
        }

        [Test]
        public void WriteRibbonStrip_OffsetPath_CentersStripAtLateralOffset()
        {
            Span<Vector3> path = stackalloc Vector3[2]
            {
                new(0f, 0f, 0f),
                new(4f, 0f, 0f),
            };
            Span<Vector3> vertices = stackalloc Vector3[6];

            int written = GroundOverlayGeometry.WriteRibbonStrip(path, 2f, 1f, 0f, vertices);

            Assert.That(written, Is.EqualTo(6));
            for (int v = 0; v < written; v++)
            {
                Assert.That(MathF.Abs(vertices[v].Z - 2f), Is.EqualTo(0.5f).Within(Epsilon), "offset strip edges expected at offset ± half width");
            }
        }

        [Test]
        public void WriteRibbonStrip_ShortPath_WritesNothing()
        {
            Span<Vector3> path = stackalloc Vector3[1] { new(0f, 0f, 0f) };
            Span<Vector3> vertices = stackalloc Vector3[6];

            Assert.That(GroundOverlayGeometry.WriteRibbonStrip(path, 0f, 1f, 0f, vertices), Is.Zero);
        }

        [Test]
        public void LiftConstants_MatchDecalReceiverDepthBias()
        {
            Assert.That(GroundOverlayGeometry.GroundLiftMeters, Is.EqualTo(0.04f), "ground overlays share the decal receiver depth bias (RaylibDecalProjectorRenderer.DecalReceiverDepthBiasMeters)");
            Assert.That(GroundOverlayGeometry.BorderLiftMeters, Is.GreaterThan(GroundOverlayGeometry.GroundLiftMeters), "border must sit above fill to win the depth test");
        }

        private static void AssertTriFacesUp(ReadOnlySpan<Vector3> triangle)
        {
            Vector3 along = triangle[1] - triangle[0];
            Vector3 across = triangle[2] - triangle[0];
            float facingY = (along.Z * across.X) - (along.X * across.Z);
            Assert.That(facingY, Is.GreaterThan(0f), "triangles must face +Y so backface culling cannot hide ground overlays");
        }

        private sealed class Vector3EqualityComparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 x, Vector3 y) =>
                MathF.Abs(x.X - y.X) < Epsilon && MathF.Abs(x.Y - y.Y) < Epsilon && MathF.Abs(x.Z - y.Z) < Epsilon;

            public int GetHashCode(Vector3 obj) => obj.GetHashCode();
        }

        private static readonly Vector3EqualityComparer Vec3Eq = new();
    }
}
