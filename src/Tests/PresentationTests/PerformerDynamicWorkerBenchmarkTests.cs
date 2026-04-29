using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Commands;
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
    public sealed class PerformerDynamicWorkerBenchmarkTests
    {
        private const int WarmupFrames = 8;
        private const int MeasuredFrames = 90;
        private static readonly int[] Counts = { 3_000, 10_000 };

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
                Assert.That(result.SkinnedCount, Is.EqualTo(result.Count));
                Assert.That(result.GpuSkinnedCount, Is.EqualTo(result.Count));
                Assert.That(result.WalkingSkinnedStateCount, Is.EqualTo(result.Count), "Dynamic workers must render the configured walking packed animator state.");
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

        private static DynamicWorkerBenchmarkResult RunScenario(int count)
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            PerformerBlacksmithShowcaseTestHarness.LoadMap(
                engine,
                PerformerBlacksmithShowcaseIds.DynamicWorkerBenchmarkMapId,
                frames: 2);

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

            int enqueued = EnqueueDynamicWorkers(queue, engine.CurrentMapSession?.MapId ?? default, count, out double enqueueMs);

            long firstTickStart = Stopwatch.GetTimestamp();
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
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

        private static int EnqueueDynamicWorkers(RuntimeEntitySpawnQueue queue, MapId mapId, int count, out double elapsedMs)
        {
            RuntimeEntitySpawnRequest[] requests = new RuntimeEntitySpawnRequest[count];
            int side = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            float spacing = 25f;
            float origin = -side * spacing * 0.5f;
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                int x = i % side;
                int y = i / side;
                requests[i] = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = PerformerBlacksmithShowcaseIds.DynamicWorkerTemplateId,
                    MapId = mapId,
                    WorldPositionCm = Fix64Vec2.FromFloat(origin + x * spacing, origin + y * spacing),
                    HasWorldPosition = 1,
                    FacingAngleRad = 0f,
                    HasFacing = 1,
                };
            }

            int enqueued = queue.EnqueueMany(requests);
            elapsedMs = ElapsedMs(start);
            return enqueued;
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
