using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class WorldPlane2DTests
    {
        [Test]
        public void FacingRad_UsesLogicXyAndVisualXzConvention()
        {
            AssertVector2(WorldPlane2D.DirectionFromFacingRad(0f), new Vector2(1f, 0f));
            AssertVector2(WorldPlane2D.DirectionFromFacingRad(MathF.PI * 0.5f), new Vector2(0f, 1f));

            AssertVector3(VisualMath.FacingRadToVisualForward(0f), new Vector3(1f, 0f, 0f));
            AssertVector3(VisualMath.FacingRadToVisualForward(MathF.PI * 0.5f), new Vector3(0f, 0f, 1f));
        }

        [Test]
        public void Fix64FacingHelpers_UseSameLogicPlaneTruth()
        {
            Assert.That(
                WorldPlane2D.FacingDegreesPositiveFromDirection(Fix64.FromInt(1), Fix64.Zero),
                Is.EqualTo(0));
            Assert.That(
                WorldPlane2D.FacingDegreesPositiveFromDirection(Fix64.Zero, Fix64.FromInt(1)),
                Is.EqualTo(90));
            Assert.That(
                WorldPlane2D.FacingDegreesPositiveFromDirection(Fix64.FromInt(-1), Fix64.Zero),
                Is.EqualTo(180));
            Assert.That(
                WorldPlane2D.FacingDegreesPositiveFromDirection(Fix64.Zero, Fix64.FromInt(-1)),
                Is.EqualTo(270));

            Fix64Vec2 offset = WorldPlane2D.Fix64OffsetCmFromFacingRad(Fix64.Pi / Fix64.FromInt(2), Fix64.FromInt(300));
            Assert.That(offset.X.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            Assert.That(offset.Y.ToFloat(), Is.EqualTo(300f).Within(0.01f));
        }

        [Test]
        public void FacingRadToVisualYRotation_RoundTripsThroughVisualLocalX()
        {
            float[] samples =
            {
                0f,
                MathF.PI * 0.25f,
                MathF.PI * 0.5f,
                MathF.PI,
                -MathF.PI * 0.5f,
            };

            for (int i = 0; i < samples.Length; i++)
            {
                float facingRad = samples[i];
                Quaternion rotation = WorldPlane2D.FacingRadToVisualYRotation(facingRad);
                Vector3 visualLocalX = Vector3.Transform(Vector3.UnitX, rotation);
                Vector3 visualForward = VisualMath.FacingRadToVisualForward(facingRad);

                AssertVector3(visualLocalX, visualForward);
                Assert.That(VisualMath.TryExtractFacingRadFromVisualYRotation(rotation, out float extracted), Is.True);
                Assert.That(
                    WorldPlane2D.NormalizePositiveRad(extracted),
                    Is.EqualTo(WorldPlane2D.NormalizePositiveRad(facingRad)).Within(0.0001f));
            }
        }

        [Test]
        public void NormalizeOrIdentity_RejectsInvalidQuaternionsAtWorldPlaneBoundary()
        {
            Assert.That(VisualMath.NormalizeOrIdentity(default), Is.EqualTo(Quaternion.Identity));
            Assert.That(
                VisualMath.NormalizeOrIdentity(new Quaternion(float.PositiveInfinity, 0f, 0f, 1f)),
                Is.EqualTo(Quaternion.Identity));
            Assert.That(
                VisualMath.NormalizeOrIdentity(new Quaternion(float.NaN, 0f, 0f, 1f)),
                Is.EqualTo(Quaternion.Identity));

            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.25f);
            AssertQuaternion(rotation, VisualMath.NormalizeOrIdentity(rotation));
        }

        [Test]
        public void ProjectFacingRadToScreen_UsesMapBasis()
        {
            Vector2 mapRight = Vector2.UnitX;
            Vector2 mapUp = Vector2.UnitY;

            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(-MathF.PI * 0.5f).Within(0.0001f));

            mapRight = Vector2.UnitY;
            mapUp = new Vector2(-1f, 0f);
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(MathF.PI * 0.5f).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void CameraMinimapBasis_UsesMainViewScreenRight()
        {
            WorldPlane2D.CameraMinimapBasisFromYawDegrees(0f, out Vector2 mapRight, out Vector2 mapUp);
            AssertVector2(mapRight, new Vector2(-1f, 0f));
            AssertVector2(mapUp, new Vector2(0f, 1f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(MathF.PI).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(-MathF.PI * 0.5f).Within(0.0001f));

            WorldPlane2D.CameraMinimapBasisFromYawDegrees(90f, out mapRight, out mapUp);
            AssertVector2(mapRight, new Vector2(0f, -1f));
            AssertVector2(mapUp, new Vector2(-1f, 0f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(MathF.PI * 0.5f).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(MathF.PI).Within(0.0001f));
        }

        [Test]
        public void ProjectFacingRadToScreen_UsesPrecomputedBasisOffset()
        {
            Vector2 mapRight = Vector2.UnitY;
            Vector2 mapUp = new Vector2(-1f, 0f);
            float offset = WorldPlane2D.ResolveScreenFacingOffsetRad(in mapRight, in mapUp, out bool reflected);

            float[] samples =
            {
                0f,
                MathF.PI * 0.25f,
                MathF.PI * 0.5f,
                -MathF.PI * 0.75f,
            };

            Assert.That(reflected, Is.False, "det=+1 basis must not report reflection");
            for (int i = 0; i < samples.Length; i++)
            {
                float facing = samples[i];
                Assert.That(
                    WorldPlane2D.ProjectFacingRadToScreen(facing, offset, reflected),
                    Is.EqualTo(WorldPlane2D.ProjectFacingRadToScreen(facing, in mapRight, in mapUp)).Within(0.0001f));
            }

            // det=−1 基（旋转随相机、与主视图同手性）：偏移快路径必须按反射换算。
            WorldPlane2D.CameraMinimapBasisFromYawDegrees(0f, out mapRight, out mapUp);
            offset = WorldPlane2D.ResolveScreenFacingOffsetRad(in mapRight, in mapUp, out reflected);
            Assert.That(reflected, Is.True, "camera-facing minimap basis is reflected relative to logic XY");
            for (int i = 0; i < samples.Length; i++)
            {
                float facing = samples[i];
                Assert.That(
                    WorldPlane2D.ProjectFacingRadToScreen(facing, offset, reflected),
                    Is.EqualTo(WorldPlane2D.ProjectFacingRadToScreen(facing, in mapRight, in mapUp)).Within(0.0001f));
            }
        }

        [Test]
        public void TransformVisualLocal2D_TreatsLocalXAsForward()
        {
            Vector3 origin = new(10f, 2f, 20f);

            AssertVector3(
                VisualMath.TransformVisualLocal2D(origin, 0f, new Vector3(3f, 4f, 5f)),
                new Vector3(13f, 6f, 25f));
            AssertVector3(
                VisualMath.TransformVisualLocal2D(origin, MathF.PI * 0.5f, new Vector3(3f, 4f, 5f)),
                new Vector3(5f, 6f, 23f));
        }

        [Test]
        public void CameraYawBasis_UsesSameWorldPlaneConvention()
        {
            AssertVector2(WorldPlane2D.CameraForwardFromYawDegrees(0f), new Vector2(0f, 1f));
            AssertVector2(WorldPlane2D.CameraRightFromYawDegrees(0f), new Vector2(-1f, 0f));
            AssertVector2(WorldPlane2D.CameraForwardFromYawDegrees(90f), new Vector2(-1f, 0f));
            AssertVector2(WorldPlane2D.CameraRightFromYawDegrees(90f), new Vector2(0f, -1f));
        }

        [Test]
        public void VisualCameraOffset_UsesVisualXzPlane()
        {
            AssertVector3(WorldPlane2D.VisualCameraTargetToCameraOffset(0f, 0f, 10f), new Vector3(0f, 0f, -10f));
            AssertVector3(WorldPlane2D.VisualCameraTargetToCameraOffset(90f, 0f, 10f), new Vector3(10f, 0f, 0f));
        }

        [Test]
        public void MapProjection_UsesSingleWorldPlaneBasis()
        {
            WorldPlane2D.WorldToMapNormalizedUnclipped(
                worldXcm: 1250f,
                worldYcm: 2100f,
                centerXcm: 1000f,
                centerYcm: 2000f,
                rightX: 1f,
                rightY: 0f,
                upX: 0f,
                upY: 1f,
                halfExtentCm: 500f,
                out float normalizedX,
                out float normalizedY);

            Assert.That(normalizedX, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(normalizedY, Is.EqualTo(0.60f).Within(0.0001f));
            Assert.That(
                WorldPlane2D.TryWorldToMapNormalized(
                    1250f,
                    2100f,
                    1000f,
                    2000f,
                    1f,
                    0f,
                    0f,
                    1f,
                    500f,
                    out normalizedX,
                    out normalizedY),
                Is.True);
            AssertVector2(
                WorldPlane2D.MapLocalToWorld(1000f, 2000f, 250f, 100f, Vector2.UnitX, Vector2.UnitY),
                new Vector2(1250f, 2100f));
        }

        [Test]
        public void VisualGroundPlaneIntersection_ReturnsLogicCentimeters()
        {
            Vector3 origin = new(12f, 8f, -7f);
            Vector3 direction = Vector3.Normalize(new Vector3(0f, -1f, 0f));

            Assert.That(
                WorldPlane2D.TryIntersectVisualGroundPlane(
                    in origin,
                    in direction,
                    planeYMeters: 0f,
                    maxDirectionY: -0.0001f,
                    out Vector2 worldCm),
                Is.True);
            AssertVector2(worldCm, new Vector2(1200f, -700f));
        }

        [Test]
        public void ClipConvexPolygonToUnitSquare_PassesThroughFullyInsidePolygon()
        {
            Vector2[] quad =
            {
                new(0.25f, 0.25f),
                new(0.75f, 0.25f),
                new(0.75f, 0.75f),
                new(0.25f, 0.75f),
            };
            Vector2[] result = new Vector2[8];

            int count = WorldPlane2D.ClipConvexPolygonToUnitSquare(quad, result);

            Assert.That(count, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
            {
                AssertVector2(result[i], quad[i]);
            }
        }

        [Test]
        public void ClipConvexPolygonToUnitSquare_ClipsFarOutCornersWithoutFolding()
        {
            // 低俯角视锥足迹：近边在界内，屏幕上沿射线的远边落点远超世界边界。
            Vector2[] quad =
            {
                new(0.3f, 0.38f),
                new(0.7f, 0.42f),
                new(1.5f, 5f),
                new(-0.5f, 5f),
            };
            Vector2[] result = new Vector2[8];

            int count = WorldPlane2D.ClipConvexPolygonToUnitSquare(quad, result);

            Assert.That(count, Is.GreaterThanOrEqualTo(3));
            for (int i = 0; i < count; i++)
            {
                Assert.That(result[i].X, Is.InRange(0f, 1f), $"clipped x must stay inside the unit square at {i}");
                Assert.That(result[i].Y, Is.InRange(0f, 1f), $"clipped y must stay inside the unit square at {i}");
            }

            AssertConvexWinding(result.AsSpan(0, count));

            // 屏幕中心（相机目标）必然在视锥足迹内；逐顶点 clamp 折叠出的蝴蝶结会丢失包含性。
            Vector2 target = new(0.5f, 0.5f);
            Assert.That(ContainsPointConvex(result.AsSpan(0, count), target), Is.True,
                "the clipped frustum footprint must still contain the camera target");
        }

        [Test]
        public void ClipConvexPolygonToUnitSquare_FullyOutside_ReturnsEmpty()
        {
            Vector2[] quad =
            {
                new(2f, 2f),
                new(3f, 2f),
                new(3f, 3f),
                new(2f, 3f),
            };
            Vector2[] result = new Vector2[8];

            int count = WorldPlane2D.ClipConvexPolygonToUnitSquare(quad, result);

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void ClipConvexPolygonToUnitSquare_PolygonEngulfingSquare_ClipsToSquare()
        {
            Vector2[] quad =
            {
                new(-5f, -5f),
                new(5f, -5f),
                new(5f, 5f),
                new(-5f, 5f),
            };
            Vector2[] result = new Vector2[8];

            int count = WorldPlane2D.ClipConvexPolygonToUnitSquare(quad, result);

            Assert.That(count, Is.EqualTo(4));
            foreach (Vector2 point in result.AsSpan(0, count))
            {
                Assert.That(MathF.Abs(point.X - Math.Clamp(point.X, 0f, 1f)), Is.LessThan(0.0001f));
                Assert.That(MathF.Abs(point.Y - Math.Clamp(point.Y, 0f, 1f)), Is.LessThan(0.0001f));
            }

            // 四个角点都出现在输出里（顺序不限）。
            foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) })
            {
                bool found = false;
                for (int i = 0; i < count && !found; i++)
                {
                    found = Vector2.DistanceSquared(result[i], corner) < 0.0001f;
                }

                Assert.That(found, Is.True, $"square corner {corner} must survive the clip");
            }
        }

        private static void AssertConvexWinding(ReadOnlySpan<Vector2> polygon)
        {
            float firstCross = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 previous = polygon[i];
                Vector2 current = polygon[(i + 1) % polygon.Length];
                Vector2 next = polygon[(i + 2) % polygon.Length];
                float cross = ((current.X - previous.X) * (next.Y - current.Y)) -
                              ((current.Y - previous.Y) * (next.X - current.X));
                if (MathF.Abs(cross) < 0.0001f)
                {
                    continue;
                }

                if (firstCross == 0f)
                {
                    firstCross = cross;
                }
                else
                {
                    Assert.That(MathF.Sign(cross), Is.EqualTo(MathF.Sign(firstCross)),
                        "clipped polygon must stay convex with a single winding; mixed signs mean a folded/bowtie shape");
                }
            }

            Assert.That(firstCross, Is.Not.EqualTo(0f), "clipped polygon must not degenerate to a line");
        }

        private static bool ContainsPointConvex(ReadOnlySpan<Vector2> polygon, Vector2 point)
        {
            int positive = 0;
            int negative = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                float cross = ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));
                if (cross > 0.0001f)
                {
                    positive++;
                }
                else if (cross < -0.0001f)
                {
                    negative++;
                }
            }

            return positive == 0 || negative == 0;
        }

        [Test]
        public void NormalizeDegreesPositive_IsCameraAngleBoundaryTruth()
        {
            Assert.That(WorldPlane2D.NormalizeDegreesPositive(725f), Is.EqualTo(5f).Within(0.0001f));
            Assert.That(WorldPlane2D.NormalizeDegreesPositive(-45f), Is.EqualTo(315f).Within(0.0001f));
            Assert.That(WorldPlane2D.NormalizeDegreesPositive(float.NaN), Is.EqualTo(0f));
            Assert.That(WorldPlane2D.NormalizeDegreesPositive(float.PositiveInfinity), Is.EqualTo(0f));
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
        }

        private static void AssertQuaternion(Quaternion actual, Quaternion expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
            Assert.That(actual.W, Is.EqualTo(expected.W).Within(0.0001f));
        }
    }
}
