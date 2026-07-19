using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

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

            AssertVector3(WorldPlane2D.FacingRadToVisualForward(0f), new Vector3(1f, 0f, 0f));
            AssertVector3(WorldPlane2D.FacingRadToVisualForward(MathF.PI * 0.5f), new Vector3(0f, 0f, 1f));
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
                Vector3 visualForward = WorldPlane2D.FacingRadToVisualForward(facingRad);

                AssertVector3(visualLocalX, visualForward);
                Assert.That(WorldPlane2D.TryExtractFacingRadFromVisualYRotation(rotation, out float extracted), Is.True);
                Assert.That(
                    WorldPlane2D.NormalizePositiveRad(extracted),
                    Is.EqualTo(WorldPlane2D.NormalizePositiveRad(facingRad)).Within(0.0001f));
            }
        }

        [Test]
        public void NormalizeOrIdentity_RejectsInvalidQuaternionsAtWorldPlaneBoundary()
        {
            Assert.That(WorldPlane2D.NormalizeOrIdentity(default), Is.EqualTo(Quaternion.Identity));
            Assert.That(
                WorldPlane2D.NormalizeOrIdentity(new Quaternion(float.PositiveInfinity, 0f, 0f, 1f)),
                Is.EqualTo(Quaternion.Identity));
            Assert.That(
                WorldPlane2D.NormalizeOrIdentity(new Quaternion(float.NaN, 0f, 0f, 1f)),
                Is.EqualTo(Quaternion.Identity));

            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.25f);
            AssertQuaternion(rotation, WorldPlane2D.NormalizeOrIdentity(rotation));
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
        public void CameraMinimapBasis_IsRightHandedScreenProjection()
        {
            WorldPlane2D.CameraMinimapBasisFromYawDegrees(0f, out Vector2 mapRight, out Vector2 mapUp);
            AssertVector2(mapRight, new Vector2(1f, 0f));
            AssertVector2(mapUp, new Vector2(0f, 1f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(-MathF.PI * 0.5f).Within(0.0001f));

            WorldPlane2D.CameraMinimapBasisFromYawDegrees(90f, out mapRight, out mapUp);
            AssertVector2(mapRight, new Vector2(0f, 1f));
            AssertVector2(mapUp, new Vector2(-1f, 0f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(0f, in mapRight, in mapUp), Is.EqualTo(MathF.PI * 0.5f).Within(0.0001f));
            Assert.That(WorldPlane2D.ProjectFacingRadToScreen(MathF.PI * 0.5f, in mapRight, in mapUp), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ProjectFacingRadToScreen_UsesPrecomputedBasisOffset()
        {
            Vector2 mapRight = Vector2.UnitY;
            Vector2 mapUp = new Vector2(-1f, 0f);
            float offset = WorldPlane2D.ResolveScreenFacingOffsetRad(in mapRight, in mapUp);

            float[] samples =
            {
                0f,
                MathF.PI * 0.25f,
                MathF.PI * 0.5f,
                -MathF.PI * 0.75f,
            };

            for (int i = 0; i < samples.Length; i++)
            {
                float facing = samples[i];
                Assert.That(
                    WorldPlane2D.ProjectFacingRadToScreen(facing, offset),
                    Is.EqualTo(WorldPlane2D.ProjectFacingRadToScreen(facing, in mapRight, in mapUp)).Within(0.0001f));
            }
        }

        [Test]
        public void TransformVisualLocal2D_TreatsLocalXAsForward()
        {
            Vector3 origin = new(10f, 2f, 20f);

            AssertVector3(
                WorldPlane2D.TransformVisualLocal2D(origin, 0f, new Vector3(3f, 4f, 5f)),
                new Vector3(13f, 6f, 25f));
            AssertVector3(
                WorldPlane2D.TransformVisualLocal2D(origin, MathF.PI * 0.5f, new Vector3(3f, 4f, 5f)),
                new Vector3(5f, 6f, 23f));
        }

        [Test]
        public void CameraYawBasis_UsesSameWorldPlaneConvention()
        {
            AssertVector2(WorldPlane2D.CameraForwardFromYawDegrees(0f), new Vector2(0f, 1f));
            AssertVector2(WorldPlane2D.CameraRightFromYawDegrees(0f), new Vector2(-1f, 0f));
            AssertVector2(WorldPlane2D.CameraScreenRightFromYawDegrees(0f), new Vector2(1f, 0f));
            AssertVector2(WorldPlane2D.CameraForwardFromYawDegrees(90f), new Vector2(-1f, 0f));
            AssertVector2(WorldPlane2D.CameraRightFromYawDegrees(90f), new Vector2(0f, -1f));
            AssertVector2(WorldPlane2D.CameraScreenRightFromYawDegrees(90f), new Vector2(0f, 1f));
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
