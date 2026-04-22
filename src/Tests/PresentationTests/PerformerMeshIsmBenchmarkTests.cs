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
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformerMeshIsmBenchmarkTests
    {
        private const string BenchmarkTemplateId = "blacksmith_mesh_benchmark_entity";
        private const string BenchmarkPerformerId = "blacksmith_mesh_benchmark_ism";
        private const string BenchmarkEntityName = "BlacksmithMeshBenchmark";
        private const int MeasuredTickFrames = 60;
        private const int StableWarmupFrames = 8;

        private static readonly int[] Counts = { 3_000, 10_000, 30_000 };

        [Test]
        public void BenchmarkMeshAssetBinding_DoesNotOwnTransformOrGrounding()
        {
            string repoRoot = PerformerBlacksmithShowcaseTestHarness.FindRepoRoot();
            string performersPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "performer_blacksmith",
                "PerformerBlacksmithShowcaseMod",
                "assets",
                "Presentation",
                "performers.json");

            JsonArray performers = JsonNode.Parse(File.ReadAllText(performersPath))?.AsArray()
                ?? throw new InvalidOperationException("performers.json must contain a top-level array.");
            JsonObject benchmark = FindDefinition(performers, BenchmarkPerformerId);
            JsonObject assetBinding = benchmark["behaviors"]?.AsArray()[0]?["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException("Benchmark performer must have slot 0 assetBinding.");

            Assert.That(assetBinding.ContainsKey("localOffset"), Is.False, "AssetBinding must not own local transform offsets.");
            Assert.That(assetBinding.ContainsKey("localScale"), Is.False, "AssetBinding must not own local transform scale.");
            Assert.That(assetBinding.ContainsKey("grounding"), Is.False, "Grounding belongs to transform/grounding behavior, not AssetBinding.");
            Assert.That(assetBinding.ContainsKey("groundingOffset"), Is.False, "Grounding belongs to transform/grounding behavior, not AssetBinding.");
        }

        [Test]
        public void Benchmark_TemplatePerformerMesh_IntoRaylibIsmBridge_WritesReport()
        {
            var results = new MeshIsmBenchmarkResult[Counts.Length];
            for (int i = 0; i < Counts.Length; i++)
            {
                results[i] = RunScenario(Counts[i]);
            }

            string artifactDir = Path.Combine(
                PerformerBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "performer-mesh-ism-production-path");
            Directory.CreateDirectory(artifactDir);

            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            File.WriteAllText(reportPath, BuildReport(results));
            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult result = results[i];
                Assert.That(result.Enqueued, Is.EqualTo(result.Count), $"{result.Count}: enqueue count mismatch.");
                Assert.That(result.EntityCount, Is.EqualTo(result.Count), $"{result.Count}: spawned entity count mismatch.");
                Assert.That(result.PerformerCount, Is.EqualTo(result.Count), $"{result.Count}: performer count mismatch.");
                Assert.That(result.PrimitiveCount, Is.EqualTo(result.Count), $"{result.Count}: primitive count mismatch.");
                Assert.That(result.IsmPrimitiveCount, Is.EqualTo(result.Count), $"{result.Count}: ISM primitive count mismatch.");
                Assert.That(result.RaylibActiveBindings, Is.EqualTo(result.Count), $"{result.Count}: raylib bridge binding count mismatch.");
                Assert.That(result.RaylibBucketCount, Is.EqualTo(1), $"{result.Count}: benchmark should collapse to one ISM lane/bucket.");
                Assert.That(result.TickFrames, Is.EqualTo(MeasuredTickFrames), $"{result.Count}: tick sample count mismatch.");
                Assert.That(result.PrimitiveDrops, Is.EqualTo(0), $"{result.Count}: primitive buffer dropped items.");
                Assert.That(result.EventDrops, Is.EqualTo(0), $"{result.Count}: presentation event stream dropped items.");
                Assert.That(result.CommandDrops, Is.EqualTo(0), $"{result.Count}: performer command buffer dropped items.");
            }
        }

        private static MeshIsmBenchmarkResult RunScenario(int count)
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            PrimitiveDrawBuffer snapshot = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
                ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer missing.");
            _ = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
                ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
            PresentationTimingDiagnostics timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");
            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            PresentationEventStream events = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream missing.");
            PerformerCommandBuffer commands = engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
                ?? throw new InvalidOperationException("PerformerCommandBuffer missing.");

            int benchmarkPerformerDefId = definitions.GetId(BenchmarkPerformerId);
            int enqueued = EnqueueBenchmarkTemplates(queue, engine.CurrentMapSession?.MapId ?? default, count, out double enqueueMs);

            long createStart = Stopwatch.GetTimestamp();
            int entityCount = 0;
            int performerCount = 0;
            int primitiveCount = 0;
            int ismPrimitiveCount = 0;
            double firstTickMs = 0d;
            double validationScansMs = 0d;
            float initDiagTotalTickMs = 0f;
            float initDiagPresentationMs = 0f;
            float initDiagSimulationMs = 0f;
            float initDiagCameraCullingMs = 0f;
            float initDiagCameraCullingEntityProcessMs = 0f;
            float initDiagCameraCullingPerformerSyncMs = 0f;
            float initDiagBehaviorMs = 0f;
            float initDiagAnimatorMs = 0f;
            float initDiagEmitMs = 0f;
            float initDiagEmitDirtyProcessMs = 0f;
            float initDiagEmitDirtyCleanupMs = 0f;
            int initDiagEmitDirtyCount = 0;
            float initDiagRequestFlushMs = 0f;
            int settleFrames = 0;
            for (int settleAttempt = 0; settleAttempt < 12; settleAttempt++)
            {
                long firstTickStart = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                double settleTickMs = ElapsedMs(firstTickStart);
                if (settleAttempt == 0)
                {
                    firstTickMs = settleTickMs;
                    initDiagTotalTickMs = timings.LastTotalTickMs;
                    initDiagPresentationMs = timings.LastPresentationMs;
                    initDiagSimulationMs = timings.LastSimulationMs;
                    initDiagCameraCullingMs = timings.LastCameraCullingMs;
                    initDiagCameraCullingEntityProcessMs = timings.LastCameraCullingEntityProcessMs;
                    initDiagCameraCullingPerformerSyncMs = timings.LastCameraCullingPerformerSyncMs;
                    initDiagBehaviorMs = timings.LastPerformerBehaviorMs;
                    initDiagAnimatorMs = timings.LastPerformerAnimatorMs;
                    initDiagEmitMs = timings.LastPerformerEmitMs;
                    initDiagEmitDirtyProcessMs = timings.LastPerformerEmitDirtyProcessMs;
                    initDiagEmitDirtyCleanupMs = timings.LastPerformerEmitDirtyCleanupMs;
                    initDiagEmitDirtyCount = timings.PerformerEmitDirtyCountLastFrame;
                    initDiagRequestFlushMs = timings.LastPresentationRequestFlushMs;
                }

                long validationStart = Stopwatch.GetTimestamp();
                entityCount = CountBenchmarkEntities(engine);
                performerCount = CountPerformers(engine, benchmarkPerformerDefId);
                CountPrimitives(
                    snapshot,
                    benchmarkPerformerDefId,
                    out primitiveCount,
                    out ismPrimitiveCount);
                validationScansMs += ElapsedMs(validationStart);

                if (entityCount == count &&
                    performerCount == count &&
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
                benchmarkPerformerDefId,
                out int raylibBenchmarkBindings,
                out int raylibBenchmarkBuckets);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int frame = 0; frame < StableWarmupFrames; frame++)
            {
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                bridge.SyncPersistentLanes(snapshot);
            }

            double[] tickMs = new double[MeasuredTickFrames];
            double[] bridgeSyncMs = new double[MeasuredTickFrames];
            double[] totalTickDiagMs = new double[MeasuredTickFrames];
            double[] presentationDiagMs = new double[MeasuredTickFrames];
            double[] simulationDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingEntityProcessDiagMs = new double[MeasuredTickFrames];
            double[] cameraCullingPerformerSyncDiagMs = new double[MeasuredTickFrames];
            double[] behaviorDiagMs = new double[MeasuredTickFrames];
            double[] animatorDiagMs = new double[MeasuredTickFrames];
            double[] emitDiagMs = new double[MeasuredTickFrames];
            double[] requestFlushDiagMs = new double[MeasuredTickFrames];
            for (int frame = 0; frame < MeasuredTickFrames; frame++)
            {
                long tickStart = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                tickMs[frame] = ElapsedMs(tickStart);
                totalTickDiagMs[frame] = timings.TotalTickMs;
                presentationDiagMs[frame] = timings.PresentationMs;
                simulationDiagMs[frame] = timings.SimulationMs;
                cameraCullingDiagMs[frame] = timings.CameraCullingMs;
                cameraCullingEntityProcessDiagMs[frame] = timings.CameraCullingEntityProcessMs;
                cameraCullingPerformerSyncDiagMs[frame] = timings.CameraCullingPerformerSyncMs;
                behaviorDiagMs[frame] = timings.PerformerBehaviorMs;
                animatorDiagMs[frame] = timings.PerformerAnimatorMs;
                emitDiagMs[frame] = timings.PerformerEmitMs;
                requestFlushDiagMs[frame] = timings.PresentationRequestFlushMs;

                long stableBridgeStart = Stopwatch.GetTimestamp();
                bridge.SyncPersistentLanes(snapshot);
                bridgeSyncMs[frame] = ElapsedMs(stableBridgeStart);
            }

            return new MeshIsmBenchmarkResult(
                count,
                enqueued,
                entityCount,
                performerCount,
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
                initDiagCameraCullingPerformerSyncMs,
                initDiagBehaviorMs,
                initDiagAnimatorMs,
                initDiagEmitMs,
                initDiagEmitDirtyProcessMs,
                initDiagEmitDirtyCleanupMs,
                initDiagEmitDirtyCount,
                initDiagRequestFlushMs,
                raylibInitBridgeSyncMs,
                tickMs,
                bridgeSyncMs,
                totalTickDiagMs,
                presentationDiagMs,
                simulationDiagMs,
                cameraCullingDiagMs,
                cameraCullingEntityProcessDiagMs,
                cameraCullingPerformerSyncDiagMs,
                behaviorDiagMs,
                animatorDiagMs,
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

            throw new InvalidOperationException($"Performer definition '{id}' was not found.");
        }

        private static int CountBenchmarkEntities(GameEngine engine)
        {
            int count = 0;
            var query = new Arch.Core.QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, BenchmarkEntityName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountPerformers(GameEngine engine, int definitionId)
        {
            int count = 0;
            var query = new Arch.Core.QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in query, (ref PerformerState state) =>
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
            sb.AppendLine("# Performer Mesh ISM Production Path Benchmark");
            sb.AppendLine();
            sb.AppendLine("- template: `blacksmith_mesh_benchmark_entity`");
            sb.AppendLine("- performer rule: `EntitySpawned -> blacksmith_mesh_benchmark_ism`");
            sb.AppendLine("- mesh: `blacksmith.building.north.intact`");
            sb.AppendLine("- render path: `InstancedStaticMesh` through `RaylibIsmRenderBridge.SyncPersistentLanes`");
            sb.AppendLine("- init excludes: real GPU draw call timing; validates production create/first emit plus latest raylib ISM bridge bucketing");
            sb.AppendLine("- tick excludes: real GPU draw call timing; validates stable production tick plus raylib bridge resync cost after initialization");
            sb.AppendLine("- stable tick sampling starts after explicit post-init GC cleanup and warmup frames so init debt does not pollute steady-state numbers");
            sb.AppendLine();
            sb.AppendLine("## Init");
            sb.AppendLine();
            sb.AppendLine("| Count | Enqueue | Create+First Emit | First Tick | Validation Scans | Raylib Initial Sync | Settle Frames | Entities | Performers | ISM Primitives | Raylib Buckets |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.EnqueueMs:F4} ms | {r.CreateAndFirstEmitMs:F4} ms | {r.FirstTickMs:F4} ms | {r.ValidationScansMs:F4} ms | {r.RaylibInitBridgeSyncMs:F4} ms | {r.SettleFrames} | {r.EntityCount} | {r.PerformerCount} | {r.IsmPrimitiveCount} | {r.RaylibBucketCount} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Init Breakdown");
            sb.AppendLine();
            sb.AppendLine("| Count | init diag Total Tick | init diag Presentation | init diag Simulation | init diag Camera Culling | init cull entity | init cull performer sync | init diag Behavior | init diag Animator | init diag Emit | init diag Emit Process | init diag Emit Cleanup | init dirty performers | init diag Request Flush |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {r.InitDiagTotalTickMs:F4} ms | {r.InitDiagPresentationMs:F4} ms | {r.InitDiagSimulationMs:F4} ms | {r.InitDiagCameraCullingMs:F4} ms | {r.InitDiagCameraCullingEntityProcessMs:F4} ms | {r.InitDiagCameraCullingPerformerSyncMs:F4} ms | {r.InitDiagBehaviorMs:F4} ms | {r.InitDiagAnimatorMs:F4} ms | {r.InitDiagEmitMs:F4} ms | {r.InitDiagEmitDirtyProcessMs:F4} ms | {r.InitDiagEmitDirtyCleanupMs:F4} ms | {r.InitDiagEmitDirtyCount} | {r.InitDiagRequestFlushMs:F4} ms |");
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
            sb.AppendLine("| Count | diag Total Tick | diag Presentation | diag Simulation | diag Camera Culling | diag cull entity | diag cull performer sync | diag Behavior | diag Animator | diag Emit | diag Request Flush |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.Count} | {Average(r.DiagTotalTickMs):F4} ms | {Average(r.DiagPresentationMs):F4} ms | {Average(r.DiagSimulationMs):F4} ms | {Average(r.DiagCameraCullingMs):F4} ms | {Average(r.DiagCameraCullingEntityProcessMs):F4} ms | {Average(r.DiagCameraCullingPerformerSyncMs):F4} ms | {Average(r.DiagBehaviorMs):F4} ms | {Average(r.DiagAnimatorMs):F4} ms | {Average(r.DiagEmitMs):F4} ms | {Average(r.DiagRequestFlushMs):F4} ms |");
            }

            sb.AppendLine();
            for (int i = 0; i < results.Length; i++)
            {
                MeshIsmBenchmarkResult r = results[i];
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- {r.Count}: init create+emit per entity `{(r.CreateAndFirstEmitMs / Math.Max(1, r.Count)):F6} ms`, first tick `{r.FirstTickMs:F4} ms`, validation scans `{r.ValidationScansMs:F4} ms`, init diag emit `{r.InitDiagEmitMs:F4} ms`, dirty emit process `{r.InitDiagEmitDirtyProcessMs:F4} ms`, dirty emit cleanup `{r.InitDiagEmitDirtyCleanupMs:F4} ms`, init diag request flush `{r.InitDiagRequestFlushMs:F4} ms`, init diag culling `{r.InitDiagCameraCullingMs:F4} ms`, init cull entity `{r.InitDiagCameraCullingEntityProcessMs:F4} ms`, init cull performer sync `{r.InitDiagCameraCullingPerformerSyncMs:F4} ms`, initial bridge sync per primitive `{(r.RaylibInitBridgeSyncMs / Math.Max(1, r.IsmPrimitiveCount)):F6} ms`, stable avg tick per entity `{(Average(r.TickMs) / Math.Max(1, r.Count)):F6} ms`, drops events `{r.EventDrops}` commands `{r.CommandDrops}` primitives `{r.PrimitiveDrops}`");
            }

            return sb.ToString();
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
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
            int PerformerCount,
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
            float InitDiagCameraCullingPerformerSyncMs,
            float InitDiagBehaviorMs,
            float InitDiagAnimatorMs,
            float InitDiagEmitMs,
            float InitDiagEmitDirtyProcessMs,
            float InitDiagEmitDirtyCleanupMs,
            int InitDiagEmitDirtyCount,
            float InitDiagRequestFlushMs,
            double RaylibInitBridgeSyncMs,
            double[] TickMs,
            double[] BridgeSyncTickMs,
            double[] DiagTotalTickMs,
            double[] DiagPresentationMs,
            double[] DiagSimulationMs,
            double[] DiagCameraCullingMs,
            double[] DiagCameraCullingEntityProcessMs,
            double[] DiagCameraCullingPerformerSyncMs,
            double[] DiagBehaviorMs,
            double[] DiagAnimatorMs,
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
