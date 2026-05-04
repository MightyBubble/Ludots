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
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformerDynamicWorkerBenchmarkTests
    {
        private const int WarmupFrames = 8;
        private const int MeasuredFrames = 90;
        private const string DynamicWorkerBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_DYNAMIC_WORKER_BENCHMARK_TOTAL";
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
        public void DynamicWorkerBenchmarkMap_DeclaresProductionVisualHeightmapAndDefaultTotal()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkMapId,
                frames: 2);

            Assert.That(engine.CurrentMapSession?.MapConfig.VisualHeightmapAsset, Is.EqualTo("assets/terrain/performer_blacksmith_dynamic_worker_hills.vhtm"));
            Assert.That(
                engine.CurrentMapSession!.MapConfig.Metadata["performerBlacksmith"]!["dynamicWorkerBenchmarkTotal"]!.GetValue<int>(),
                Is.EqualTo(PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkDefaultTotal));
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
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].WidthInTiles, Is.EqualTo(256));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].HeightInTiles, Is.EqualTo(256));
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

            Assert.That(engine.CurrentMapSession?.MapConfig.VisualHeightmapAsset, Is.EqualTo("assets/terrain/performer_blacksmith_dynamic_worker_large_world.vhtm"));
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
                engine.GameSession.Camera.State.TargetCm.X,
                engine.GameSession.Camera.State.TargetCm.Y,
                out float expectedHeightCm), Is.True);
            Assert.That(engine.GameSession.Camera.State.TargetHeightCm, Is.EqualTo(expectedHeightCm + definition.TargetHeightOffsetCm).Within(0.01f));
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_ProducesAuthoredPerformerMarkersForFullMapAndZoom()
        {
            const int expectedMarkers = PerformerBlacksmithShowcaseIds.MinimapMarkerShowcaseDefaultTotal;
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);

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
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].WidthInTiles, Is.EqualTo(256));
            Assert.That(engine.CurrentMapSession.MapConfig.Boards[0].HeightInTiles, Is.EqualTo(256));
            Assert.That(markerBuffer.Count, Is.EqualTo(expectedMarkers));
            Assert.That(markerBuffer.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(CountOrientedMarkers(markerBuffer), Is.EqualTo(expectedMarkers));
            Assert.That(screenMarkers.Count, Is.EqualTo(expectedMarkers));
            Assert.That(screenMarkers.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(CountOrientedScreenMarkers(screenMarkers), Is.EqualTo(expectedMarkers));
            Assert.That(minimap.MarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(minimap.VisibleMarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.Preset, Is.EqualTo(MinimapPreset.RtsFullMap));
            Assert.That(snapshot.MarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.VisibleMarkerCount, Is.EqualTo(expectedMarkers));
            Assert.That(snapshot.VisibleMarkers.Count, Is.EqualTo(expectedMarkers));

            SelectZoomStableScreenMarkerPair(minimap, screenMarkers, out int stableIdA, out int stableIdB, out float beforeDistance);
            minimap.ApplyWheelZoom(1f);
            minimap.Refresh(engine, markerBuffer, screenMarkers);
            float afterDistance = DistanceBetweenStableScreenMarkers(screenMarkers, stableIdA, stableIdB);
            Assert.That(afterDistance, Is.GreaterThan(beforeDistance * 1.05f), "Zooming in must increase screen distance between fixed world markers.");
        }

        [Test]
        public void MinimapMarkerLargeWorldShowcase_SubmitsVisibleWorldSpheres()
        {
            const int expectedMarkers = PerformerBlacksmithShowcaseIds.MinimapMarkerShowcaseDefaultTotal;
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId,
                frames: 0);

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
                out int ownerVisibleRows,
                out int rootPerformerRows,
                out int rootStaticRows,
                out int rootDirtyRows,
                out int rootOwnerCullVisibleRows,
                out int rootStableVisualPresentRows);
            int primitiveMarkerRows = CountMarkerDefinitionRows(primitives, markerDefinitionId);
            int snapshotMarkerRows = CountMarkerDefinitionRows(snapshot, markerDefinitionId);
            AssertVisibleMarkerSpherePayloads(primitives, markerDefinitionId);

            Assert.Multiple(() =>
            {
                Assert.That(ownerVisualRows, Is.EqualTo(expectedMarkers), "Every minimap marker ball owner must carry the production VisualTransform path.");
                Assert.That(ownerPayloadRows, Is.EqualTo(expectedMarkers), "Every minimap marker ball owner must be linked to its performer payload for hot culling.");
                Assert.That(rootPerformerRows, Is.EqualTo(expectedMarkers), "Every authored ball entity must create the authored MinimapMarker performer.");
                Assert.That(rootStaticRows, Is.EqualTo(expectedMarkers), "Authored static sphere performers must use the event-driven StableDrawCache lane.");
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
                    snapshot.Count,
                    snapshot.StaticMeshLaneItemCount,
                    snapshotMarkerRows,
                    timings.VisibleEntitiesLastFrame));
                Assert.That(rootStableVisualPresentRows, Is.GreaterThan(0), BuildMinimapWorldVisualDiagnostics(
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
                    snapshot.Count,
                    snapshot.StaticMeshLaneItemCount,
                    snapshotMarkerRows,
                    timings.VisibleEntitiesLastFrame));
            });
        }

        private static void AssertVisibleMarkerSpherePayloads(PrimitiveDrawBuffer primitives, int markerDefinitionId)
        {
            int checkedRows = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.TemplateId != markerDefinitionId || !item.RenderPath.IsStaticInstanceLane())
                {
                    continue;
                }

                checkedRows++;
                Assert.That(item.Scale.X, Is.InRange(6f, 14f), "Visible minimap showcase spheres must be large enough to read without swallowing the camera.");
                Assert.That(item.Scale.Y, Is.InRange(6f, 14f), "Visible minimap showcase spheres must be large enough to read without swallowing the camera.");
                Assert.That(item.Scale.Z, Is.InRange(6f, 14f), "Visible minimap showcase spheres must be large enough to read without swallowing the camera.");
                Assert.That(item.Color.X, Is.GreaterThanOrEqualTo(0.85f), "Visible minimap showcase spheres must contrast against the Raylib clear color.");
                Assert.That(item.Color.Y, Is.LessThanOrEqualTo(0.35f), "Visible minimap showcase spheres must contrast against the Raylib clear color.");
                Assert.That(item.Color.Z, Is.LessThanOrEqualTo(0.2f), "Visible minimap showcase spheres must contrast against the Raylib clear color.");
                Assert.That(MathF.Abs(item.Position.X), Is.LessThanOrEqualTo(92f), "The acceptance cluster must place visible balls near the default camera target.");
                Assert.That(MathF.Abs(item.Position.Z), Is.LessThanOrEqualTo(92f), "The acceptance cluster must place visible balls near the default camera target.");
                if (checkedRows >= 8)
                {
                    return;
                }
            }

            Assert.Fail("No visible minimap marker sphere payload rows were available for visual authoring validation.");
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
            Assert.That(queue.Count, Is.EqualTo(PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkDefaultTotal));
            WaitForDynamicWorkers(engine, PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkDefaultTotal, maxFrames: 180);

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

            Assert.That(count, Is.EqualTo(PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkDefaultTotal));
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
            var entities = new List<Entity>(PerformerBlacksmithShowcaseIds.MinimapMarkerShowcaseDefaultTotal);
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
            out int ownerVisibleRows,
            out int rootPerformerRows,
            out int rootStaticRows,
            out int rootDirtyRows,
            out int rootOwnerCullVisibleRows,
            out int rootStableVisualPresentRows)
        {
            int ownersWithVisual = 0;
            int ownersWithPayload = 0;
            int ownersVisible = 0;
            var ownerQuery = new QueryDescription().WithAll<Name, VisualTransform, CullState>();
            engine.World.Query(in ownerQuery, (Entity entity, ref Name name, ref CullState cull) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    return;
                }

                ownersWithVisual++;
                if (engine.World.Has<PresentationOwnerHasPerformerPayload>(entity))
                {
                    ownersWithPayload++;
                }

                if (cull.IsVisible)
                {
                    ownersVisible++;
                }
            });

            int roots = 0;
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
            ownerVisibleRows = ownersVisible;
            rootPerformerRows = roots;
            rootStaticRows = staticRoots;
            rootDirtyRows = dirtyRoots;
            rootOwnerCullVisibleRows = ownerCullVisibleRoots;
            rootStableVisualPresentRows = stableVisualPresentRoots;
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
            int snapshotCount,
            int snapshotStaticCount,
            int snapshotMarkerRows,
            int visibleEntities)
        {
            return "Minimap marker showcase must submit real visible world sphere visuals; " +
                   $"owners visual/payload/visible={ownerVisualRows}/{ownerPayloadRows}/{ownerVisibleRows}, " +
                   $"performers root/static/dirty/ownerVisible/stablePresent={rootPerformerRows}/{rootStaticRows}/{rootDirtyRows}/{rootOwnerCullVisibleRows}/{rootStableVisualPresentRows}, " +
                   $"stableCache={stableCacheCount}, primitive total/static/marker={primitiveCount}/{primitiveStaticCount}/{primitiveMarkerRows}, " +
                   $"snapshot total/static/marker={snapshotCount}/{snapshotStaticCount}/{snapshotMarkerRows}, visibleEntities={visibleEntities}.";
        }

        private static void SelectZoomStableScreenMarkerPair(
            MinimapRuntime minimap,
            MinimapScreenMarkerBuffer screenMarkers,
            out int stableIdA,
            out int stableIdB,
            out float distance)
        {
            Assert.That(screenMarkers.Count, Is.GreaterThanOrEqualTo(2));
            float safeMinX = minimap.FieldX + (minimap.FieldSize * 0.2f);
            float safeMaxX = minimap.FieldX + (minimap.FieldSize * 0.8f);
            float safeMinY = minimap.FieldY + (minimap.FieldSize * 0.2f);
            float safeMaxY = minimap.FieldY + (minimap.FieldSize * 0.8f);

            stableIdA = 0;
            stableIdB = 0;
            float bestDistanceSquared = 0f;
            for (int i = 0; i < screenMarkers.Count - 1; i++)
            {
                float ax = screenMarkers.GetScreenX(i);
                float ay = screenMarkers.GetScreenY(i);
                if (ax < safeMinX || ax > safeMaxX || ay < safeMinY || ay > safeMaxY)
                {
                    continue;
                }

                for (int j = i + 1; j < screenMarkers.Count; j++)
                {
                    float bx = screenMarkers.GetScreenX(j);
                    float by = screenMarkers.GetScreenY(j);
                    if (bx < safeMinX || bx > safeMaxX || by < safeMinY || by > safeMaxY)
                    {
                        continue;
                    }

                    float dx = bx - ax;
                    float dy = by - ay;
                    float distanceSquared = (dx * dx) + (dy * dy);
                    if (distanceSquared > bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        stableIdA = screenMarkers.GetStableId(i);
                        stableIdB = screenMarkers.GetStableId(j);
                    }
                }
            }

            Assert.That(stableIdA, Is.GreaterThan(0), "The minimap marker showcase must place at least two markers near the RTS minimap center so zoom can be verified on stable ids.");
            Assert.That(stableIdB, Is.GreaterThan(0), "The minimap marker showcase must place at least two markers near the RTS minimap center so zoom can be verified on stable ids.");
            distance = MathF.Sqrt(bestDistanceSquared);
        }

        private static float DistanceBetweenStableScreenMarkers(MinimapScreenMarkerBuffer screenMarkers, int stableIdA, int stableIdB)
        {
            int indexA = FindScreenMarkerIndex(screenMarkers, stableIdA);
            int indexB = FindScreenMarkerIndex(screenMarkers, stableIdB);
            float dx = screenMarkers.GetScreenX(indexB) - screenMarkers.GetScreenX(indexA);
            float dy = screenMarkers.GetScreenY(indexB) - screenMarkers.GetScreenY(indexA);
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static int FindScreenMarkerIndex(MinimapScreenMarkerBuffer screenMarkers, int stableId)
        {
            for (int i = 0; i < screenMarkers.Count; i++)
            {
                if (screenMarkers.GetStableId(i) == stableId)
                {
                    return i;
                }
            }

            Assert.Fail($"Expected minimap screen marker stableId {stableId} to remain visible after zoom.");
            return -1;
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
    }
}
