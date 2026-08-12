using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;
using PerformerBlacksmithShowcaseMod.Runtime;
using SkiaSharp;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PerformerDynamicWorkerBenchmarkTests
    {
        private const int WarmupFrames = 8;
        private const int MeasuredFrames = 90;
        private const int MinimapHotPathWarmupFrames = 12;
        private const int MinimapHotPathMeasuredFrames = 90;
        private const string DynamicWorkerBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_DYNAMIC_WORKER_BENCHMARK_TOTAL";
        private const string MetadataSectionKey = "performerBlacksmith";
        private const string DynamicWorkerBenchmarkTotalMetadataKey = "dynamicWorkerBenchmarkTotal";
        private const string MinimapMarkerShowcaseTotalMetadataKey = "minimapMarkerShowcaseTotal";
        private static readonly int[] Counts = { 3_000, 10_000, 30_000 };

        [Test]
        public void Benchmark_DynamicWorkers_SkinnedAnimatorProductionPath_WritesReport()
        {
            DynamicWorkerBenchmarkResult[] results = new DynamicWorkerBenchmarkResult[Counts.Length];
            for (int i = 0; i < Counts.Length; i++)
            {
                results[i] = RunScenario(Counts[i]);
            }

            string artifactDir = Path.Combine(
                PerformerBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "performer-dynamic-worker-production-path");
            Directory.CreateDirectory(artifactDir);

            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            File.WriteAllText(reportPath, BuildReport(results));
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            for (int i = 0; i < results.Length; i++)
            {
                DynamicWorkerBenchmarkResult result = results[i];
                Assert.That(result.Enqueued, Is.EqualTo(result.Count));
                Assert.That(result.EntityCount, Is.EqualTo(result.Count));
                Assert.That(result.RootPerformerCount, Is.EqualTo(result.Count));
                Assert.That(result.AttachmentPerformerCount, Is.EqualTo(result.Count));
                Assert.That(result.SkinnedCount, Is.GreaterThan(0), "Skinned buffer contains camera-visible submissions, not total world performers.");
                Assert.That(result.GpuSkinnedCount, Is.EqualTo(result.SkinnedCount));
                Assert.That(result.WalkingSkinnedStateCount, Is.EqualTo(result.SkinnedCount), "Every visible dynamic worker submission must use the configured walking packed animator state.");
                Assert.That(result.ActiveAnimatorCount, Is.EqualTo(result.Count));
                Assert.That(result.GroundedPerformerCount, Is.EqualTo(result.Count));
                Assert.That(result.AttachedPerformerCount, Is.EqualTo(result.Count));
                Assert.That(result.DirectSkinnedFrameCount, Is.EqualTo(MeasuredFrames));
                Assert.That(result.MovedEntityCount, Is.GreaterThan(0), "Dynamic workers must actually move.");
                Assert.That(result.EventDrops, Is.EqualTo(0));
                Assert.That(result.CommandDrops, Is.EqualTo(0));
                Assert.That(result.SkinnedDrops, Is.EqualTo(0));
            }
        }

        [Test]
        public void DynamicWorkerBenchmarkMap_DeclaresProductionVisualHeightmapAndConfiguredTotal()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkMapId,
                frames: 2);

            Assert.That(engine.CurrentMapSession?.MapConfig.VisualHeightmapAsset, Is.EqualTo("assets/terrain/performer_blacksmith_dynamic_worker_hills.vhtm"));
            Assert.That(
                engine.CurrentMapSession!.MapConfig.Metadata["performerBlacksmith"]!["dynamicWorkerBenchmarkTotal"]!.GetValue<int>(),
                Is.EqualTo(ReadBlacksmithMetadataInt(engine, DynamicWorkerBenchmarkTotalMetadataKey)));
            Assert.That(engine.CurrentMapSession.MapConfig.Metadata["performerBlacksmith"]!["dynamicWorkerScatterPaddingCm"]!.GetValue<float>(), Is.EqualTo(6000f));
            Assert.That(engine.CurrentMapSession.MapConfig.Metadata["performerBlacksmith"]!["dynamicWorkerMovementPaddingCm"]!.GetValue<float>(), Is.EqualTo(6000f));
            Assert.That(engine.GetService(CoreServiceKeys.VisualHeightmap), Is.AssignableTo<IVisualHeightmapRenderSource>());
        }

        [Test]
        public void DynamicWorkerLargeWorldBenchmarkMap_DeclaresProductionLargeWorldSurface()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerLargeWorldBenchmarkMapId,
                frames: 2);

            Assert.That(engine.CurrentMapSession?.MapConfig.VisualHeightmapAsset, Is.EqualTo("assets/terrain/performer_blacksmith_dynamic_worker_large_world.vhtm"));
            Assert.That(engine.CurrentMapSession!.MapConfig.Boards, Has.Count.EqualTo(1));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].SpatialType, Is.EqualTo("Grid"));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].WidthInMacroTiles, Is.EqualTo(256));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].HeightInMacroTiles, Is.EqualTo(256));
            Assert.That(
                engine.CurrentMapSession.MapConfig.Metadata["performerBlacksmith"]!["dynamicWorkerBenchmarkTotal"]!.GetValue<int>(),
                Is.EqualTo(30_000));

            IVisualHeightmapRenderSource renderSource = engine.GetService(CoreServiceKeys.VisualHeightmap) as IVisualHeightmapRenderSource
                ?? throw new InvalidOperationException("VisualHeightmap render source missing.");
            Assert.That(renderSource.Bounds.Width, Is.EqualTo(6_553_600));
            Assert.That(renderSource.Bounds.Height, Is.EqualTo(6_553_600));
            Assert.That(engine.WorldSizeSpec.Bounds.Width, Is.EqualTo(6_553_600));
            Assert.That(engine.WorldSizeSpec.Bounds.Height, Is.EqualTo(6_553_600));
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_UsesVisualHeightmapSceneAndCameraProfile()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 2);

            const string expectedHeightmapAsset = "assets/terrain/performer_blacksmith_minimap_marker_large_world_relief.vhtm";
            Assert.That(engine.CurrentMapSession?.MapConfig.VisualHeightmapAsset, Is.EqualTo(expectedHeightmapAsset));
            Assert.That(engine.CurrentMapSession?.MapConfig.DefaultCamera?.VirtualCameraId, Is.EqualTo("PerformerBlacksmith.Camera.LargeWorldHeightmap"));
            Assert.That(engine.GetService(CoreServiceKeys.VisualHeightmap), Is.AssignableTo<IVisualHeightmapRenderSource>());

            VirtualCameraRegistry cameraRegistry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry missing.");
            Assert.That(cameraRegistry.TryGet("PerformerBlacksmith.Camera.LargeWorldHeightmap", out VirtualCameraDefinition? definition), Is.True);
            Assert.That(definition.TargetHeightMode, Is.EqualTo(VirtualCameraTargetHeightMode.VisualHeightmap));
            Assert.That(definition.TargetHeightLayerIndex, Is.EqualTo(0));

            IVisualHeightmap heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
                ?? throw new InvalidOperationException("VisualHeightmap missing.");
            Assert.That(heightmap.TrySampleHeightCm(
                engine.AuthorityCamera().State.TargetCm.X,
                engine.AuthorityCamera().State.TargetCm.Y,
                out float expectedHeightCm), Is.True);
            Assert.That(engine.AuthorityCamera().State.TargetHeightCm, Is.EqualTo(expectedHeightCm + definition.TargetHeightOffsetCm).Within(0.01f));

            string assetPath = Path.Combine(
                PerformerBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "mods",
                "showcases",
                "performer_blacksmith",
                "PerformerBlacksmithShowcaseMod",
                "assets",
                "terrain",
                "performer_blacksmith_minimap_marker_large_world_relief.vhtm");
            using FileStream stream = File.OpenRead(assetPath);
            VisualHeightmapAsset asset = VisualHeightmapBinary.Read(stream);
            ReadHeightRange(asset, out float minHeight, out float maxHeight);

            Assert.Multiple(() =>
            {
                Assert.That(asset.SampleColumns, Is.EqualTo(1025));
                Assert.That(asset.SampleRows, Is.EqualTo(1025));
                Assert.That(asset.InterpolationMode, Is.EqualTo(VisualHeightmapInterpolationMode.TriangleHeightfield));
                Assert.That(asset.Layers[0].Name, Is.EqualTo("relief"));
                Assert.That(maxHeight - minHeight, Is.GreaterThan(19_500), "The minimap marker showcase surface must keep the authored 3x relief for visual acceptance.");
            });
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_ProducesAuthoredPerformerMarkersForFullMapAndZoom()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);
            int expectedMarkers = ReadBlacksmithMetadataInt(engine, MinimapMarkerShowcaseTotalMetadataKey);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            Assert.That(queue.Count, Is.EqualTo(expectedMarkers), "Large world minimap showcase must enqueue the authored marker ball batch.");

            WaitForMinimapMarkerBalls(engine, expectedMarkers, maxFrames: 240);

            RemoveMapEntityFromMinimapMarkerBalls(engine);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
            Assert.That(CountMinimapMarkerBallsWithMapEntity(engine), Is.EqualTo(0), "This test strips spawning map ownership to prove it is not the minimap API.");

            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            int markerDefinitionId = definitions.GetId(PerformerBlacksmithShowcaseIds.MinimapMarkerBallDefinitionId);
            Assert.That(markerDefinitionId, Is.GreaterThan(0));
            AssertMinimapMarkerBallAuthoring(definitions.Get(markerDefinitionId));
            Assert.That(CountRootPerformers(engine, markerDefinitionId), Is.EqualTo(expectedMarkers));

            MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("Core MinimapRuntime missing.");
            MinimapMarkerBuffer markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);
            MinimapDebugSnapshot snapshot = minimap.CaptureDebugSnapshot();

            Assert.That(engine.CurrentMapSession?.MapConfig.Boards, Has.Count.EqualTo(1));
            Assert.That(engine.CurrentMapSession!.MapConfig.Boards[0].SpatialType, Is.EqualTo("Grid"));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].WidthInMacroTiles, Is.EqualTo(256));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].HeightInMacroTiles, Is.EqualTo(256));
            Assert.That(markerBuffer.Count, Is.EqualTo(expectedMarkers));
            Assert.That(markerBuffer.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(CountOrientedMarkers(markerBuffer), Is.EqualTo(expectedMarkers));
            Assert.That(screenMarkers.Count, Is.EqualTo(expectedMarkers));
            Assert.That(screenMarkers.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(screenMarkers.BucketCount, Is.GreaterThan(0));
            Assert.That(SumScreenMarkerBucketCounts(screenMarkers), Is.EqualTo(expectedMarkers));
            Assert.That(CountOrientedScreenMarkers(screenMarkers), Is.EqualTo(expectedMarkers));
            Assert.That(minimap.MarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(minimap.VisibleMarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.Preset, Is.EqualTo(MinimapPreset.RtsFullMap));
            Assert.That(snapshot.MarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.VisibleMarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.VisibleMarkers.Count, Is.GreaterThan(0));
            Assert.That(snapshot.VisibleMarkers.Count, Is.LessThan(expectedMarkers));

            Dictionary<int, Vector2> beforeScreenMarkers = CaptureScreenMarkerPositions(screenMarkers);
            minimap.ApplyWheelZoom(1f);
            minimap.Refresh(engine, markerBuffer, screenMarkers);
            SelectCommonZoomStableScreenMarkerPair(
                beforeScreenMarkers,
                screenMarkers,
                out int stableIdA,
                out int stableIdB,
                out float beforeDistance,
                out float afterDistance);
            Assert.That(afterDistance, Is.GreaterThan(beforeDistance * 1.05f), "Zooming in must increase screen distance between fixed world markers.");
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_SubmitsVisibleWorldSpheres()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);
            int expectedMarkers = ReadBlacksmithMetadataInt(engine, MinimapMarkerShowcaseTotalMetadataKey);

            WaitForMinimapMarkerBalls(engine, expectedMarkers, maxFrames: 240);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 4);

            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            int markerDefinitionId = definitions.GetId(PerformerBlacksmithShowcaseIds.MinimapMarkerBallDefinitionId);
            PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            PrimitiveDrawBuffer snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            StableDrawCache stableDrawCache = engine.GetService(CoreServiceKeys.PresentationStableDrawCache)
                ?? throw new InvalidOperationException("PresentationStableDrawCache missing.");
            PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");

            CountMinimapMarkerBallVisualRows(
                engine,
                markerDefinitionId,
                out int ownerVisualRows,
                out int ownerPayloadRows,
                out int ownerPayloadTransformSyncRows,
                out int ownerPerformerPlaneSyncRows,
                out int ownerPerformerFacingSyncRows,
                out int ownerVisibleRows,
                out int rootPerformerRows,
                out int rootTransformSyncRows,
                out int rootOwnerPayloadTransformSyncRows,
                out int rootStaticRows,
                out int rootDirtyRows,
                out int rootOwnerCullVisibleRows,
                out int rootStableVisualPresentRows);
            int primitiveMarkerRows = CountMarkerDefinitionRows(primitives, markerDefinitionId);
            int snapshotMarkerRows = CountMarkerDefinitionRows(snapshot, markerDefinitionId);
            Vector2 expectedCameraTargetM = ResolveDefaultCameraTargetMeters(engine);
            AssertVisibleMarkerWorldPayloads(primitives, markerDefinitionId, expectedCameraTargetM, out int sphereRows, out int orientationRows);
            CountMinimapMarkerBallMovementRows(
                engine,
                out int markerMovementRows,
                out int dynamicWorkerTagRows,
                out int staticTransformRows,
                out int movedRows,
                out float maxDisplacementCm,
                out int sampledRows,
                out int finiteFacingRows);

            Assert.Multiple(() =>
            {
                Assert.That(ownerVisualRows, Is.EqualTo(expectedMarkers), "Every minimap marker ball owner must carry the production VisualTransform path.");
                Assert.That(ownerPayloadRows, Is.EqualTo(expectedMarkers), "Every minimap marker ball owner must be linked to its performer payload for hot culling.");
                Assert.That(ownerPayloadTransformSyncRows, Is.EqualTo(expectedMarkers), "Every minimap marker ball owner payload must opt into the production single-root transform sync lane.");
                Assert.That(ownerPerformerPlaneSyncRows, Is.EqualTo(expectedMarkers), "Every moving marker owner must be the transform SSOT consumed by its performer.");
                Assert.That(ownerPerformerFacingSyncRows, Is.EqualTo(expectedMarkers), "Every moving marker owner facing must be the facing SSOT consumed by its performer.");
                Assert.That(rootPerformerRows, Is.EqualTo(expectedMarkers), "Every authored ball entity must create the authored MinimapMarker performer.");
                Assert.That(rootTransformSyncRows, Is.EqualTo(expectedMarkers), "Every marker performer must stay on the production transform sync tick path.");
                Assert.That(rootOwnerPayloadTransformSyncRows, Is.EqualTo(expectedMarkers), "The large batch path must keep the explicit owner-payload transform sync marker.");
                Assert.That(rootStaticRows, Is.EqualTo(0), "Marker balls are movable performer visuals, not static StableDrawCache impostors.");
                Assert.That(markerMovementRows, Is.EqualTo(expectedMarkers), "Every marker ball must opt into the dedicated movement tag.");
                Assert.That(dynamicWorkerTagRows, Is.EqualTo(0), "Marker balls must not reuse the dynamic worker movement tag.");
                Assert.That(staticTransformRows, Is.EqualTo(0), "Marker balls must stay on the movable VisualTransform/heightmap path.");
                Assert.That(movedRows, Is.EqualTo(expectedMarkers), "All marker balls must move through the generic world-position performer chain.");
                Assert.That(maxDisplacementCm, Is.GreaterThan(3f), "Marker balls must advance through the configured slow movement path and be readable over sustained frames.");
                Assert.That(sampledRows, Is.EqualTo(expectedMarkers), "Marker balls must sample the visual heightmap so the scene, not the minimap, shows terrain relief.");
                Assert.That(finiteFacingRows, Is.EqualTo(expectedMarkers), "Marker balls must expose finite 2D facing for primitive and minimap orientation.");
                Assert.That(sphereRows, Is.GreaterThan(0), "The world view must submit visible red sphere rows.");
                Assert.That(orientationRows, Is.GreaterThan(0), "The world view must submit visible asymmetric primitive rows so facing is readable.");
                Assert.That(rootOwnerCullVisibleRows, Is.GreaterThan(0), BuildMinimapWorldVisualDiagnostics(
                    ownerVisualRows,
                    ownerPayloadRows,
                    ownerVisibleRows,
                    rootPerformerRows,
                    rootStaticRows,
                    rootDirtyRows,
                    rootOwnerCullVisibleRows,
                    rootStableVisualPresentRows,
                    stableDrawCache.Count,
                    primitives.Count,
                    primitives.StaticMeshLaneItemCount,
                    primitiveMarkerRows,
                    sphereRows,
                    orientationRows,
                    snapshot.Count,
                    snapshot.StaticMeshLaneItemCount,
                    snapshotMarkerRows,
                    timings.VisibleEntitiesLastFrame));
                Assert.That(rootStableVisualPresentRows, Is.EqualTo(0), BuildMinimapWorldVisualDiagnostics(
                    ownerVisualRows,
                    ownerPayloadRows,
                    ownerVisibleRows,
                    rootPerformerRows,
                    rootStaticRows,
                    rootDirtyRows,
                    rootOwnerCullVisibleRows,
                    rootStableVisualPresentRows,
                    stableDrawCache.Count,
                    primitives.Count,
                    primitives.StaticMeshLaneItemCount,
                    primitiveMarkerRows,
                    sphereRows,
                    orientationRows,
                    snapshot.Count,
                    snapshot.StaticMeshLaneItemCount,
                    snapshotMarkerRows,
                    timings.VisibleEntitiesLastFrame));
                Assert.That(primitiveMarkerRows, Is.GreaterThan(0), BuildMinimapWorldVisualDiagnostics(
                    ownerVisualRows,
                    ownerPayloadRows,
                    ownerVisibleRows,
                    rootPerformerRows,
                    rootStaticRows,
                    rootDirtyRows,
                    rootOwnerCullVisibleRows,
                    rootStableVisualPresentRows,
                    stableDrawCache.Count,
                    primitives.Count,
                    primitives.StaticMeshLaneItemCount,
                    primitiveMarkerRows,
                    sphereRows,
                    orientationRows,
                    snapshot.Count,
                    snapshot.StaticMeshLaneItemCount,
                    snapshotMarkerRows,
                    timings.VisibleEntitiesLastFrame));
            });
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_PerformerForwardOrientationMatchesWorldPrimitive()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);
            int expectedMarkers = ReadBlacksmithMetadataInt(engine, MinimapMarkerShowcaseTotalMetadataKey);

            WaitForMinimapMarkerBalls(engine, expectedMarkers, maxFrames: 240);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 90);

            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            int markerDefinitionId = definitions.GetId(PerformerBlacksmithShowcaseIds.MinimapMarkerBallDefinitionId);
            Assert.That(markerDefinitionId, Is.GreaterThan(0));
            int minimapMarkerSlot = FindRequiredMinimapMarkerSlot(definitions.Get(markerDefinitionId));

            MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            Assert.That(markers.Count, Is.EqualTo(expectedMarkers));

            int checkedRows = 0;
            var query = new QueryDescription().WithAll<PerformerState, PerformerWorldRotation>();
            engine.World.Query(in query, (ref PerformerState state, ref PerformerWorldRotation rotation) =>
            {
                if (state.DefId != markerDefinitionId || checkedRows >= 32)
                {
                    return;
                }

                Assert.That(WorldPlane2D.TryExtractFacingRadFromVisualYRotation(rotation.Value, out float expectedFacing), Is.True);

                int markerStableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, minimapMarkerSlot);
                Assert.That(TryFindMarkerOrientation(markers, markerStableId, out float markerOrientation), Is.True);
                Assert.That(WorldPlane2D.AngleDistanceRad(markerOrientation, expectedFacing), Is.LessThan(0.0005f));

                checkedRows++;
            });

            Assert.That(checkedRows, Is.EqualTo(32), "The moving minimap marker performers must project their own world rotation, matching the 3D orientation primitive.");
        }

        [Test]
        public void Benchmark_MinimapMarkerLargeWorldShowcase_ThirtyThousandHotPath_WritesReport()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);
            int expectedMarkers = ReadBlacksmithMetadataInt(engine, MinimapMarkerShowcaseTotalMetadataKey);

            PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");
            timings.SystemBreakdownEnabled = true;

            WaitForMinimapMarkerBalls(engine, expectedMarkers, maxFrames: 240);

            MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("Core MinimapRuntime missing.");
            MinimapMarkerBuffer markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            Assert.That(queue.Count, Is.EqualTo(0), "30k marker benchmark should measure steady-state hot path after spawn queue drains.");

            int width = Math.Max(1, engine.MergedConfig?.WindowWidth ?? 1280);
            int height = Math.Max(1, engine.MergedConfig?.WindowHeight ?? 720);
            using var renderer = new SkiaOverlayRenderer();
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var scene = new PresentationOverlayScene(64);

            for (int frame = 0; frame < MinimapHotPathWarmupFrames; frame++)
            {
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                RenderMinimapMarkersForBenchmark(scene, renderer, surface, screenMarkers);
            }

            double[] tickMs = new double[MinimapHotPathMeasuredFrames];
            double[] presentationMs = new double[MinimapHotPathMeasuredFrames];
            double[] simulationMs = new double[MinimapHotPathMeasuredFrames];
            double[] markerCollectMs = new double[MinimapHotPathMeasuredFrames];
            double[] projectionMs = new double[MinimapHotPathMeasuredFrames];
            double[] skiaMarkerBuildMs = new double[MinimapHotPathMeasuredFrames];
            double[] skiaMarkerDrawMs = new double[MinimapHotPathMeasuredFrames];
            double[] skiaRenderMs = new double[MinimapHotPathMeasuredFrames];
            double[] terrainHeightSyncMs = new double[MinimapHotPathMeasuredFrames];
            double[] primitiveRenderMs = new double[MinimapHotPathMeasuredFrames];
            string[] presentationTop1Names = new string[MinimapHotPathMeasuredFrames];
            string[] presentationTop2Names = new string[MinimapHotPathMeasuredFrames];
            string[] presentationTop3Names = new string[MinimapHotPathMeasuredFrames];
            double[] presentationTop1Ms = new double[MinimapHotPathMeasuredFrames];
            double[] presentationTop2Ms = new double[MinimapHotPathMeasuredFrames];
            double[] presentationTop3Ms = new double[MinimapHotPathMeasuredFrames];
            int[] markerCounts = new int[MinimapHotPathMeasuredFrames];
            int[] screenMarkerCounts = new int[MinimapHotPathMeasuredFrames];
            int[] bucketCounts = new int[MinimapHotPathMeasuredFrames];
            int[] orientationBucketCounts = new int[MinimapHotPathMeasuredFrames];
            int[] droppedMarkerCounts = new int[MinimapHotPathMeasuredFrames];
            int[] droppedScreenMarkerCounts = new int[MinimapHotPathMeasuredFrames];
            long[] allocatedBytes = new long[MinimapHotPathMeasuredFrames];

            for (int frame = 0; frame < MinimapHotPathMeasuredFrames; frame++)
            {
                long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                long tickStart = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                tickMs[frame] = ElapsedMs(tickStart);

                long renderStart = Stopwatch.GetTimestamp();
                RenderMinimapMarkersForBenchmark(scene, renderer, surface, screenMarkers);
                skiaRenderMs[frame] = ElapsedMs(renderStart);
                allocatedBytes[frame] = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocBefore);

                presentationMs[frame] = timings.LastPresentationMs;
                simulationMs[frame] = timings.LastSimulationMs;
                markerCollectMs[frame] = timings.LastPerformerMinimapMarkerMs;
                projectionMs[frame] = timings.LastMinimapProjectionMs;
                terrainHeightSyncMs[frame] = timings.LastTerrainHeightSyncMs;
                primitiveRenderMs[frame] = timings.LastPrimitiveRenderMs;
                presentationTop1Names[frame] = timings.LastPresentationTopSystem1Name;
                presentationTop2Names[frame] = timings.LastPresentationTopSystem2Name;
                presentationTop3Names[frame] = timings.LastPresentationTopSystem3Name;
                presentationTop1Ms[frame] = timings.LastPresentationTopSystem1Ms;
                presentationTop2Ms[frame] = timings.LastPresentationTopSystem2Ms;
                presentationTop3Ms[frame] = timings.LastPresentationTopSystem3Ms;
                skiaMarkerBuildMs[frame] = renderer.LastMinimapMarkerBatchBuildMs;
                skiaMarkerDrawMs[frame] = renderer.LastMinimapMarkerBatchDrawMs;
                markerCounts[frame] = markerBuffer.Count;
                screenMarkerCounts[frame] = screenMarkers.Count;
                bucketCounts[frame] = renderer.LastMinimapMarkerBatchBucketCount;
                orientationBucketCounts[frame] = renderer.LastMinimapMarkerOrientationBatchBucketCount;
                droppedMarkerCounts[frame] = markerBuffer.DroppedSinceClear;
                droppedScreenMarkerCounts[frame] = screenMarkers.DroppedSinceClear;
            }

            var result = new MinimapHotPathBenchmarkResult(
                expectedMarkers,
                markerBuffer.Count,
                screenMarkers.Count,
                screenMarkers.BucketCount,
                CountOrientedMarkers(markerBuffer),
                CountOrientedScreenMarkers(screenMarkers),
                markerBuffer.DroppedTotal,
                screenMarkers.DroppedTotal,
                tickMs,
                presentationMs,
                simulationMs,
                markerCollectMs,
                projectionMs,
                skiaMarkerBuildMs,
                skiaMarkerDrawMs,
                skiaRenderMs,
                terrainHeightSyncMs,
                primitiveRenderMs,
                presentationTop1Names,
                presentationTop2Names,
                presentationTop3Names,
                presentationTop1Ms,
                presentationTop2Ms,
                presentationTop3Ms,
                markerCounts,
                screenMarkerCounts,
                bucketCounts,
                orientationBucketCounts,
                droppedMarkerCounts,
                droppedScreenMarkerCounts,
                allocatedBytes);

            string artifactDir = Path.Combine(
                PerformerBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "minimap-marker-large-world-hotpath");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            File.WriteAllText(reportPath, BuildMinimapHotPathReport(result));
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.Multiple(() =>
            {
                Assert.That(result.MarkerCount, Is.EqualTo(expectedMarkers));
                Assert.That(result.ScreenMarkerCount, Is.EqualTo(expectedMarkers));
                Assert.That(result.OrientedMarkerCount, Is.EqualTo(expectedMarkers));
                Assert.That(result.OrientedScreenMarkerCount, Is.EqualTo(expectedMarkers));
                Assert.That(Max(result.DroppedMarkerCounts), Is.EqualTo(0));
                Assert.That(Max(result.DroppedScreenMarkerCounts), Is.EqualTo(0));
                Assert.That(result.BucketCount, Is.GreaterThan(0));
                Assert.That(Max(result.MarkerCounts), Is.EqualTo(expectedMarkers));
                Assert.That(Max(result.ScreenMarkerCounts), Is.EqualTo(expectedMarkers));
            });
        }

        private static void AssertVisibleMarkerWorldPayloads(
            PrimitiveDrawBuffer primitives,
            int markerDefinitionId,
            Vector2 expectedCameraTargetM,
            out int sphereRows,
            out int orientationRows)
        {
            int checkedRows = 0;
            int checkedOrientationRows = 0;
            sphereRows = 0;
            orientationRows = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.TemplateId != markerDefinitionId || !item.RenderPath.IsStaticInstanceLane())
                {
                    continue;
                }

                bool isRed = item.Color.X >= 0.85f && item.Color.Y <= 0.35f && item.Color.Z <= 0.2f;
                bool sphereScale =
                    item.Scale.X is >= 2.5f and <= 14f &&
                    item.Scale.Y is >= 2.5f and <= 14f &&
                    item.Scale.Z is >= 2.5f and <= 14f &&
                    MathF.Abs(item.Scale.X - item.Scale.Z) <= MathF.Max(0.5f, item.Scale.X * 0.12f);
                bool orientationScale =
                    item.Scale.X is >= 1.2f and <= 8f &&
                    item.Scale.Y is >= 0.15f and <= 2.4f &&
                    item.Scale.Z is >= 0.3f and <= 4f &&
                    item.Scale.X >= item.Scale.Z * 2.0f;

                if (sphereScale && isRed)
                {
                    sphereRows++;
                    checkedRows++;
                    Assert.That(MathF.Abs(item.Position.X - expectedCameraTargetM.X), Is.LessThanOrEqualTo(140f), "The acceptance cluster must place visible balls near the configured default camera target.");
                    Assert.That(MathF.Abs(item.Position.Z - expectedCameraTargetM.Y), Is.LessThanOrEqualTo(140f), "The acceptance cluster must place visible balls near the configured default camera target.");
                }
                else if (orientationScale && isRed)
                {
                    orientationRows++;
                    checkedOrientationRows++;
                    Assert.That(MathF.Abs(item.Position.X - expectedCameraTargetM.X), Is.LessThanOrEqualTo(180f), "Orientation primitives must stay attached to visible marker balls near the configured default camera target.");
                    Assert.That(MathF.Abs(item.Position.Z - expectedCameraTargetM.Y), Is.LessThanOrEqualTo(180f), "Orientation primitives must stay attached to visible marker balls near the configured default camera target.");
                    Assert.That(float.IsFinite(item.Rotation.X), Is.True);
                    Assert.That(float.IsFinite(item.Rotation.Y), Is.True);
                    Assert.That(float.IsFinite(item.Rotation.Z), Is.True);
                    Assert.That(float.IsFinite(item.Rotation.W), Is.True);
                    Vector3 forward = Vector3.Transform(Vector3.UnitX, Quaternion.Normalize(item.Rotation));
                    Assert.That(float.IsFinite(forward.X) && float.IsFinite(forward.Z), Is.True);
                    Assert.That(MathF.Abs(forward.X) + MathF.Abs(forward.Z), Is.GreaterThan(0.99f), "Orientation primitive must use performer local +X as the authored forward axis.");
                }

                if (checkedRows >= 8 && checkedOrientationRows >= 8)
                {
                    return;
                }
            }

            Assert.Fail($"Expected visible minimap marker sphere and orientation primitive rows. sphereRows={sphereRows}, orientationRows={orientationRows}.");
        }

        private static void AssertMinimapMarkerBallAuthoring(PerformerDefinition definition)
        {
            Assert.That(definition.Behaviors, Has.Length.GreaterThanOrEqualTo(4));
            BehaviorSlot bodyPrimitive = FindRequiredBehavior(definition, BehaviorKind.AssetBinding, slot =>
                slot.AssetBinding.AssetKind == AssetKind.Mesh &&
                slot.AssetBinding.LocalOffset.X == 0f &&
                slot.AssetBinding.LocalScale.X >= 2.5f &&
                MathF.Abs(slot.AssetBinding.LocalScale.X - slot.AssetBinding.LocalScale.Z) <= MathF.Max(0.5f, slot.AssetBinding.LocalScale.X * 0.12f));
            Assert.That(bodyPrimitive.AssetBinding.LocalOffset.Y, Is.GreaterThan(0f), "World marker sphere height must be authored on the visual mesh so Grounding can use the owner-backed heightmap fast path.");

            BehaviorSlot orientationPrimitive = FindRequiredBehavior(definition, BehaviorKind.AssetBinding, slot =>
                slot.AssetBinding.AssetKind == AssetKind.Mesh &&
                slot.AssetBinding.LocalOffset.X > 0f &&
                slot.AssetBinding.LocalScale.X > slot.AssetBinding.LocalScale.Z * 2f);
            Assert.That(orientationPrimitive.Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(orientationPrimitive.AssetBinding.AssetKind, Is.EqualTo(AssetKind.Mesh));
            Assert.That(orientationPrimitive.AssetBinding.LocalOffset.X, Is.GreaterThan(0f), "World orientation primitive must sit on local +X, matching FacingDirection 0 = +X.");
            Assert.That(MathF.Abs(orientationPrimitive.AssetBinding.LocalOffset.Z), Is.LessThanOrEqualTo(0.001f));
            Assert.That(orientationPrimitive.AssetBinding.LocalScale.X, Is.GreaterThan(orientationPrimitive.AssetBinding.LocalScale.Z * 2f), "World orientation primitive must use local +X as its long authored forward axis.");
            Assert.That(orientationPrimitive.AssetBinding.LocalRotation, Is.EqualTo(Quaternion.Identity));

            BehaviorSlot minimapMarker = FindRequiredBehavior(definition, BehaviorKind.MinimapMarker, static _ => true);
            Assert.That(minimapMarker.Kind, Is.EqualTo(BehaviorKind.MinimapMarker));
            Assert.That(minimapMarker.MinimapMarker.OrientationMode, Is.EqualTo(MinimapMarkerOrientationMode.PerformerForward));
            Assert.That(minimapMarker.MinimapMarker.OrientationParamKey, Is.EqualTo(-1));
            Assert.That(minimapMarker.MinimapMarker.OrientationOffsetRad, Is.EqualTo(0f), "Minimap marker orientation must consume the same performer forward as the 3D primitive without authored correction.");
            Assert.That(minimapMarker.MinimapMarker.OrientationLengthPx, Is.GreaterThan(0f));
        }

        private static void ReadHeightRange(VisualHeightmapAsset asset, out float minHeight, out float maxHeight)
        {
            minHeight = float.PositiveInfinity;
            maxHeight = float.NegativeInfinity;
            if (asset.UsesRawUInt16Samples)
            {
                for (int i = 0; i < asset.HeightSamplesRaw.Length; i++)
                {
                    float sample = asset.SampleScale.Decode(asset.HeightSamplesRaw[i]);
                    minHeight = MathF.Min(minHeight, sample);
                    maxHeight = MathF.Max(maxHeight, sample);
                }

                return;
            }

            for (int i = 0; i < asset.HeightSamplesCm.Length; i++)
            {
                float sample = asset.HeightSamplesCm[i];
                minHeight = MathF.Min(minHeight, sample);
                maxHeight = MathF.Max(maxHeight, sample);
            }
        }

        private static int FindRequiredMinimapMarkerSlot(PerformerDefinition definition)
        {
            return FindRequiredBehavior(definition, BehaviorKind.MinimapMarker, static _ => true).SlotIndex;
        }

        private static BehaviorSlot FindRequiredBehavior(
            PerformerDefinition definition,
            BehaviorKind kind,
            Predicate<BehaviorSlot> predicate)
        {
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                BehaviorSlot slot = definition.Behaviors[i];
                if (slot.Kind == kind && predicate(slot))
                {
                    return slot;
                }
            }

            throw new InvalidOperationException($"Performer '{definition.Key}' must declare a {kind} behavior matching the test contract.");
        }

        private static Vector2 ResolveDefaultCameraTargetMeters(GameEngine engine)
        {
            float targetXCm = engine.CurrentMapSession?.MapConfig?.DefaultCamera?.TargetXCm
                ?? engine.AuthorityCamera().State.TargetCm.X;
            float targetYCm = engine.CurrentMapSession?.MapConfig?.DefaultCamera?.TargetYCm
                ?? engine.AuthorityCamera().State.TargetCm.Y;
            return new Vector2(targetXCm * 0.01f, targetYCm * 0.01f);
        }

        private static int ReadBlacksmithMetadataInt(GameEngine engine, string key)
        {
            return engine.CurrentMapSession!.MapConfig.Metadata[MetadataSectionKey]![key]!.GetValue<int>();
        }

        [Test]
        public void DynamicWorkerBenchmark_DefaultStartupScatter_StaysInsideVisualHeightmapAndSamplesTerrain()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkMapId,
                frames: 0);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            int expectedWorkers = ReadBlacksmithMetadataInt(engine, DynamicWorkerBenchmarkTotalMetadataKey);
            Assert.That(queue.Count, Is.EqualTo(expectedWorkers));
            WaitForDynamicWorkers(engine, expectedWorkers, maxFrames: 180);

            IVisualHeightmap heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
                ?? throw new InvalidOperationException("VisualHeightmap missing.");
            IVisualHeightmapRenderSource renderSource = heightmap as IVisualHeightmapRenderSource
                ?? throw new InvalidOperationException("VisualHeightmap render source missing.");

            int count = 0;
            int sampled = 0;
            int centralSpawnCount = 0;
            float centralHalfWidthCm = (renderSource.Bounds.Right - renderSource.Bounds.Left) * 0.125f;
            float centralHalfHeightCm = (renderSource.Bounds.Bottom - renderSource.Bounds.Top) * 0.125f;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm, VisualHeightmapSampleState>();
            engine.World.Query(in query, (ref Name name, ref WorldPositionCm position, ref VisualHeightmapSampleState state) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.Ordinal))
                {
                    return;
                }

                count++;
                float x = position.Value.X.ToFloat();
                float y = position.Value.Y.ToFloat();
                Assert.That(x, Is.InRange((float)renderSource.Bounds.Left, (float)renderSource.Bounds.Right));
                Assert.That(y, Is.InRange((float)renderSource.Bounds.Top, (float)renderSource.Bounds.Bottom));
                Assert.That(heightmap.TrySampleHeightCm(x, y, out float heightCm), Is.True);
                Assert.That(float.IsFinite(heightCm), Is.True);
                if (MathF.Abs(x) <= centralHalfWidthCm && MathF.Abs(y) <= centralHalfHeightCm)
                {
                    centralSpawnCount++;
                }

                if (state.Sampled != 0)
                {
                    sampled++;
                }
            });

            Assert.That(count, Is.EqualTo(expectedWorkers));
            Assert.That(sampled, Is.EqualTo(count));
            Assert.That(centralSpawnCount, Is.GreaterThan(0), "Dynamic worker scatter must fill the VisualHeightmap area, not leave a ring-shaped center hole.");
        }

        private static DynamicWorkerBenchmarkResult RunScenario(int count)
        {
            string? previousTotal = Environment.GetEnvironmentVariable(DynamicWorkerBenchmarkTotalEnvKey);
            Environment.SetEnvironmentVariable(DynamicWorkerBenchmarkTotalEnvKey, count.ToString(CultureInfo.InvariantCulture));
            try
            {
                return RunScenarioCore(count);
            }
            finally
            {
                Environment.SetEnvironmentVariable(DynamicWorkerBenchmarkTotalEnvKey, previousTotal);
            }
        }

        private static DynamicWorkerBenchmarkResult RunScenarioCore(int count)
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkMapId,
                frames: 0);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");
            timings.SystemBreakdownEnabled = true;
            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            SkinnedVisualBatchBuffer skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");
            PresentationEventStream events = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream missing.");
            PerformerCommandBuffer commands = engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
                ?? throw new InvalidOperationException("PerformerCommandBuffer missing.");

            int dynamicWorkerDefId = definitions.GetId(PerformerBlacksmithShowcaseIds.DynamicWorkerDefinitionId);
            int attachmentDefId = definitions.GetId("blacksmith_dynamic_worker_tool_attachment");
            if (dynamicWorkerDefId <= 0)
            {
                throw new InvalidOperationException("Dynamic worker performer definition is not registered.");
            }

            int enqueued = queue.Count;
            double enqueueMs = 0d;
            Assert.That(enqueued, Is.EqualTo(count), "Dynamic worker benchmark must be queued by the production MapLoaded path.");

            long firstTickStart = Stopwatch.GetTimestamp();
            WaitForDynamicWorkers(engine, count, maxFrames: 240);
            double firstTickMs = ElapsedMs(firstTickStart);
            double initTotalTickMs = timings.LastTotalTickMs;
            double initSimulationMs = timings.LastSimulationMs;
            double initPresentationMs = timings.LastPresentationMs;
            double initCameraCullingMs = timings.LastCameraCullingMs;
            double initCameraCullEntityMs = timings.LastCameraCullingEntityProcessMs;
            double initCameraCullPerformerMs = timings.LastCameraCullingPerformerSyncMs;
            double initBehaviorMs = timings.LastPerformerBehaviorMs;
            double initAnimatorMs = timings.LastPerformerAnimatorMs;
            double initTransformSyncMs = timings.LastPerformerEntityTransformSyncMs;
            double initEmitMs = timings.LastPerformerEmitMs;
            double initEmitDirtyProcessMs = timings.LastPerformerEmitDirtyProcessMs;
            int initEmitDirtyCount = timings.PerformerEmitDirtyCountLastFrame;
            double initEmitRetainedProcessMs = timings.LastPerformerEmitRetainedProcessMs;
            int initEmitRetainedCount = timings.PerformerEmitRetainedCountLastFrame;
            double initRequestFlushMs = timings.LastPresentationRequestFlushMs;
            string initPresentationTop1 = timings.LastPresentationTopSystem1Name;
            double initPresentationTop1Ms = timings.LastPresentationTopSystem1Ms;
            string initSimulationTop1 = timings.LastSimulationTopSystem1Name;
            double initSimulationTop1Ms = timings.LastSimulationTopSystem1Ms;

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
            }

            double[] tickMs = new double[MeasuredFrames];
            double[] simulationMs = new double[MeasuredFrames];
            double[] presentationMs = new double[MeasuredFrames];
            double[] cameraCullingMs = new double[MeasuredFrames];
            double[] cameraCullEntityMs = new double[MeasuredFrames];
            double[] cameraCullPerformerMs = new double[MeasuredFrames];
            double[] behaviorMs = new double[MeasuredFrames];
            double[] animatorMs = new double[MeasuredFrames];
            double[] transformSyncMs = new double[MeasuredFrames];
            double[] emitMs = new double[MeasuredFrames];
            double[] emitDirtyProcessMs = new double[MeasuredFrames];
            int[] emitDirtyCounts = new int[MeasuredFrames];
            double[] emitRetainedProcessMs = new double[MeasuredFrames];
            int[] emitRetainedCounts = new int[MeasuredFrames];
            double[] requestFlushMs = new double[MeasuredFrames];
            bool[] directSkinnedFrames = new bool[MeasuredFrames];
            string[] presentationTop1Names = new string[MeasuredFrames];
            double[] presentationTop1Ms = new double[MeasuredFrames];
            string[] simulationTop1Names = new string[MeasuredFrames];
            double[] simulationTop1Ms = new double[MeasuredFrames];
            int[] skinnedCounts = new int[MeasuredFrames];

            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                long tickStart = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                tickMs[frame] = ElapsedMs(tickStart);
                simulationMs[frame] = timings.LastSimulationMs;
                presentationMs[frame] = timings.LastPresentationMs;
                cameraCullingMs[frame] = timings.LastCameraCullingMs;
                cameraCullEntityMs[frame] = timings.LastCameraCullingEntityProcessMs;
                cameraCullPerformerMs[frame] = timings.LastCameraCullingPerformerSyncMs;
                behaviorMs[frame] = timings.LastPerformerBehaviorMs;
                animatorMs[frame] = timings.LastPerformerAnimatorMs;
                transformSyncMs[frame] = timings.LastPerformerEntityTransformSyncMs;
                emitMs[frame] = timings.LastPerformerEmitMs;
                emitDirtyProcessMs[frame] = timings.LastPerformerEmitDirtyProcessMs;
                emitDirtyCounts[frame] = timings.PerformerEmitDirtyCountLastFrame;
                emitRetainedProcessMs[frame] = timings.LastPerformerEmitRetainedProcessMs;
                emitRetainedCounts[frame] = timings.PerformerEmitRetainedCountLastFrame;
                requestFlushMs[frame] = timings.LastPresentationRequestFlushMs;
                directSkinnedFrames[frame] = skinned.DirectWrittenThisFrame;
                presentationTop1Names[frame] = timings.LastPresentationTopSystem1Name;
                presentationTop1Ms[frame] = timings.LastPresentationTopSystem1Ms;
                simulationTop1Names[frame] = timings.LastSimulationTopSystem1Name;
                simulationTop1Ms[frame] = timings.LastSimulationTopSystem1Ms;
                skinnedCounts[frame] = skinned.Count;
            }

            CountDynamicWorkers(
                engine,
                dynamicWorkerDefId,
                attachmentDefId,
                out int entityCount,
                out int rootPerformerCount,
                out int attachmentPerformerCount,
                out int activeAnimatorCount,
                out int groundedPerformerCount,
                out int attachedPerformerCount,
                out int movedEntityCount,
                out int gpuSkinnedCount,
                out int walkingSkinnedStateCount);

            return new DynamicWorkerBenchmarkResult(
                count,
                enqueued,
                entityCount,
                rootPerformerCount,
                attachmentPerformerCount,
                skinned.Count,
                gpuSkinnedCount,
                activeAnimatorCount,
                groundedPerformerCount,
                attachedPerformerCount,
                movedEntityCount,
                walkingSkinnedStateCount,
                CountTrue(directSkinnedFrames),
                enqueueMs,
                firstTickMs,
                initTotalTickMs,
                initSimulationMs,
                initPresentationMs,
                initCameraCullingMs,
                initCameraCullEntityMs,
                initCameraCullPerformerMs,
                initBehaviorMs,
                initAnimatorMs,
                initTransformSyncMs,
                initEmitMs,
                initEmitDirtyProcessMs,
                initEmitDirtyCount,
                initEmitRetainedProcessMs,
                initEmitRetainedCount,
                initRequestFlushMs,
                initPresentationTop1,
                initPresentationTop1Ms,
                initSimulationTop1,
                initSimulationTop1Ms,
                tickMs,
                simulationMs,
                presentationMs,
                cameraCullingMs,
                cameraCullEntityMs,
                cameraCullPerformerMs,
                behaviorMs,
                animatorMs,
                transformSyncMs,
                emitMs,
                emitDirtyProcessMs,
                emitDirtyCounts,
                emitRetainedProcessMs,
                emitRetainedCounts,
                requestFlushMs,
                directSkinnedFrames,
                presentationTop1Names,
                presentationTop1Ms,
                simulationTop1Names,
                simulationTop1Ms,
                skinnedCounts,
                events.DroppedTotal,
                commands.DroppedTotal,
                skinned.DroppedTotal);
        }

        private static void WaitForDynamicWorkers(GameEngine engine, int expectedCount, int maxFrames)
        {
            int count = 0;
            for (int frame = 0; frame < maxFrames; frame++)
            {
                count = CountDynamicWorkerEntityRows(engine);
                if (count == expectedCount)
                {
                    return;
                }

                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
            }

            RuntimeEntitySpawnQueue? queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
            Assert.Fail($"Timed out waiting for {expectedCount} dynamic workers. Current={count}, spawnQueue={queue?.Count ?? -1}.");
        }

        private static void WaitForMinimapMarkerBalls(GameEngine engine, int expectedCount, int maxFrames)
        {
            int entityCount = 0;
            int markerCount = 0;
            MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("Core MinimapRuntime missing.");
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();

            for (int frame = 0; frame < maxFrames; frame++)
            {
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                entityCount = CountMinimapMarkerBallEntityRows(engine);
                markerCount = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)?.Count ?? 0;
                if (entityCount == expectedCount && markerCount == expectedCount)
                {
                    return;
                }
            }

            RuntimeEntitySpawnQueue? queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
            Assert.Fail($"Timed out waiting for {expectedCount} minimap marker balls. Entities={entityCount}, markers={markerCount}, spawnQueue={queue?.Count ?? -1}.");
        }

        private static int CountDynamicWorkerEntityRows(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountMinimapMarkerBallEntityRows(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountMinimapMarkerBallsWithMapEntity(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            engine.World.Query(in query, (ref Name name, ref MapEntity _) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static void RemoveMapEntityFromMinimapMarkerBalls(GameEngine engine)
        {
            var entities = new List<Entity>();
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            engine.World.Query(in query, (Entity entity, ref Name name, ref MapEntity _) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    entities.Add(entity);
                }
            });

            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (engine.World.IsAlive(entity) && engine.World.Has<MapEntity>(entity))
                {
                    engine.World.Remove<MapEntity>(entity);
                }
            }
        }

        private static int CountRootPerformers(GameEngine engine, int definitionId)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in query, (ref PerformerState state) =>
            {
                if (state.DefId == definitionId)
                {
                    total++;
                }
            });

            return total;
        }

        private static void CountMinimapMarkerBallVisualRows(
            GameEngine engine,
            int markerDefinitionId,
            out int ownerVisualRows,
            out int ownerPayloadRows,
            out int ownerPayloadTransformSyncRows,
            out int ownerPerformerPlaneSyncRows,
            out int ownerPerformerFacingSyncRows,
            out int ownerVisibleRows,
            out int rootPerformerRows,
            out int rootTransformSyncRows,
            out int rootOwnerPayloadTransformSyncRows,
            out int rootStaticRows,
            out int rootDirtyRows,
            out int rootOwnerCullVisibleRows,
            out int rootStableVisualPresentRows)
        {
            int ownersWithVisual = 0;
            int ownersWithPayload = 0;
            int ownersWithPayloadTransformSync = 0;
            int ownersWithSyncedPerformerPlane = 0;
            int ownersWithSyncedPerformerFacing = 0;
            int ownersVisible = 0;
            var ownerQuery = new QueryDescription().WithAll<Name, WorldPositionCm, VisualTransform, CullState, FacingDirection>();
            engine.World.Query(in ownerQuery, (Entity entity, ref Name name, ref WorldPositionCm worldPosition, ref CullState cull, ref FacingDirection facing) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    return;
                }

                ownersWithVisual++;
                if (engine.World.Has<PresentationOwnerHasPerformerPayload>(entity))
                {
                    ownersWithPayload++;
                    ref readonly PresentationOwnerHasPerformerPayload payload = ref engine.World.Get<PresentationOwnerHasPerformerPayload>(entity);
                    if (payload.SingleRootTransformSync != 0)
                    {
                        ownersWithPayloadTransformSync++;
                    }

                    Entity performer = payload.SingleRootPerformer;
                    if (performer != Entity.Null &&
                        engine.World.IsAlive(performer) &&
                        engine.World.Has<PerformerState>(performer) &&
                        engine.World.Get<PerformerState>(performer).DefId == markerDefinitionId)
                    {
                        if (engine.World.Has<PerformerWorldPlanePosition>(performer))
                        {
                            Vector2 ownerPlaneCm = worldPosition.Value.ToVector2();
                            Vector2 performerPlaneCm = engine.World.Get<PerformerWorldPlanePosition>(performer).ValueCm;
                            if (Vector2.DistanceSquared(ownerPlaneCm, performerPlaneCm) <= 0.0001f)
                            {
                                ownersWithSyncedPerformerPlane++;
                            }
                        }

                        if (engine.World.Has<PerformerWorldFacing>(performer))
                        {
                            PerformerWorldFacing performerFacing = engine.World.Get<PerformerWorldFacing>(performer);
                            if (performerFacing.HasValue != 0 &&
                                WorldPlane2D.AngleDistanceRad(performerFacing.AngleRad, facing.AngleRad) <= 0.0005f)
                            {
                                ownersWithSyncedPerformerFacing++;
                            }
                        }
                    }
                }

                if (cull.IsVisible)
                {
                    ownersVisible++;
                }
            });

            int roots = 0;
            int transformSyncRoots = 0;
            int ownerPayloadTransformSyncRoots = 0;
            int staticRoots = 0;
            int dirtyRoots = 0;
            int ownerCullVisibleRoots = 0;
            int stableVisualPresentRoots = 0;
            var performerQuery = new QueryDescription().WithAll<PerformerState, PerformerCullState, PerformerEmitCache>();
            engine.World.Query(in performerQuery, (Entity entity, ref PerformerState state, ref PerformerCullState cull, ref PerformerEmitCache emitCache) =>
            {
                if (state.DefId != markerDefinitionId)
                {
                    return;
                }

                roots++;
                if (engine.World.Has<PerfTransformSyncTick>(entity))
                {
                    transformSyncRoots++;
                }

                if (engine.World.Has<PerfOwnerPayloadTransformSync>(entity))
                {
                    ownerPayloadTransformSyncRoots++;
                }

                if (engine.World.Has<PerfStaticStableVisual>(entity))
                {
                    staticRoots++;
                }

                if (emitCache.StaticDirty != 0)
                {
                    dirtyRoots++;
                }

                if (cull.OwnerCullVisible)
                {
                    ownerCullVisibleRoots++;
                }

                if (emitCache.StableVisualPresent != 0)
                {
                    stableVisualPresentRoots++;
                }
            });

            ownerVisualRows = ownersWithVisual;
            ownerPayloadRows = ownersWithPayload;
            ownerPayloadTransformSyncRows = ownersWithPayloadTransformSync;
            ownerPerformerPlaneSyncRows = ownersWithSyncedPerformerPlane;
            ownerPerformerFacingSyncRows = ownersWithSyncedPerformerFacing;
            ownerVisibleRows = ownersVisible;
            rootPerformerRows = roots;
            rootTransformSyncRows = transformSyncRoots;
            rootOwnerPayloadTransformSyncRows = ownerPayloadTransformSyncRoots;
            rootStaticRows = staticRoots;
            rootDirtyRows = dirtyRoots;
            rootOwnerCullVisibleRows = ownerCullVisibleRoots;
            rootStableVisualPresentRows = stableVisualPresentRoots;
        }

        private static void CountMinimapMarkerBallMovementRows(
            GameEngine engine,
            out int markerMovementRows,
            out int dynamicWorkerTagRows,
            out int staticTransformRows,
            out int movedRows,
            out float maxDisplacementCm,
            out int sampledRows,
            out int finiteFacingRows)
        {
            int markerMovement = 0;
            int dynamicWorkerTags = 0;
            int staticTransforms = 0;
            int moved = 0;
            float maxDisplacement = 0f;
            int sampled = 0;
            int finiteFacing = 0;

            var query = new QueryDescription()
                .WithAll<Name, WorldPositionCm, PreviousWorldPositionCm, VisualHeightmapSampleState, FacingDirection>();
            engine.World.Query(
                in query,
                (Entity entity, ref Name name, ref WorldPositionCm position, ref PreviousWorldPositionCm previous, ref VisualHeightmapSampleState sampleState, ref FacingDirection facing) =>
                {
                    if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (engine.World.Has<MinimapMarkerBallMovementTag>(entity))
                    {
                        markerMovement++;
                    }

                    if (engine.World.Has<DynamicWorkerCrowdTag>(entity))
                    {
                        dynamicWorkerTags++;
                    }

                    if (engine.World.Has<PresentationStaticTransform>(entity))
                    {
                        staticTransforms++;
                    }

                    if (position.Value != previous.Value)
                    {
                        moved++;
                        float dx = position.Value.X.ToFloat() - previous.Value.X.ToFloat();
                        float dy = position.Value.Y.ToFloat() - previous.Value.Y.ToFloat();
                        float displacement = MathF.Sqrt((dx * dx) + (dy * dy));
                        if (float.IsFinite(displacement))
                        {
                            maxDisplacement = MathF.Max(maxDisplacement, displacement);
                        }
                    }

                    if (sampleState.Sampled != 0)
                    {
                        sampled++;
                    }

                    if (float.IsFinite(facing.AngleRad))
                    {
                        finiteFacing++;
                    }
                });

            markerMovementRows = markerMovement;
            dynamicWorkerTagRows = dynamicWorkerTags;
            staticTransformRows = staticTransforms;
            movedRows = moved;
            maxDisplacementCm = maxDisplacement;
            sampledRows = sampled;
            finiteFacingRows = finiteFacing;
        }

        private static int CountMarkerDefinitionRows(PrimitiveDrawBuffer buffer, int markerDefinitionId)
        {
            int count = 0;
            foreach (ref readonly PrimitiveDrawItem item in buffer.GetSpan())
            {
                if (item.TemplateId == markerDefinitionId && item.RenderPath.IsStaticInstanceLane())
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOrientedMarkers(MinimapMarkerBuffer markers)
        {
            int total = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                if ((markers.GetFlags(i) & MinimapMarkerFlags.HasOrientation) != 0u &&
                    float.IsFinite(markers.GetOrientationRad(i)) &&
                    markers.GetOrientationLengthPx(i) > 0f)
                {
                    total++;
                }
            }

            return total;
        }

        private static bool TryFindMarkerOrientation(MinimapMarkerBuffer markers, int stableId, out float orientationRad)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers.GetStableId(i) == stableId &&
                    (markers.GetFlags(i) & MinimapMarkerFlags.HasOrientation) != 0u)
                {
                    orientationRad = markers.GetOrientationRad(i);
                    return true;
                }
            }

            orientationRad = 0f;
            return false;
        }

        private static int CountOrientedScreenMarkers(MinimapScreenMarkerBuffer screenMarkers)
        {
            int total = 0;
            for (int i = 0; i < screenMarkers.Count; i++)
            {
                if ((screenMarkers.GetFlags(i) & MinimapMarkerFlags.HasOrientation) != 0u &&
                    float.IsFinite(screenMarkers.GetOrientationRad(i)) &&
                    screenMarkers.GetOrientationLengthPx(i) > 0f)
                {
                    total++;
                }
            }

            return total;
        }

        private static int SumScreenMarkerBucketCounts(MinimapScreenMarkerBuffer screenMarkers)
        {
            int total = 0;
            for (int i = 0; i < screenMarkers.BucketCount; i++)
            {
                total += screenMarkers.GetBucket(i).Count;
            }

            return total;
        }

        private static void RenderMinimapMarkersForBenchmark(
            PresentationOverlayScene scene,
            SkiaOverlayRenderer renderer,
            SKSurface surface,
            MinimapScreenMarkerBuffer screenMarkers)
        {
            scene.BeginBuild();
            scene.SetTopMostMinimapMarkers(screenMarkers);
            scene.EndBuild();
            surface.Canvas.Clear(SKColors.Transparent);
            renderer.ResetFrameStats();
            renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        }

        private static string BuildMinimapHotPathReport(in MinimapHotPathBenchmarkResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Minimap Marker Large World Hot Path Benchmark");
            sb.AppendLine();
            sb.AppendLine("- map: `performer_blacksmith_minimap_marker_large_world_showcase`");
            sb.AppendLine("- source: authored performer `MinimapMarker` behavior");
            sb.AppendLine("- world: 256x256 chunks, visual heightmap scene, 30k moving marker balls");
            sb.AppendLine("- path: `PerformerWorldPlanePosition/PerformerWorldFacing -> MinimapMarkerBuffer -> MinimapScreenMarkerBuffer -> SkiaOverlayRenderer`");
            sb.AppendLine("- secondary path: none; no `Name`, `Team`, or `MapEntity` minimap signal");
            sb.AppendLine("- timing note: `Avg Tick FPS` is computed from the engine tick only; offscreen CPU Skia marker raster is measured separately and is not part of that FPS.");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Markers | Screen | Buckets | Oriented | Screen Oriented | Drop markers | Drop screen | Avg Tick | P95 Tick | Max Tick | Avg Tick FPS | Avg collect | Avg projection | Avg Skia build | Avg offscreen CPU Skia draw | Avg offscreen CPU Skia total | Avg alloc |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.MarkerCount} | {r.ScreenMarkerCount} | {r.BucketCount} | {r.OrientedMarkerCount} | {r.OrientedScreenMarkerCount} | {r.MarkerDroppedTotal} | {r.ScreenMarkerDroppedTotal} | {Average(r.TickMs):F4} ms | {Percentile(r.TickMs, 0.95):F4} ms | {Max(r.TickMs):F4} ms | {Fps(Average(r.TickMs)):F1} | {Average(r.MarkerCollectMs):F4} ms | {Average(r.ProjectionMs):F4} ms | {Average(r.SkiaMarkerBuildMs):F4} ms | {Average(r.SkiaMarkerDrawMs):F4} ms | {Average(r.SkiaRenderMs):F4} ms | {Average(r.AllocatedBytes):F0} B |");
            sb.AppendLine();
            sb.AppendLine("## Detail");
            sb.AppendLine();
            sb.AppendLine("| Metric | Avg | P95 | Max |");
            sb.AppendLine("|---|---:|---:|---:|");
            AppendMetric(sb, "total tick", r.TickMs);
            AppendMetric(sb, "presentation", r.PresentationMs);
            AppendMetric(sb, "simulation", r.SimulationMs);
            AppendMetric(sb, "performer minimap collect", r.MarkerCollectMs);
            AppendMetric(sb, "minimap projection", r.ProjectionMs);
            AppendMetric(sb, "Skia marker batch build", r.SkiaMarkerBuildMs);
            AppendMetric(sb, "offscreen CPU Skia marker draw", r.SkiaMarkerDrawMs);
            AppendMetric(sb, "offscreen CPU Skia render wrapper", r.SkiaRenderMs);
            AppendMetric(sb, "terrain height sync", r.TerrainHeightSyncMs);
            AppendMetric(sb, "primitive render diag", r.PrimitiveRenderMs);
            sb.AppendLine();
            sb.AppendLine("## Presentation Top Systems");
            sb.AppendLine();
            sb.AppendLine("| Rank | Most frequent system | Avg when ranked |");
            sb.AppendLine("|---:|---|---:|");
            AppendTopSystemMetric(sb, 1, r.PresentationTop1Names, r.PresentationTop1Ms);
            AppendTopSystemMetric(sb, 2, r.PresentationTop2Names, r.PresentationTop2Ms);
            AppendTopSystemMetric(sb, 3, r.PresentationTop3Names, r.PresentationTop3Ms);
            sb.AppendLine();
            sb.AppendLine("## Counts");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- marker count avg/max: `{Average(r.MarkerCounts):F1}` / `{Max(r.MarkerCounts)}`");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- screen marker count avg/max: `{Average(r.ScreenMarkerCounts):F1}` / `{Max(r.ScreenMarkerCounts)}`");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- Skia bucket count avg/max: `{Average(r.BucketCounts):F1}` / `{Max(r.BucketCounts)}`");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- orientation bucket count avg/max: `{Average(r.OrientationBucketCounts):F1}` / `{Max(r.OrientationBucketCounts)}`");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- per-frame marker drops max: `{Max(r.DroppedMarkerCounts)}`; screen drops max: `{Max(r.DroppedScreenMarkerCounts)}`");
            return sb.ToString();
        }

        private static void AppendMetric(StringBuilder sb, string name, double[] values)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {Average(values):F4} ms | {Percentile(values, 0.95):F4} ms | {Max(values):F4} ms |");
        }

        private static void AppendTopSystemMetric(StringBuilder sb, int rank, string[] names, double[] values)
        {
            string name = MostFrequentNonEmpty(names);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {rank} | {name} | {AverageForName(names, values, name):F4} ms |");
        }

        private static string BuildMinimapWorldVisualDiagnostics(
            int ownerVisualRows,
            int ownerPayloadRows,
            int ownerVisibleRows,
            int rootPerformerRows,
            int rootStaticRows,
            int rootDirtyRows,
            int rootOwnerCullVisibleRows,
            int rootStableVisualPresentRows,
            int stableCacheCount,
            int primitiveCount,
            int primitiveStaticCount,
            int primitiveMarkerRows,
            int primitiveSphereRows,
            int primitiveOrientationRows,
            int snapshotCount,
            int snapshotStaticCount,
            int snapshotMarkerRows,
            int visibleEntities)
        {
            return "Minimap marker showcase must submit real visible world sphere visuals; " +
                   $"owners visual/payload/visible={ownerVisualRows}/{ownerPayloadRows}/{ownerVisibleRows}, " +
                   $"performers root/static/dirty/ownerVisible/stablePresent={rootPerformerRows}/{rootStaticRows}/{rootDirtyRows}/{rootOwnerCullVisibleRows}/{rootStableVisualPresentRows}, " +
                   $"stableCache={stableCacheCount}, primitive total/static/marker/sphere/orientation={primitiveCount}/{primitiveStaticCount}/{primitiveMarkerRows}/{primitiveSphereRows}/{primitiveOrientationRows}, " +
                   $"snapshot total/static/marker={snapshotCount}/{snapshotStaticCount}/{snapshotMarkerRows}, visibleEntities={visibleEntities}.";
        }

        private static Dictionary<int, Vector2> CaptureScreenMarkerPositions(MinimapScreenMarkerBuffer screenMarkers)
        {
            var positions = new Dictionary<int, Vector2>(screenMarkers.Count);
            for (int i = 0; i < screenMarkers.Count; i++)
            {
                positions[screenMarkers.GetStableId(i)] = new Vector2(
                    screenMarkers.GetScreenX(i),
                    screenMarkers.GetScreenY(i));
            }

            return positions;
        }

        private static void SelectCommonZoomStableScreenMarkerPair(
            Dictionary<int, Vector2> beforeScreenMarkers,
            MinimapScreenMarkerBuffer afterScreenMarkers,
            out int stableIdA,
            out int stableIdB,
            out float beforeDistance,
            out float afterDistance)
        {
            Assert.That(beforeScreenMarkers.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(afterScreenMarkers.Count, Is.GreaterThanOrEqualTo(2));
            stableIdA = 0;
            stableIdB = 0;
            beforeDistance = 0f;
            afterDistance = 0f;

            for (int i = 0; i < afterScreenMarkers.Count - 1; i++)
            {
                int idA = afterScreenMarkers.GetStableId(i);
                if (!beforeScreenMarkers.TryGetValue(idA, out Vector2 beforeA))
                {
                    continue;
                }

                Vector2 afterA = new(afterScreenMarkers.GetScreenX(i), afterScreenMarkers.GetScreenY(i));
                for (int j = i + 1; j < afterScreenMarkers.Count; j++)
                {
                    int idB = afterScreenMarkers.GetStableId(j);
                    if (!beforeScreenMarkers.TryGetValue(idB, out Vector2 beforeB))
                    {
                        continue;
                    }

                    float candidateBeforeDistance = Vector2.Distance(beforeA, beforeB);
                    if (candidateBeforeDistance <= beforeDistance)
                    {
                        continue;
                    }

                    Vector2 afterB = new(afterScreenMarkers.GetScreenX(j), afterScreenMarkers.GetScreenY(j));
                    stableIdA = idA;
                    stableIdB = idB;
                    beforeDistance = candidateBeforeDistance;
                    afterDistance = Vector2.Distance(afterA, afterB);
                }
            }

            Assert.That(stableIdA, Is.GreaterThan(0), "The minimap marker showcase must keep at least two authored marker ids visible across zoom.");
            Assert.That(stableIdB, Is.GreaterThan(0), "The minimap marker showcase must keep at least two authored marker ids visible across zoom.");
            Assert.That(beforeDistance, Is.GreaterThan(1f), "The stable marker pair must have measurable pre-zoom separation.");
        }

        private static void CountDynamicWorkerSignalRows(
            GameEngine engine,
            MapId activeMapId,
            out int namePositionRows,
            out int activeMapSignalRows,
            out int otherMapSignalRows)
        {
            int withNamePosition = 0;
            int activeSignals = 0;
            int foreignSignals = 0;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm, MapEntity>();
            engine.World.Query(in query, (ref Name name, ref MapEntity mapEntity) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.Ordinal))
                {
                    return;
                }

                withNamePosition++;
                if (mapEntity.MapId == activeMapId)
                {
                    activeSignals++;
                }
                else
                {
                    foreignSignals++;
                }
            });

            namePositionRows = withNamePosition;
            activeMapSignalRows = activeSignals;
            otherMapSignalRows = foreignSignals;
        }

        private static void CountDynamicWorkers(
            GameEngine engine,
            int definitionId,
            int attachmentDefinitionId,
            out int entityCount,
            out int rootPerformerCount,
            out int attachmentPerformerCount,
            out int activeAnimatorCount,
                out int groundedPerformerCount,
                out int attachedPerformerCount,
                out int movedEntityCount,
                out int gpuSkinnedCount,
                out int walkingSkinnedStateCount)
        {
            int entities = 0;
            int rootPerformers = 0;
            int attachmentPerformers = 0;
            int activeAnimators = 0;
            int groundedPerformers = 0;
            int attachedPerformers = 0;
            int movedEntities = 0;
            int gpuSkinned = 0;
            int walkingSkinned = 0;

            var entityQuery = new QueryDescription().WithAll<Name, WorldPositionCm, PreviousWorldPositionCm>();
            engine.World.Query(in entityQuery, (ref Name name, ref WorldPositionCm position, ref PreviousWorldPositionCm previous) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.Ordinal))
                {
                    return;
                }

                entities++;
                if (position.Value != previous.Value)
                {
                    movedEntities++;
                }
            });

            var performerQuery = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in performerQuery, (Entity entity, ref PerformerState state) =>
            {
                if (state.DefId != definitionId)
                {
                    if (state.DefId == attachmentDefinitionId)
                    {
                        attachmentPerformers++;
                        if (engine.World.Has<PerfHasAttachment>(entity))
                        {
                            attachedPerformers++;
                        }
                    }

                    return;
                }

                rootPerformers++;
                if (engine.World.Has<PerfHasAnimator>(entity))
                {
                    activeAnimators++;
                }

                if (engine.World.Has<PerfHasGrounding>(entity))
                {
                    groundedPerformers++;
                }
                else if (HasOwnerBackedGrounding(engine, in state))
                {
                    groundedPerformers++;
                }

            });

            var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("SkinnedVisualBatchBuffer missing.");
            foreach (ref readonly SkinnedVisualBatchItem item in skinned.GetSpan())
            {
                if (item.TemplateId == definitionId && item.RenderPath == VisualRenderPath.GpuSkinnedInstance)
                {
                    gpuSkinned++;
                    if (item.Animator.GetPrimaryStateIndex() == 42)
                    {
                        walkingSkinned++;
                    }
                }
            }

            entityCount = entities;
            rootPerformerCount = rootPerformers;
            attachmentPerformerCount = attachmentPerformers;
            activeAnimatorCount = activeAnimators;
            groundedPerformerCount = groundedPerformers;
            attachedPerformerCount = attachedPerformers;
            movedEntityCount = movedEntities;
            gpuSkinnedCount = gpuSkinned;
            walkingSkinnedStateCount = walkingSkinned;
        }

        private static bool HasOwnerBackedGrounding(GameEngine engine, in PerformerState state)
        {
            if (state.OwnerEntity == Entity.Null ||
                !engine.World.IsAlive(state.OwnerEntity) ||
                !engine.World.Has<VisualHeightmapSampleState>(state.OwnerEntity))
            {
                return false;
            }

            VisualHeightmapSampleState sampleState = engine.World.Get<VisualHeightmapSampleState>(state.OwnerEntity);
            return sampleState.Sampled != 0;
        }

        private static string BuildReport(DynamicWorkerBenchmarkResult[] results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Performer Dynamic Worker Production Path Benchmark");
            sb.AppendLine();
            sb.AppendLine("- template: `blacksmith_dynamic_worker_entity`");
            sb.AppendLine("- performer rule: `EntitySpawned -> blacksmith_dynamic_worker_actor`");
            sb.AppendLine("- render path: `SkinnedMesh` through production `PresentationRequest`/`StableDrawCache`/`SkinnedVisualBatchBuffer`");
            sb.AppendLine("- animator: `blacksmith.worker.locomotion` packed state");
            sb.AppendLine("- grounding: performer `Grounding` behavior, batched through `PerformerGroundingUtility.ResolveBatch`");
            sb.AppendLine("- attachment: child performer `blacksmith_dynamic_worker_tool_attachment` follows the worker through `Attachment` behavior");
            sb.AppendLine("- movement: mod-owned ECS `DynamicWorkerCrowdMovementSystem`, no fake render data");
            sb.AppendLine();
            sb.AppendLine("## Init");
            sb.AppendLine();
            sb.AppendLine("| Count | Enqueue | First Tick | init Total | init Sim | init Pres | init Culling | init Transform Sync | init Behavior | init Animator | init Emit | init Dirty Emit | dirty count | retained emit | retained count | init Flush | top presentation | top simulation |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            for (int i = 0; i < results.Length; i++)
            {
                DynamicWorkerBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.EnqueueMs:F4} ms | {r.FirstTickMs:F4} ms | {r.InitTotalTickMs:F4} ms | {r.InitSimulationMs:F4} ms | {r.InitPresentationMs:F4} ms | {r.InitCameraCullingMs:F4} ms | {r.InitTransformSyncMs:F4} ms | {r.InitBehaviorMs:F4} ms | {r.InitAnimatorMs:F4} ms | {r.InitEmitMs:F4} ms | {r.InitEmitDirtyProcessMs:F4} ms | {r.InitEmitDirtyCount} | {r.InitEmitRetainedProcessMs:F4} ms | {r.InitEmitRetainedCount} | {r.InitRequestFlushMs:F4} ms | {r.InitPresentationTop1} {r.InitPresentationTop1Ms:F4} ms | {r.InitSimulationTop1} {r.InitSimulationTop1Ms:F4} ms |");
            }

            sb.AppendLine();
            sb.AppendLine("## Stable Tick");
            sb.AppendLine();
            sb.AppendLine("| Count | Entities | Root Performers | Attach Performers | Skinned | Walking State | Animators | Grounded | Attached | Moved | Avg Tick | P95 Tick | Max Tick | Avg FPS | Avg Sim | Avg Pres | Avg Culling | Avg Transform Sync | Avg Behavior | Avg Animator | Avg Emit | Avg Dirty Emit | Avg Retained Emit | Avg Flush | top presentation | top simulation |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            for (int i = 0; i < results.Length; i++)
            {
                DynamicWorkerBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.EntityCount} | {r.RootPerformerCount} | {r.AttachmentPerformerCount} | {r.SkinnedCount} | {r.WalkingSkinnedStateCount} | {r.ActiveAnimatorCount} | {r.GroundedPerformerCount} | {r.AttachedPerformerCount} | {r.MovedEntityCount} | {Average(r.TickMs):F4} ms | {Percentile(r.TickMs, 0.95):F4} ms | {Max(r.TickMs):F4} ms | {Fps(Average(r.TickMs)):F1} | {Average(r.SimulationMs):F4} ms | {Average(r.PresentationMs):F4} ms | {Average(r.CameraCullingMs):F4} ms | {Average(r.TransformSyncMs):F4} ms | {Average(r.BehaviorMs):F4} ms | {Average(r.AnimatorMs):F4} ms | {Average(r.EmitMs):F4} ms | {Average(r.EmitDirtyProcessMs):F4} ms | {Average(r.EmitRetainedProcessMs):F4} ms | {Average(r.RequestFlushMs):F4} ms | {MostFrequentNonEmpty(r.PresentationTop1Names)} {AverageForName(r.PresentationTop1Names, r.PresentationTop1Ms, MostFrequentNonEmpty(r.PresentationTop1Names)):F4} ms | {MostFrequentNonEmpty(r.SimulationTop1Names)} {AverageForName(r.SimulationTop1Names, r.SimulationTop1Ms, MostFrequentNonEmpty(r.SimulationTop1Names)):F4} ms |");
            }

            sb.AppendLine();
            for (int i = 0; i < results.Length; i++)
            {
                DynamicWorkerBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- {r.Count}: avg dirty emit count `{Average(r.EmitDirtyCounts):F1}`, avg retained emit count `{Average(r.EmitRetainedCounts):F1}`, avg skinned count `{Average(r.SkinnedCounts):F1}`, gpu skinned `{r.GpuSkinnedCount}`, direct skinned frames `{r.DirectSkinnedFrameCount}/{MeasuredFrames}`, drops events `{r.EventDrops}` commands `{r.CommandDrops}` skinned `{r.SkinnedDrops}`");
            }

            return sb.ToString();
        }

        private static double ElapsedMs(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

        private static double Average(double[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            double sum = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum / values.Length;
        }

        private static double Average(int[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            long sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum / (double)values.Length;
        }

        private static double Average(long[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            long sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum / (double)values.Length;
        }

        private static int CountTrue(bool[] values)
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    count++;
                }
            }

            return count;
        }

        private static double Percentile(double[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            double[] copy = new double[values.Length];
            Array.Copy(values, copy, values.Length);
            Array.Sort(copy);
            int index = (int)Math.Ceiling((copy.Length - 1) * percentile);
            return copy[index];
        }

        private static double Max(double[] values)
        {
            double max = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        private static int Max(int[] values)
        {
            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        private static string MostFrequentNonEmpty(string[] values)
        {
            string best = string.Empty;
            int bestCount = 0;
            for (int i = 0; i < values.Length; i++)
            {
                string candidate = values[i] ?? string.Empty;
                if (candidate.Length == 0)
                {
                    continue;
                }

                int count = 0;
                for (int j = 0; j < values.Length; j++)
                {
                    if (string.Equals(candidate, values[j], StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                if (count > bestCount)
                {
                    best = candidate;
                    bestCount = count;
                }
            }

            return best;
        }

        private static double AverageForName(string[] names, double[] values, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0d;
            }

            double sum = 0d;
            int count = 0;
            for (int i = 0; i < names.Length && i < values.Length; i++)
            {
                if (!string.Equals(names[i], name, StringComparison.Ordinal))
                {
                    continue;
                }

                sum += values[i];
                count++;
            }

            return count == 0 ? 0d : sum / count;
        }

        private static double Fps(double tickMs)
            => tickMs <= 0d ? 0d : 1000d / tickMs;

        private readonly record struct DynamicWorkerBenchmarkResult(
            int Count,
            int Enqueued,
            int EntityCount,
            int RootPerformerCount,
            int AttachmentPerformerCount,
            int SkinnedCount,
            int GpuSkinnedCount,
            int ActiveAnimatorCount,
            int GroundedPerformerCount,
            int AttachedPerformerCount,
            int MovedEntityCount,
            int WalkingSkinnedStateCount,
            int DirectSkinnedFrameCount,
            double EnqueueMs,
            double FirstTickMs,
            double InitTotalTickMs,
            double InitSimulationMs,
            double InitPresentationMs,
            double InitCameraCullingMs,
            double InitCameraCullEntityMs,
            double InitCameraCullPerformerMs,
            double InitBehaviorMs,
            double InitAnimatorMs,
            double InitTransformSyncMs,
            double InitEmitMs,
            double InitEmitDirtyProcessMs,
            int InitEmitDirtyCount,
            double InitEmitRetainedProcessMs,
            int InitEmitRetainedCount,
            double InitRequestFlushMs,
            string InitPresentationTop1,
            double InitPresentationTop1Ms,
            string InitSimulationTop1,
            double InitSimulationTop1Ms,
            double[] TickMs,
            double[] SimulationMs,
            double[] PresentationMs,
            double[] CameraCullingMs,
            double[] CameraCullEntityMs,
            double[] CameraCullPerformerMs,
            double[] BehaviorMs,
            double[] AnimatorMs,
            double[] TransformSyncMs,
            double[] EmitMs,
            double[] EmitDirtyProcessMs,
            int[] EmitDirtyCounts,
            double[] EmitRetainedProcessMs,
            int[] EmitRetainedCounts,
            double[] RequestFlushMs,
            bool[] DirectSkinnedFrames,
            string[] PresentationTop1Names,
            double[] PresentationTop1Ms,
            string[] SimulationTop1Names,
            double[] SimulationTop1Ms,
            int[] SkinnedCounts,
            int EventDrops,
            int CommandDrops,
            int SkinnedDrops);

        private readonly record struct MinimapHotPathBenchmarkResult(
            int ExpectedMarkers,
            int MarkerCount,
            int ScreenMarkerCount,
            int BucketCount,
            int OrientedMarkerCount,
            int OrientedScreenMarkerCount,
            int MarkerDroppedTotal,
            int ScreenMarkerDroppedTotal,
            double[] TickMs,
            double[] PresentationMs,
            double[] SimulationMs,
            double[] MarkerCollectMs,
            double[] ProjectionMs,
            double[] SkiaMarkerBuildMs,
            double[] SkiaMarkerDrawMs,
            double[] SkiaRenderMs,
            double[] TerrainHeightSyncMs,
            double[] PrimitiveRenderMs,
            string[] PresentationTop1Names,
            string[] PresentationTop2Names,
            string[] PresentationTop3Names,
            double[] PresentationTop1Ms,
            double[] PresentationTop2Ms,
            double[] PresentationTop3Ms,
            int[] MarkerCounts,
            int[] ScreenMarkerCounts,
            int[] BucketCounts,
            int[] OrientationBucketCounts,
            int[] DroppedMarkerCounts,
            int[] DroppedScreenMarkerCounts,
            long[] AllocatedBytes);
    }
}
