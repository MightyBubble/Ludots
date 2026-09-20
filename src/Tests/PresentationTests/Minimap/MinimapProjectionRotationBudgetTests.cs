using System;
using System.IO;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// minimap 投影与渲染帧率解耦的守卫：标记数超过轮转预算（2500）时，
    /// 相机/缩放/knowledge 视口未变的帧零重投影、屏幕缓冲整帧保留；
    /// 每周期必有一次全量重投影覆盖全部标记；相机/缩放变化立即触发全量帧；
    /// 小标记集保持逐帧全量投影的精确语义。
    /// </summary>
    [TestFixture]
    public sealed class MinimapProjectionRotationBudgetTests
    {
        private const int RotationMarkerCount = 3000;

        [Test]
        public void UnchangedFramesBetweenSlices_DoZeroReprojectionAndRetainScreenMarkers()
        {
            using GameEngine engine = CreateEngine();
            MinimapRuntime runtime = CreateRuntime();
            var markers = new MinimapMarkerBuffer(RotationMarkerCount);
            var screenMarkers = new MinimapScreenMarkerBuffer(RotationMarkerCount);
            SeedMarkers(markers, RotationMarkerCount);

            runtime.Visible = true;
            runtime.UseRtsFullMapPreset();
            runtime.Refresh(engine, markers, screenMarkers);
            Assert.That(screenMarkers.Count, Is.EqualTo(RotationMarkerCount));
            Assert.That(runtime.ReprojectedMarkerCountLastFrame, Is.EqualTo(RotationMarkerCount));

            // 轮转周期为 2：第二帧可能仍是相位对齐全量帧，第三帧必须零重投影整帧保留。
            int zeroReprojectionFrames = 0;
            for (int frame = 0; frame < 3; frame++)
            {
                runtime.Refresh(engine, markers, screenMarkers);
                if (runtime.ReprojectedMarkerCountLastFrame == 0)
                {
                    zeroReprojectionFrames++;
                    Assert.That(screenMarkers.Count, Is.EqualTo(RotationMarkerCount),
                        "Zero-reprojection frame must retain every screen marker.");
                    Assert.That(runtime.VisibleMarkerCount, Is.EqualTo(RotationMarkerCount));
                    Assert.That(runtime.RetainedMarkerCountLastFrame, Is.EqualTo(RotationMarkerCount));
                }
            }

            Assert.That(zeroReprojectionFrames, Is.GreaterThanOrEqualTo(1),
                "Unchanged frames between slice phases must do zero reprojection.");
        }

        [Test]
        public void RotationReprojectsEveryMarkerWithinPeriod()
        {
            using GameEngine engine = CreateEngine();
            MinimapRuntime runtime = CreateRuntime();
            var markers = new MinimapMarkerBuffer(RotationMarkerCount);
            var screenMarkers = new MinimapScreenMarkerBuffer(RotationMarkerCount);
            SeedMarkers(markers, RotationMarkerCount);

            runtime.Visible = true;
            runtime.UseRtsFullMapPreset();
            int fullProjectionFrames = 0;
            for (int frame = 0; frame < 6; frame++)
            {
                runtime.Refresh(engine, markers, screenMarkers);
                Assert.That(screenMarkers.Count, Is.EqualTo(RotationMarkerCount),
                    $"Frame {frame}: screen markers must never be lost across rotation.");
                if (runtime.ReprojectedMarkerCountLastFrame == RotationMarkerCount)
                {
                    fullProjectionFrames++;
                }
            }

            Assert.That(fullProjectionFrames, Is.GreaterThanOrEqualTo(3),
                "Every rotation period must contain one full projection covering all markers.");
        }

        [Test]
        public void CameraViewportChange_ForcesImmediateFullReprojection()
        {
            using GameEngine engine = CreateEngine();
            MinimapRuntime runtime = CreateRuntime();
            var markers = new MinimapMarkerBuffer(RotationMarkerCount);
            var screenMarkers = new MinimapScreenMarkerBuffer(RotationMarkerCount);
            SeedMarkers(markers, RotationMarkerCount);

            runtime.Visible = true;
            runtime.UseRtsFullMapPreset();
            runtime.Refresh(engine, markers, screenMarkers);
            runtime.Refresh(engine, markers, screenMarkers);
            runtime.Refresh(engine, markers, screenMarkers);
            Assert.That(runtime.ReprojectedMarkerCountLastFrame, Is.Zero,
                "Third unchanged frame sits between slice phases and must skip reprojection.");

            runtime.SetZoomNormalized(0.4f);
            int rebuildsBeforeZoom = runtime.FullProjectionRebuildCount;
            runtime.Refresh(engine, markers, screenMarkers);
            Assert.That(runtime.FullProjectionRebuildCount, Is.EqualTo(rebuildsBeforeZoom + 1),
                "Zoom change must trigger a full reprojection frame immediately.");
            Assert.That(runtime.ReprojectedMarkerCountLastFrame, Is.EqualTo(screenMarkers.Count),
                "Full reprojection frame must re-evaluate every marker that lands in the viewport.");
        }

        [Test]
        public void SmallMarkerSets_KeepExactFrameByFrameReprojection()
        {
            using GameEngine engine = CreateEngine();
            MinimapRuntime runtime = CreateRuntime();
            var markers = new MinimapMarkerBuffer(8);
            var screenMarkers = new MinimapScreenMarkerBuffer(8);
            SeedMarkers(markers, 8);

            runtime.Visible = true;
            runtime.UseRtsFullMapPreset();
            runtime.Refresh(engine, markers, screenMarkers);
            float markerScreenXBefore = FindScreenXByStableId(screenMarkers, markers.GetStableId(3));

            markers.BeginFrame();
            SeedMarkers(markers, 8, worldXOffsetCm: 24000f);
            runtime.Refresh(engine, markers, screenMarkers);
            Assert.That(runtime.ReprojectedMarkerCountLastFrame, Is.EqualTo(8),
                "Marker sets below the rotation budget must fully reproject every frame.");
            float markerScreenXAfter = FindScreenXByStableId(screenMarkers, markers.GetStableId(3));
            Assert.That(markerScreenXAfter, Is.Not.EqualTo(markerScreenXBefore),
                "Marker movement must be reflected on the very next frame for small sets.");
        }

        private static void SeedMarkers(MinimapMarkerBuffer markers, int count, float worldXOffsetCm = 0f)
        {
            markers.BeginFrame();
            var color = new Vector4(0.2f, 0.8f, 1f, 1f);
            for (int i = 0; i < count; i++)
            {
                Assert.That(
                    markers.TryAdd(
                        900_000 + i,
                        worldXOffsetCm - 20000f + (i * (40000f / MathF.Max(1, count - 1))),
                        -10000f + (i % 64) * 300f,
                        in color,
                        6f),
                    Is.True);
            }
        }

        private static float FindScreenXByStableId(MinimapScreenMarkerBuffer screenMarkers, int rawStableId)
        {
            for (int i = 0; i < screenMarkers.Count; i++)
            {
                if (screenMarkers.GetStableId(i) == ComposeScreenStableId(rawStableId))
                {
                    return screenMarkers.GetScreenX(i);
                }
            }

            throw new InvalidOperationException($"Screen marker for stable id {rawStableId} was not found.");
        }

        private static int ComposeScreenStableId(int stableId)
        {
            unchecked
            {
                int hash = (stableId * 397) ^ 0x4d4d;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }

        private static MinimapRuntime CreateRuntime()
        {
            return new MinimapRuntime(new MinimapRuntimeConfig
            {
                InitialZoomNormalized = 1f,
                WheelZoomNormalizedStep = 0.08f,
                ButtonZoomNormalizedStep = 0.18f,
                ZoomSliderEnabled = true,
                ModeToggleEnabled = true,
                RotateToggleEnabled = true,
                DebugMarkerSampleCapacity = 128,
                MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MinZoomExplicitHalfExtentCm = 750f,
                MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MaxZoomExplicitHalfExtentCm = 50000f,
            });
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            return engine;
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Directory.GetParent(current)!.FullName;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
