using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Minimap;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
        /// <summary>
        /// 相机 yaw → minimap 基向量的手性守卫。
        /// 全 yaw 周期内 (mapRight, mapUp) 必须保持：单位长度、叉积恒定（无翻转/退化区间）、
        /// 有限 yaw 永不触发 NormalizeOrDefault 回退；朝向偏移在 mapRight 反向的支点切割处
        /// 必须被下游角度归一化完全吸收（无朝向跳变）。
        /// 与主视图 CreateLookAt 一致的 (forward, right) 基在逻辑平面坐标系里叉积为 -1：
        /// minimap 的 Y 轴翻转到屏幕坐标由投影核的 (1 - y) 承担，不允许在这里再翻 mapRight。
        /// </summary>
    [TestFixture]
    public sealed class MinimapCameraBasisChiralityTests
    {
        private const float FieldHalfExtentCm = 1000f;
        private const float FieldScalePx = 200f;

        [Test]
        public void CameraMinimapBasis_PreservesChiralityAcrossFullYawCycle()
        {
            foreach (float yaw in YawSweep())
            {
                WorldPlane2D.CameraMinimapBasisFromYawDegrees(yaw, out Vector2 mapRight, out Vector2 mapUp);

                float cross = (mapRight.X * mapUp.Y) - (mapRight.Y * mapUp.X);
                Assert.That(cross, Is.EqualTo(-1f).Within(0.001f),
                    $"chirality cross(right, up) must stay -1 (main-view-consistent basis) at yaw {yaw}");
                Assert.That(mapRight.LengthSquared(), Is.EqualTo(1f).Within(0.001f),
                    $"mapRight must stay unit length at yaw {yaw}");
                Assert.That(mapUp.LengthSquared(), Is.EqualTo(1f).Within(0.001f),
                    $"mapUp must stay unit length at yaw {yaw}");

                Vector2 normalizedRight = WorldPlane2D.NormalizeOrDefault(mapRight, Vector2.UnitX);
                Vector2 normalizedUp = WorldPlane2D.NormalizeOrDefault(mapUp, Vector2.UnitY);
                Assert.That(Vector2.Distance(normalizedRight, mapRight), Is.LessThan(0.001f),
                    $"finite yaw {yaw} must never hit the NormalizeOrDefault fallback for mapRight");
                Assert.That(Vector2.Distance(normalizedUp, mapUp), Is.LessThan(0.001f),
                    $"finite yaw {yaw} must never hit the NormalizeOrDefault fallback for mapUp");
            }
        }

        [Test]
        public void RotateBasisProjection_IsIsometricForEveryYaw_NoCollapseInterval()
        {
            Vector2 first = new(100f, 0f);
            Vector2 second = new(-100f, 0f);
            float expectedSeparationPx =
                Vector2.Distance(first, second) * FieldScalePx / (FieldHalfExtentCm * 2f);

            foreach (float yaw in YawSweep())
            {
                WorldPlane2D.CameraMinimapBasisFromYawDegrees(yaw, out Vector2 mapRight, out Vector2 mapUp);
                WorldPlane2D.ProjectWorldCmToScreenUnclipped(
                    first.X, first.Y, 0f, 0f,
                    mapRight.X, mapRight.Y, mapUp.X, mapUp.Y,
                    FieldHalfExtentCm, 0f, 0f, FieldScalePx,
                    out float firstX, out float firstY);
                WorldPlane2D.ProjectWorldCmToScreenUnclipped(
                    second.X, second.Y, 0f, 0f,
                    mapRight.X, mapRight.Y, mapUp.X, mapUp.Y,
                    FieldHalfExtentCm, 0f, 0f, FieldScalePx,
                    out float secondX, out float secondY);

                float separationPx = MathF.Sqrt(
                    ((firstX - secondX) * (firstX - secondX)) +
                    ((firstY - secondY) * (firstY - secondY)));
                Assert.That(separationPx, Is.EqualTo(expectedSeparationPx).Within(0.5f),
                    $"projection must stay an isometry at yaw {yaw}; a collapse or stretch means a degenerate basis");
            }
        }

        [Test]
        public void ScreenFacingOffset_BranchCutIsAbsorbedByDownstreamAngleAndBucket()
        {
            float[] facingSamples = { 0.3f, 1.2f, 2.7f, 4.9f, -1.4f };

            float previousYaw = float.NaN;
            foreach (float yaw in FineYawSweepAroundBranchCut())
            {
                WorldPlane2D.CameraMinimapBasisFromYawDegrees(yaw, out Vector2 mapRight, out Vector2 mapUp);
                float offsetRad = WorldPlane2D.ResolveScreenFacingOffsetRad(in mapRight, in mapUp, out bool reflected);

                foreach (float facingRad in facingSamples)
                {
                    float screenFacing = WorldPlane2D.ProjectFacingRadToScreen(facingRad, offsetRad, reflected);
                    float basisFacing = WorldPlane2D.ProjectFacingRadToScreen(facingRad, in mapRight, in mapUp);
                    Assert.That(
                        WorldPlane2D.AngleDistanceRad(screenFacing, basisFacing),
                        Is.LessThan(0.001f),
                        $"offset-based and basis-based screen facing must agree at yaw {yaw} (main-view-consistent basis)");

                    int bucket = WorldPlane2D.ProjectFacingRadToScreenBucket(
                        facingRad, offsetRad, reflected, MinimapScreenMarkerBuffer.OrientationBucketCount);

                    if (!float.IsNaN(previousYaw))
                    {
                        WorldPlane2D.CameraMinimapBasisFromYawDegrees(
                            previousYaw, out Vector2 previousRight, out Vector2 previousUp);
                        float previousOffset = WorldPlane2D.ResolveScreenFacingOffsetRad(
                            in previousRight, in previousUp, out bool previousReflected);
                        float previousScreenFacing = WorldPlane2D.ProjectFacingRadToScreen(facingRad, previousOffset, previousReflected);
                        // 反射基（det=−1）屏幕角随 yaw 反向前进；旋转基（det=+1）同向。
                        float yawStepRad = WorldPlane2D.NormalizeSignedRad(
                            WorldPlane2D.DegToRadValue(yaw - previousYaw));
                        float expectedAdvance = reflected ? -yawStepRad : yawStepRad;
                        float actualAdvance = WorldPlane2D.NormalizeSignedRad(screenFacing - previousScreenFacing);
                        Assert.That(
                            WorldPlane2D.AngleDistanceRad(actualAdvance, expectedAdvance),
                            Is.LessThan(0.001f),
                            $"screen facing must advance by exactly the yaw step {previousYaw} → {yaw}; " +
                            "any excess jump means the offset branch cut leaked into the screen-facing angle");
                    }
                }

                previousYaw = yaw;
            }
        }

        [Test]
        public void RotateWithCameraProjection_MatchesMainViewLateralOrder()
        {
            List<float> mirroredYaws = new();
            int sampledYawCount = 0;
            foreach (float yaw in YawSweep())
            {
                sampledYawCount++;
                var state = new CameraState
                {
                    RigKind = CameraRigKind.Orbit,
                    TargetCm = Vector2.Zero,
                    Yaw = yaw,
                    Pitch = 45f,
                    DistanceCm = 2000f,
                    FovYDeg = 60f,
                };
                CameraRenderState3D renderState = CameraViewportUtil.StateToRenderState(state);

                Vector3 forward3 = Vector3.Normalize(renderState.Target - renderState.Position);
                Vector3 right3 = Vector3.Normalize(Vector3.Cross(forward3, renderState.Up));
                Vector2 cameraRightLogic = new(right3.X, right3.Z);

                const float lateralCm = 100f;
                Vector2 worldRightOfCamera = cameraRightLogic * lateralCm;
                Vector2 worldLeftOfCamera = -cameraRightLogic * lateralCm;

                var resolution = new Vector2(1280f, 720f);
                float aspect = resolution.X / resolution.Y;
                Vector2 mainScreenRight = CameraViewportUtil.WorldToScreen(
                    WorldPlane2D.LogicCmToVisualMeters(worldRightOfCamera.X, worldRightOfCamera.Y),
                    in renderState,
                    resolution,
                    aspect);
                Vector2 mainScreenLeft = CameraViewportUtil.WorldToScreen(
                    WorldPlane2D.LogicCmToVisualMeters(worldLeftOfCamera.X, worldLeftOfCamera.Y),
                    in renderState,
                    resolution,
                    aspect);
                Assert.That(float.IsNaN(mainScreenRight.X) || float.IsNaN(mainScreenLeft.X), Is.False,
                    $"main-view truth projection must be in frustum at yaw {yaw}");
                Assert.That(mainScreenRight.X, Is.GreaterThan(mainScreenLeft.X),
                    $"main-view truth sanity: the camera-right point must render right of the camera-left point at yaw {yaw}");

                WorldPlane2D.CameraMinimapBasisFromYawDegrees(yaw, out Vector2 mapRight, out Vector2 mapUp);
                bool rightProjected = WorldPlane2D.TryProjectWorldCmToScreen(
                    worldRightOfCamera.X, worldRightOfCamera.Y, 0f, 0f,
                    mapRight.X, mapRight.Y, mapUp.X, mapUp.Y,
                    FieldHalfExtentCm, 0f, 0f, FieldScalePx,
                    out _, out _, out float minimapRightX, out _);
                bool leftProjected = WorldPlane2D.TryProjectWorldCmToScreen(
                    worldLeftOfCamera.X, worldLeftOfCamera.Y, 0f, 0f,
                    mapRight.X, mapRight.Y, mapUp.X, mapUp.Y,
                    FieldHalfExtentCm, 0f, 0f, FieldScalePx,
                    out _, out _, out float minimapLeftX, out _);
                Assert.That(rightProjected && leftProjected, Is.True,
                    $"minimap must project both lateral points at yaw {yaw}");

                if (minimapRightX <= minimapLeftX)
                {
                    mirroredYaws.Add(yaw);
                }
            }

            Assert.That(mirroredYaws, Is.Empty,
                "minimap must not mirror the main view at any yaw; the point on the camera's right " +
                $"(main-view screen right of center) must also land right of center on the minimap. " +
                $"Mirrored at {mirroredYaws.Count}/{sampledYawCount} sampled yaws: {string.Join(", ", mirroredYaws)}");
        }

        private static IEnumerable<float> YawSweep()
        {
            for (int yaw = 0; yaw <= 360; yaw += 15)
            {
                yield return yaw;
            }

            yield return -15f;
            yield return 375f;
            yield return 742.5f;
        }

        private static IEnumerable<float> FineYawSweepAroundBranchCut()
        {
            for (int yaw = 0; yaw <= 360; yaw += 15)
            {
                yield return yaw;
            }

            for (int step = -8; step <= 8; step++)
            {
                yield return 180f + (step * 0.25f);
            }
        }
    }
}
