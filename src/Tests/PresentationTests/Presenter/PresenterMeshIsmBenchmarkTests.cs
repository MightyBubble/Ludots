using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterMeshIsmBenchmarkTests
    {
        private const string BenchmarkTemplateId = "blacksmith_mesh_benchmark_entity";
        private const string BenchmarkPresenterId = "blacksmith_mesh_benchmark_ism";
        private const string BenchmarkEntityName = "BlacksmithMeshBenchmark";
        private const int MeasuredTickFrames = 60;
        private const int StableWarmupFrames = 8;
        private const int SpawnSettleFrameBudget = 12;

        private static readonly int[] Counts = { 3_000, 10_000, 30_000 };

        [Test]
        public void BenchmarkMeshAssetBinding_DoesNotOwnTransformOrGrounding()
        {
            string repoRoot = PresenterBlacksmithShowcaseTestHarness.FindRepoRoot();
            string presentersPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "presenter_blacksmith",
                "PresenterBlacksmithShowcaseMod",
                "assets",
                "Presentation",
                "presenters.json");

            JsonArray presenters = JsonNode.Parse(File.ReadAllText(presentersPath))?.AsArray()
                ?? throw new InvalidOperationException("presenters.json must contain a top-level array.");
            JsonObject benchmark = FindDefinition(presenters, BenchmarkPresenterId);
            JsonObject assetBinding = benchmark["behaviors"]?.AsArray()[0]?["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException("Benchmark presenter must have slot 0 assetBinding.");

            Assert.That(assetBinding.ContainsKey("localOffset"), Is.False, "AssetBinding must not own local transform offsets.");
            Assert.That(assetBinding.ContainsKey("localScale"), Is.False, "AssetBinding must not own local transform scale.");
            Assert.That(assetBinding.ContainsKey("grounding"), Is.False, "Grounding belongs to transform/grounding behavior, not AssetBinding.");
            Assert.That(assetBinding.ContainsKey("groundingOffset"), Is.False, "Grounding belongs to transform/grounding behavior, not AssetBinding.");
        }

        [Test]
        public void BenchmarkMeshAssetBinding_DefaultSwapParam_UsesBaseAsset()
        {
            using GameEngine engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            PrimitiveDrawBuffer snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");

            int benchmarkPresenterDefId = definitions.GetId(BenchmarkPresenterId);
            int northIntactMeshId = meshes.GetId("blacksmith.building.north.intact");
            int ruinedMeshId = meshes.GetId("blacksmith.building.ruined");

            EnqueueBenchmarkTemplates(queue, engine.CurrentMapSession?.MapId ?? default, 1, out _);

            PrimitiveDrawItem? emitted = null;
            for (int frame = 0; frame < SpawnSettleFrameBudget && !emitted.HasValue; frame++)
            {
                PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                foreach (ref readonly PrimitiveDrawItem item in snapshot.GetSpan())
                {
                    if (item.TemplateId == benchmarkPresenterDefId)
                    {
                        emitted = item;
                        break;
                    }
                }
            }

            Assert.That(emitted.HasValue, Is.True, "Benchmark presenter should emit one production primitive.");
            Assert.That(emitted!.Value.MeshAssetId, Is.EqualTo(northIntactMeshId), "The benchmark presenter must author an explicit default assetSwap value for the base asset.");
            Assert.That(emitted.Value.MeshAssetId, Is.Not.EqualTo(ruinedMeshId), "The default assetSwap value must not be interpreted as a ruined state.");
        }

        [Test]
        public void Benchmark_TemplatePresenterMesh_IntoRaylibIsmBridge_WritesReport()
        {
            var results = new MeshIsmBenchmarkResult[Counts.Length];
            for (int i = 0; i < Counts.Length; i++)
            {
                results[i] = RunScenario(Counts[i]);
            }

            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "presenter-mesh-ism-production-path");
            Directory.CreateDirectory(artifactDir);

            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            File.WriteAllText(reportPath, BuildReport(results));
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult result = results[i];
                Assert.That(result.Enqueued, Is.EqualTo(result.Count), $"{result.Count}: enqueue count mismatch.");
                Assert.That(result.EntityCount, Is.EqualTo(result.Count), $"{result.Count}: spawned entity count mismatch.");
                Assert.That(result.PresenterCount, Is.EqualTo(result.Count), $"{result.Count}: presenter count mismatch.");
                Assert.That(result.PrimitiveCount, Is.EqualTo(result.Count), $"{result.Count}: primitive count mismatch.");
                Assert.That(result.IsmPrimitiveCount, Is.EqualTo(result.Count), $"{result.Count}: ISM primitive count mismatch.");
                Assert.That(result.RaylibActiveBindings, Is.EqualTo(result.Count), $"{result.Count}: raylib bridge binding count mismatch.");
                Assert.That(result.RaylibBucketCount, Is.EqualTo(1), $"{result.Count}: benchmark should collapse to one ISM lane/bucket.");
                Assert.That(result.TickFrames, Is.EqualTo(MeasuredTickFrames), $"{result.Count}: tick sample count mismatch.");
                Assert.That(result.PrimitiveDrops, Is.EqualTo(0), $"{result.Count}: primitive buffer dropped items.");
                Assert.That(result.EventDrops, Is.EqualTo(0), $"{result.Count}: presentation event stream dropped items.");
                Assert.That(result.CommandDrops, Is.EqualTo(0), $"{result.Count}: presenter command buffer dropped items.");
            }
        }

        private static MeshIsmBenchmarkResult RunScenario(int count)
        {
            using GameEngine engine = PresenterBlacksmithShowcaseTestHarness.CreateEngine();
            PresenterBlacksmithShowcaseTestHarness.LoadMap(engine, PresenterBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            PrimitiveDrawBuffer snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            _ = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("PresenterEntityRuntime missing.");
            PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");
            timings.SystemBreakdownEnabled = true;
            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            PresentationEventStream events = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream missing.");
            PresenterCommandBuffer commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
                ?? throw new InvalidOperationException("PresenterCommandBuffer missing.");

            int benchmarkPresenterDefId = definitions.GetId(BenchmarkPresenterId);
            int enqueued = EnqueueBenchmarkTemplates(queue, engine.CurrentMapSession?.MapId ?? default, count, out double enqueueMs);

            long createStart = Stopwatch.GetTimestamp();
            int entityCount = 0;
            int presenterCount = 0;
            int primitiveCount = 0;
            int ismPrimitiveCount = 0;
            double firstTickMs = 0d;
            double validationScansMs = 0d;
            float initDiagTotalTickMs = 0f;
            float initDiagPresentationMs = 0f;
            float initDiagSimulationMs = 0f;
            float initDiagCameraCullingMs = 0f;
            float initDiagCameraCullingEntityProcessMs = 0f;
            float initDiagCameraCullingPresenterSyncMs = 0f;
            float initDiagCameraCullingStaticProcessMs = 0f;
            float initDiagCameraCullingStaticPendingRemoveMs = 0f;
            float initDiagCameraCullingDynamicProcessMs = 0f;
            float initDiagBehaviorMs = 0f;
            float initDiagAnimatorMs = 0f;
            float initDiagTransformSyncMs = 0f;
            float initRuntimeSpawnBatchPrepareMs = 0f;
            float initRuntimeSpawnWorldCreateMs = 0f;
            float initRuntimeSpawnFillBatchMs = 0f;
            float initRuntimeSpawnPostSpawnMs = 0f;
            float initRuntimeSpawnPresenterBatchMs = 0f;
            float initRuntimeSpawnPresenterCreateMs = 0f;
            float initRuntimeSpawnPresenterBootstrapMarkMs = 0f;
            float initRuntimeSpawnPresenterCreateSetupMs = 0f;
            float initRuntimeSpawnPresenterWorldCreateMs = 0f;
            float initRuntimeSpawnPresenterComponentFillMs = 0f;
            float initRuntimeSpawnPresenterIndexWriteMs = 0f;
            float initRuntimeSpawnPresenterOwnerPayloadMs = 0f;
            float initRuntimeSpawnPresenterPostCreateMs = 0f;
            int initRuntimeSpawnBatchCount = 0;
            int initRuntimeSpawnPresenterCreated = 0;
            float initDiagEmitMs = 0f;
            float initDiagEmitDirtyProcessMs = 0f;
            float initDiagEmitDirtyCleanupMs = 0f;
            int initDiagEmitDirtyCount = 0;
            float initDiagRequestFlushMs = 0f;
            string initPresentationTop1Name = string.Empty;
            float initPresentationTop1Ms = 0f;
            string initPresentationTop2Name = string.Empty;
            float initPresentationTop2Ms = 0f;
            string initPresentationTop3Name = string.Empty;
            float initPresentationTop3Ms = 0f;
            string initSimulationTop1Name = string.Empty;
            float initSimulationTop1Ms = 0f;
            string initSimulationTop2Name = string.Empty;
            float initSimulationTop2Ms = 0f;
            string initSimulationTop3Name = string.Empty;
            float initSimulationTop3Ms = 0f;
            int settleFrames = 0;
            for (int settleAttempt = 0; settleAttempt < SpawnSettleFrameBudget; settleAttempt++)
            {
                long firstTickStart = Stopwatch.GetTimestamp();
                PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                double settleTickMs = ElapsedMs(firstTickStart);
                if (settleAttempt == 0)
                {
                    firstTickMs = settleTickMs;
                    initDiagTotalTickMs = timings.LastTotalTickMs;
                    initDiagPresentationMs = timings.LastPresentationMs;
                    initDiagSimulationMs = timings.LastSimulationMs;
                    initDiagCameraCullingMs = timings.LastCameraCullingMs;
                    initDiagCameraCullingEntityProcessMs = timings.LastCameraCullingEntityProcessMs;
                    initDiagCameraCullingPresenterSyncMs = timings.LastCameraCullingPresenterSyncMs;
                    initDiagCameraCullingStaticProcessMs = timings.LastCameraCullingStaticProcessMs;
                    initDiagCameraCullingStaticPendingRemoveMs = timings.LastCameraCullingStaticPendingRemoveMs;
                    initDiagCameraCullingDynamicProcessMs = timings.LastCameraCullingDynamicProcessMs;
                    initDiagBehaviorMs = timings.LastPresenterBehaviorMs;
                    initDiagAnimatorMs = timings.LastPresenterAnimatorMs;
                    initDiagTransformSyncMs = timings.LastPresenterEntityTransformSyncMs;
                    initRuntimeSpawnBatchPrepareMs = timings.LastRuntimeSpawnBatchPrepareMs;
                    initRuntimeSpawnWorldCreateMs = timings.LastRuntimeSpawnWorldCreateMs;
                    initRuntimeSpawnFillBatchMs = timings.LastRuntimeSpawnFillBatchMs;
                    initRuntimeSpawnPostSpawnMs = timings.LastRuntimeSpawnPostSpawnMs;
                    initRuntimeSpawnPresenterBatchMs = timings.LastRuntimeSpawnPresenterBatchMs;
                    initRuntimeSpawnPresenterCreateMs = timings.LastRuntimeSpawnPresenterCreateMs;
                    initRuntimeSpawnPresenterBootstrapMarkMs = timings.LastRuntimeSpawnPresenterBootstrapMarkMs;
                    initRuntimeSpawnPresenterCreateSetupMs = timings.LastRuntimeSpawnPresenterCreateSetupMs;
                    initRuntimeSpawnPresenterWorldCreateMs = timings.LastRuntimeSpawnPresenterWorldCreateMs;
                    initRuntimeSpawnPresenterComponentFillMs = timings.LastRuntimeSpawnPresenterComponentFillMs;
                    initRuntimeSpawnPresenterIndexWriteMs = timings.LastRuntimeSpawnPresenterIndexWriteMs;
                    initRuntimeSpawnPresenterOwnerPayloadMs = timings.LastRuntimeSpawnPresenterOwnerPayloadMs;
                    initRuntimeSpawnPresenterPostCreateMs = timings.LastRuntimeSpawnPresenterPostCreateMs;
                    initRuntimeSpawnBatchCount = timings.RuntimeSpawnBatchCountLastFrame;
                    initRuntimeSpawnPresenterCreated = timings.RuntimeSpawnPresenterCreatedLastFrame;
                    initDiagEmitMs = timings.LastPresenterEmitMs;
                    initDiagEmitDirtyProcessMs = timings.LastPresenterEmitDirtyProcessMs;
                    initDiagEmitDirtyCleanupMs = timings.LastPresenterEmitDirtyCleanupMs;
                    initDiagEmitDirtyCount = timings.PresenterEmitDirtyCountLastFrame;
                    initDiagRequestFlushMs = timings.LastPresentationRequestFlushMs;
                    initPresentationTop1Name = timings.LastPresentationTopSystem1Name;
                    initPresentationTop1Ms = timings.LastPresentationTopSystem1Ms;
                    initPresentationTop2Name = timings.LastPresentationTopSystem2Name;
                    initPresentationTop2Ms = timings.LastPresentationTopSystem2Ms;
                    initPresentationTop3Name = timings.LastPresentationTopSystem3Name;
                    initPresentationTop3Ms = timings.LastPresentationTopSystem3Ms;
                    initSimulationTop1Name = timings.LastSimulationTopSystem1Name;
                    initSimulationTop1Ms = timings.LastSimulationTopSystem1Ms;
                    initSimulationTop2Name = timings.LastSimulationTopSystem2Name;
                    initSimulationTop2Ms = timings.LastSimulationTopSystem2Ms;
                    initSimulationTop3Name = timings.LastSimulationTopSystem3Name;
                    initSimulationTop3Ms = timings.LastSimulationTopSystem3Ms;
                }

                long validationStart = Stopwatch.GetTimestamp();
                entityCount = CountBenchmarkEntities(engine);
                presenterCount = CountPresenters(engine, benchmarkPresenterDefId);
                CountPrimitives(
                    snapshot,
                    benchmarkPresenterDefId,
                    out primitiveCount,
                    out ismPrimitiveCount);
                validationScansMs += ElapsedMs(validationStart);

                if (entityCount == count &&
                    presenterCount == count &&
                    ismPrimitiveCount == count)
                {
                    settleFrames = settleAttempt + 1;
                    break;
                }
            }

            double createAndFirstEmitMs = ElapsedMs(createStart);

            var bridge = new RaylibIsmRenderBridge();
            long bridgeStart = Stopwatch.GetTimestamp();
            bridge.SyncPersistentLanes(snapshot);
            double raylibInitBridgeSyncMs = ElapsedMs(bridgeStart);
            CountRaylibBenchmarkBuckets(
                bridge,
                benchmarkPresenterDefId,
                out int raylibBenchmarkBindings,
                out int raylibBenchmarkBuckets);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int frame = 0; frame < StableWarmupFrames; frame++)
            {
                PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                bridge.SyncPersistentLanes(snapshot);
            }

            double[] tickMs = new double[MeasuredTickFrames];
            double[] bridgeSyncMs = new double[MeasuredTickFrames];
            double[] totalTickDiagMs = new double[MeasuredTickFrames];
            double[] presentationDiagMs = new double[MeasuredTickFrames];
            double[] simulationDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingEntityProcessDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingPresenterSyncDiagMs = new double[MeasuredTickFrames];
            double[] behaviorDiagMs = new double[MeasuredTickFrames];
            double[] animatorDiagMs = new double[MeasuredTickFrames];
            double[] transformSyncDiagMs = new double[MeasuredTickFrames];
            double[] emitDiagMs = new double[MeasuredTickFrames];
            double[] requestFlushDiagMs = new double[MeasuredTickFrames];
            for (int frame = 0; frame < MeasuredTickFrames; frame++)
            {
                long tickStart = Stopwatch.GetTimestamp();
                PresenterBlacksmithShowcaseTestHarness.Tick(engine, 1);
                tickMs[frame] = ElapsedMs(tickStart);
                totalTickDiagMs[frame] = timings.TotalTickMs;
                presentationDiagMs[frame] = timings.PresentationMs;
                simulationDiagMs[frame] = timings.SimulationMs;
                cameraCullingDiagMs[frame] = timings.CameraCullingMs;
                cameraCullingEntityProcessDiagMs[frame] = timings.CameraCullingEntityProcessMs;
                cameraCullingPresenterSyncDiagMs[frame] = timings.CameraCullingPresenterSyncMs;
                behaviorDiagMs[frame] = timings.PresenterBehaviorMs;
                animatorDiagMs[frame] = timings.PresenterAnimatorMs;
                transformSyncDiagMs[frame] = timings.PresenterEntityTransformSyncMs;
                emitDiagMs[frame] = timings.PresenterEmitMs;
                requestFlushDiagMs[frame] = timings.PresentationRequestFlushMs;

                long stableBridgeStart = Stopwatch.GetTimestamp();
                bridge.SyncPersistentLanes(snapshot);
                bridgeSyncMs[frame] = ElapsedMs(stableBridgeStart);
            }

            return new MeshIsmBenchmarkResult(
                count,
                enqueued,
                entityCount,
                presenterCount,
                primitiveCount,
                ismPrimitiveCount,
                raylibBenchmarkBindings,
                raylibBenchmarkBuckets,
                settleFrames,
                enqueueMs,
                createAndFirstEmitMs,
                firstTickMs,
                validationScansMs,
                initDiagTotalTickMs,
                initDiagPresentationMs,
                initDiagSimulationMs,
                initDiagCameraCullingMs,
                initDiagCameraCullingEntityProcessMs,
                initDiagCameraCullingPresenterSyncMs,
                initDiagCameraCullingStaticProcessMs,
                initDiagCameraCullingStaticPendingRemoveMs,
                initDiagCameraCullingDynamicProcessMs,
                initDiagBehaviorMs,
                initDiagAnimatorMs,
                initDiagTransformSyncMs,
                initRuntimeSpawnBatchPrepareMs,
                initRuntimeSpawnWorldCreateMs,
                initRuntimeSpawnFillBatchMs,
                initRuntimeSpawnPostSpawnMs,
                initRuntimeSpawnPresenterBatchMs,
                initRuntimeSpawnPresenterCreateMs,
                initRuntimeSpawnPresenterBootstrapMarkMs,
                initRuntimeSpawnPresenterCreateSetupMs,
                initRuntimeSpawnPresenterWorldCreateMs,
                initRuntimeSpawnPresenterComponentFillMs,
                initRuntimeSpawnPresenterIndexWriteMs,
                initRuntimeSpawnPresenterOwnerPayloadMs,
                initRuntimeSpawnPresenterPostCreateMs,
                initRuntimeSpawnBatchCount,
                initRuntimeSpawnPresenterCreated,
                initDiagEmitMs,
                initDiagEmitDirtyProcessMs,
                initDiagEmitDirtyCleanupMs,
                initDiagEmitDirtyCount,
                initDiagRequestFlushMs,
                initPresentationTop1Name,
                initPresentationTop1Ms,
                initPresentationTop2Name,
                initPresentationTop2Ms,
                initPresentationTop3Name,
                initPresentationTop3Ms,
                initSimulationTop1Name,
                initSimulationTop1Ms,
                initSimulationTop2Name,
                initSimulationTop2Ms,
                initSimulationTop3Name,
                initSimulationTop3Ms,
                raylibInitBridgeSyncMs,
                tickMs,
                bridgeSyncMs,
                totalTickDiagMs,
                presentationDiagMs,
                simulationDiagMs,
                cameraCullingDiagMs,
                cameraCullingEntityProcessDiagMs,
                cameraCullingPresenterSyncDiagMs,
                behaviorDiagMs,
                animatorDiagMs,
                transformSyncDiagMs,
                emitDiagMs,
                requestFlushDiagMs,
                primitives.DroppedTotal,
                events.DroppedTotal,
                commands.DroppedTotal);
        }

        private static int EnqueueBenchmarkTemplates(RuntimeEntitySpawnQueue queue, MapId mapId, int count, out double elapsedMs)
        {
            var requests = new RuntimeEntitySpawnRequest[count];
            int ringCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            int ringCapacity = Math.Max(6, count / ringCount + 2);
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                int ring = i / ringCapacity;
                int slot = i % ringCapacity;
                float alpha = ringCount <= 1 ? 1f : (ring + 1f) / ringCount;
                float radius = 750f + (2400f - 750f) * alpha;
                float angle = (slot / (float)ringCapacity) * MathF.PI * 2f;
                float x = MathF.Cos(angle) * radius;
                float y = MathF.Sin(angle) * radius;
                requests[i] = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = BenchmarkTemplateId,
                    MapId = mapId,
                    WorldPositionCm = Fix64Vec2.FromFloat(x, y),
                    HasWorldPosition = 1,
                    FacingAngleRad = angle + MathF.PI,
                    HasFacing = 1,
                };
            }

            int enqueued = queue.EnqueueMany(requests);
            elapsedMs = ElapsedMs(start);
            return enqueued;
        }

        private static JsonObject FindDefinition(JsonArray definitions, string id)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] is JsonObject obj &&
                    string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                {
                    return obj;
                }
            }

            throw new InvalidOperationException($"Presenter definition '{id}' was not found.");
        }

        private static int CountBenchmarkEntities(GameEngine engine)
        {
            int count = 0;
            var query = new Arch.Core.QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, BenchmarkEntityName, StringComparison.Ordinal))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountPresenters(GameEngine engine, int definitionId)
        {
            int count = 0;
            var query = new Arch.Core.QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (ref PresenterState state) =>
            {
                if (state.DefId == definitionId)
                {
                    count++;
                }
            });

            return count;
        }

        private static void CountPrimitives(
            PrimitiveDrawBuffer primitives,
            int templateId,
            out int primitiveCount,
            out int ismPrimitiveCount)
        {
            primitiveCount = 0;
            ismPrimitiveCount = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.TemplateId != templateId)
                {
                    continue;
                }

                primitiveCount++;
                if (item.RenderPath == VisualRenderPath.InstancedStaticMesh &&
                    item.Mobility == VisualMobility.Static &&
                    item.Visibility == VisualVisibility.Visible)
                {
                    ismPrimitiveCount++;
                }
            }
        }

        private static void CountRaylibBenchmarkBuckets(
            RaylibIsmRenderBridge bridge,
            int templateId,
            out int bindingCount,
            out int bucketCount)
        {
            bindingCount = 0;
            bucketCount = 0;
            for (int i = 0; i < bridge.ActiveBuckets.Count; i++)
            {
                RaylibIsmRenderBridge.Bucket bucket = bridge.ActiveBuckets[i];
                int bucketBenchmarkItems = 0;
                for (int itemIndex = 0; itemIndex < bucket.Items.Count; itemIndex++)
                {
                    if (bucket.Items[itemIndex].TemplateId == templateId)
                    {
                        bucketBenchmarkItems++;
                    }
                }

                if (bucketBenchmarkItems > 0)
                {
                    bucketCount++;
                    bindingCount += bucketBenchmarkItems;
                }
            }
        }

        private static string BuildReport(MeshIsmBenchmarkResult[] results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Presenter Mesh ISM Production Path Benchmark");
            sb.AppendLine();
            sb.AppendLine("- template: `blacksmith_mesh_benchmark_entity`");
            sb.AppendLine("- presenter rule: `EntitySpawned -> blacksmith_mesh_benchmark_ism`");
            sb.AppendLine("- mesh: `blacksmith.building.north.intact`");
            sb.AppendLine("- render path: `InstancedStaticMesh` through `RaylibIsmRenderBridge.SyncPersistentLanes`");
            sb.AppendLine("- init excludes: real GPU draw call timing; validates production create/first emit plus latest raylib ISM bridge bucketing");
            sb.AppendLine("- tick excludes: real GPU draw call timing; validates stable production tick plus raylib bridge resync cost after initialization");
            sb.AppendLine("- stable tick sampling starts after explicit post-init GC cleanup and warmup frames so init debt does not pollute steady-state numbers");
            sb.AppendLine();
            sb.AppendLine("## Init");
            sb.AppendLine();
            sb.AppendLine("| Count | Enqueue | Create+First Emit | First Tick | Validation Scans | Raylib Initial Sync | Settle Frames | Entities | Presenters | ISM Primitives | Raylib Buckets |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.EnqueueMs:F4} ms | {r.CreateAndFirstEmitMs:F4} ms | {r.FirstTickMs:F4} ms | {r.ValidationScansMs:F4} ms | {r.RaylibInitBridgeSyncMs:F4} ms | {r.SettleFrames} | {r.EntityCount} | {r.PresenterCount} | {r.IsmPrimitiveCount} | {r.RaylibBucketCount} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Init Breakdown");
            sb.AppendLine();
            sb.AppendLine("| Count | init diag Total Tick | init diag Presentation | init diag Simulation | runtime batch | runtime prepare | runtime world create | runtime fill batch | runtime post spawn | runtime presenter batch | runtime presenter create | presenter setup | presenter world create | presenter component fill | presenter index write | presenter owner payload | presenter post create | runtime bootstrap mark | runtime presenters | init diag Camera Culling | init cull entity | init cull static | init cull pending remove | init cull dynamic | init cull presenter sync | init diag Behavior | init diag Animator | init diag Transform Sync | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty presenters | init diag Request Flush | init top presentation systems | init top simulation systems |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.InitDiagTotalTickMs:F4} ms | {r.InitDiagPresentationMs:F4} ms | {r.InitDiagSimulationMs:F4} ms | {r.InitRuntimeSpawnBatchCount} | {r.InitRuntimeSpawnBatchPrepareMs:F4} ms | {r.InitRuntimeSpawnWorldCreateMs:F4} ms | {r.InitRuntimeSpawnFillBatchMs:F4} ms | {r.InitRuntimeSpawnPostSpawnMs:F4} ms | {r.InitRuntimeSpawnPresenterBatchMs:F4} ms | {r.InitRuntimeSpawnPresenterCreateMs:F4} ms | {r.InitRuntimeSpawnPresenterCreateSetupMs:F4} ms | {r.InitRuntimeSpawnPresenterWorldCreateMs:F4} ms | {r.InitRuntimeSpawnPresenterComponentFillMs:F4} ms | {r.InitRuntimeSpawnPresenterIndexWriteMs:F4} ms | {r.InitRuntimeSpawnPresenterOwnerPayloadMs:F4} ms | {r.InitRuntimeSpawnPresenterPostCreateMs:F4} ms | {r.InitRuntimeSpawnPresenterBootstrapMarkMs:F4} ms | {r.InitRuntimeSpawnPresenterCreated} | {r.InitDiagCameraCullingMs:F4} ms | {r.InitDiagCameraCullingEntityProcessMs:F4} ms | {r.InitDiagCameraCullingStaticProcessMs:F4} ms | {r.InitDiagCameraCullingStaticPendingRemoveMs:F4} ms | {r.InitDiagCameraCullingDynamicProcessMs:F4} ms | {r.InitDiagCameraCullingPresenterSyncMs:F4} ms | {r.InitDiagBehaviorMs:F4} ms | {r.InitDiagAnimatorMs:F4} ms | {r.InitDiagTransformSyncMs:F4} ms | {r.InitDiagEmitMs:F4} ms | {r.InitDiagEmitDirtyProcessMs:F4} ms | {r.InitDiagEmitDirtyCleanupMs:F4} ms | {r.InitDiagEmitDirtyCount} | {r.InitDiagRequestFlushMs:F4} ms | {FormatPresentationTopSystems(r)} | {FormatSimulationTopSystems(r)} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Stable Tick");
            sb.AppendLine();
            sb.AppendLine("| Count | Frames | Avg Tick | P95 Tick | Max Tick | Avg Bridge Sync | P95 Bridge Sync | Max Bridge Sync |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
            sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.TickFrames} | {Average(r.TickMs):F4} ms | {Percentile(r.TickMs, 0.95):F4} ms | {Max(r.TickMs):F4} ms | {Average(r.BridgeSyncTickMs):F4} ms | {Percentile(r.BridgeSyncTickMs, 0.95):F4} ms | {Max(r.BridgeSyncTickMs):F4} ms |");
            }

            sb.AppendLine();
            sb.AppendLine("## Tick Breakdown");
            sb.AppendLine();
            sb.AppendLine("> `diag_*` values below come from `PresentationTimingDiagnostics` and are exponentially smoothed in-engine; use them as stable attribution, not exact per-frame wall-clock sums.");
            sb.AppendLine();
            sb.AppendLine("| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull presenter sync | diag Behavior | diag Animator | diag Transform Sync | diag Emit | diag Request Flush |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {Average(r.DiagTotalTickMs):F4} ms | {Average(r.DiagPresentationMs):F4} ms | {Average(r.DiagSimulationMs):F4} ms | {Average(r.DiagCameraCullingMs):F4} ms | {Average(r.DiagCameraCullingEntityProcessMs):F4} ms | {Average(r.DiagCameraCullingPresenterSyncMs):F4} ms | {Average(r.DiagBehaviorMs):F4} ms | {Average(r.DiagAnimatorMs):F4} ms | {Average(r.DiagTransformSyncMs):F4} ms | {Average(r.DiagEmitMs):F4} ms | {Average(r.DiagRequestFlushMs):F4} ms |");
            }

            sb.AppendLine();
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- {r.Count}: init create+emit per entity `{(r.CreateAndFirstEmitMs / Math.Max(1, r.Count)):F6} ms`, runtime prepare `{r.InitRuntimeSpawnBatchPrepareMs:F4} ms`, world create `{r.InitRuntimeSpawnWorldCreateMs:F4} ms`, fill batch `{r.InitRuntimeSpawnFillBatchMs:F4} ms`, post spawn `{r.InitRuntimeSpawnPostSpawnMs:F4} ms`, presenter batch `{r.InitRuntimeSpawnPresenterBatchMs:F4} ms`, presenter create `{r.InitRuntimeSpawnPresenterCreateMs:F4} ms`, presenter setup `{r.InitRuntimeSpawnPresenterCreateSetupMs:F4} ms`, presenter world create `{r.InitRuntimeSpawnPresenterWorldCreateMs:F4} ms`, presenter component fill `{r.InitRuntimeSpawnPresenterComponentFillMs:F4} ms`, presenter index write `{r.InitRuntimeSpawnPresenterIndexWriteMs:F4} ms`, presenter owner payload `{r.InitRuntimeSpawnPresenterOwnerPayloadMs:F4} ms`, presenter post create `{r.InitRuntimeSpawnPresenterPostCreateMs:F4} ms`, bootstrap mark `{r.InitRuntimeSpawnPresenterBootstrapMarkMs:F4} ms`, first tick `{r.FirstTickMs:F4} ms`, validation scans `{r.ValidationScansMs:F4} ms`, init diag transform sync `{r.InitDiagTransformSyncMs:F4} ms`, init diag emit `{r.InitDiagEmitMs:F4} ms`, dirty emit process `{r.InitDiagEmitDirtyProcessMs:F4} ms`, dirty emit cleanup `{r.InitDiagEmitDirtyCleanupMs:F4} ms`, init diag request flush `{r.InitDiagRequestFlushMs:F4} ms`, init diag culling `{r.InitDiagCameraCullingMs:F4} ms`, init cull entity `{r.InitDiagCameraCullingEntityProcessMs:F4} ms`, init cull static `{r.InitDiagCameraCullingStaticProcessMs:F4} ms`, init cull pending remove `{r.InitDiagCameraCullingStaticPendingRemoveMs:F4} ms`, init cull dynamic `{r.InitDiagCameraCullingDynamicProcessMs:F4} ms`, init cull presenter sync `{r.InitDiagCameraCullingPresenterSyncMs:F4} ms`, initial bridge sync per primitive `{(r.RaylibInitBridgeSyncMs / Math.Max(1, r.IsmPrimitiveCount)):F6} ms`, stable avg tick per entity `{(Average(r.TickMs) / Math.Max(1, r.Count)):F6} ms`, drops events `{r.EventDrops}` commands `{r.CommandDrops}` primitives `{r.PrimitiveDrops}`");
            }

            return sb.ToString();
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private static string FormatPresentationTopSystems(in MeshIsmBenchmarkResult result)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{result.InitPresentationTop1Name} {result.InitPresentationTop1Ms:F4} ms; {result.InitPresentationTop2Name} {result.InitPresentationTop2Ms:F4} ms; {result.InitPresentationTop3Name} {result.InitPresentationTop3Ms:F4} ms");
        }

        private static string FormatSimulationTopSystems(in MeshIsmBenchmarkResult result)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{result.InitSimulationTop1Name} {result.InitSimulationTop1Ms:F4} ms; {result.InitSimulationTop2Name} {result.InitSimulationTop2Ms:F4} ms; {result.InitSimulationTop3Name} {result.InitSimulationTop3Ms:F4} ms");
        }

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

        private readonly record struct MeshIsmBenchmarkResult(
            int Count,
            int Enqueued,
            int EntityCount,
            int PresenterCount,
            int PrimitiveCount,
            int IsmPrimitiveCount,
            int RaylibActiveBindings,
            int RaylibBucketCount,
            int SettleFrames,
            double EnqueueMs,
            double CreateAndFirstEmitMs,
            double FirstTickMs,
            double ValidationScansMs,
            float InitDiagTotalTickMs,
            float InitDiagPresentationMs,
            float InitDiagSimulationMs,
            float InitDiagCameraCullingMs,
            float InitDiagCameraCullingEntityProcessMs,
            float InitDiagCameraCullingPresenterSyncMs,
            float InitDiagCameraCullingStaticProcessMs,
            float InitDiagCameraCullingStaticPendingRemoveMs,
            float InitDiagCameraCullingDynamicProcessMs,
            float InitDiagBehaviorMs,
            float InitDiagAnimatorMs,
            float InitDiagTransformSyncMs,
            float InitRuntimeSpawnBatchPrepareMs,
            float InitRuntimeSpawnWorldCreateMs,
            float InitRuntimeSpawnFillBatchMs,
            float InitRuntimeSpawnPostSpawnMs,
            float InitRuntimeSpawnPresenterBatchMs,
            float InitRuntimeSpawnPresenterCreateMs,
            float InitRuntimeSpawnPresenterBootstrapMarkMs,
            float InitRuntimeSpawnPresenterCreateSetupMs,
            float InitRuntimeSpawnPresenterWorldCreateMs,
            float InitRuntimeSpawnPresenterComponentFillMs,
            float InitRuntimeSpawnPresenterIndexWriteMs,
            float InitRuntimeSpawnPresenterOwnerPayloadMs,
            float InitRuntimeSpawnPresenterPostCreateMs,
            int InitRuntimeSpawnBatchCount,
            int InitRuntimeSpawnPresenterCreated,
            float InitDiagEmitMs,
            float InitDiagEmitDirtyProcessMs,
            float InitDiagEmitDirtyCleanupMs,
            int InitDiagEmitDirtyCount,
            float InitDiagRequestFlushMs,
            string InitPresentationTop1Name,
            float InitPresentationTop1Ms,
            string InitPresentationTop2Name,
            float InitPresentationTop2Ms,
            string InitPresentationTop3Name,
            float InitPresentationTop3Ms,
            string InitSimulationTop1Name,
            float InitSimulationTop1Ms,
            string InitSimulationTop2Name,
            float InitSimulationTop2Ms,
            string InitSimulationTop3Name,
            float InitSimulationTop3Ms,
            double RaylibInitBridgeSyncMs,
            double[] TickMs,
            double[] BridgeSyncTickMs,
            double[] DiagTotalTickMs,
            double[] DiagPresentationMs,
            double[] DiagSimulationMs,
            double[] DiagCameraCullingMs,
            double[] DiagCameraCullingEntityProcessMs,
            double[] DiagCameraCullingPresenterSyncMs,
            double[] DiagBehaviorMs,
            double[] DiagAnimatorMs,
            double[] DiagTransformSyncMs,
            double[] DiagEmitMs,
            double[] DiagRequestFlushMs,
            int PrimitiveDrops,
            int EventDrops,
            int CommandDrops)
        {
            public int TickFrames => TickMs.Length;
        }
    }
}
