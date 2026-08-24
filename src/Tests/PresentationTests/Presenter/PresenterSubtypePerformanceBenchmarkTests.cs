using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    [Category("benchmark")]
    public sealed class PresenterSubtypePerformanceBenchmarkTests
    {
        private const int CountPerSubtype = 2_000;
        private const int MeasuredFrames = 60;

        [Test]
        public void Benchmark_PresenterSubtypeRetainedAndStaticLanes_WritesReport()
        {
            using World world = World.Create();
            var runtime = new PresenterEntityRuntime(world);
            var definitions = new PresenterDefinitionRegistry();
            var requests = new PresentationRequestBuffer(64_000);
            var stableDrawCache = new StableDrawCache(64_000);
            var stableIds = new PresentationStableIdAllocator();
            var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 64_000);
            var timings = new PresentationTimingDiagnostics { SystemBreakdownEnabled = true };

            int decalDefId = RegisterStaticVisual(definitions, "subtype.static.decal", AssetKind.Decal, 101, 201);
            int vfxDefId = RegisterStaticVisual(definitions, "subtype.static.vfx", AssetKind.VFX, 102, 202);
            int splineDefId = RegisterRetainedAsset(definitions, "subtype.retained.spline", AssetKind.Spline, 103);
            int overlayDefId = RegisterRetainedAsset(definitions, "subtype.retained.ground_overlay", AssetKind.GroundOverlay, (int)GroundOverlayShape.Circle);
            int surfaceDefId = definitions.Register("subtype.surface.source", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 12,
                        Kind = BehaviorKind.SurfaceSource,
                        ActiveByDefault = true,
                        SurfaceSource = new SurfaceAuthoringBlock
                        {
                            Kind = PresenterSurfaceKind.SplineRibbon,
                            LodProfileId = "default_surface_lod",
                            MaterialSet = new PresenterSurfaceMaterialSet { PrimaryMaterialId = "default_surface" },
                        },
                    },
                ],
            });

            long createStart = Stopwatch.GetTimestamp();
            CreateSubtypeBatch(world, runtime, definitions, decalDefId, CountPerSubtype, stableIdBase: 100_000);
            CreateSubtypeBatch(world, runtime, definitions, vfxDefId, CountPerSubtype, stableIdBase: 200_000);
            CreateSubtypeBatch(world, runtime, definitions, splineDefId, CountPerSubtype, stableIdBase: 300_000);
            CreateSubtypeBatch(world, runtime, definitions, overlayDefId, CountPerSubtype, stableIdBase: 400_000);
            CreateSubtypeBatch(world, runtime, definitions, surfaceDefId, CountPerSubtype, stableIdBase: 500_000);
            double createMs = ElapsedMs(createStart);

            using var emit = new PresenterEmitSystem(
                world,
                runtime,
                definitions,
                requests,
                new System.Collections.Generic.Dictionary<string, object>(),
                animatorStates: null!,
                soundRequests: null!,
                timingDiagnostics: timings,
                stableDrawCache: stableDrawCache,
                visualStableIds: visualStableIds);

            long firstEmitStart = Stopwatch.GetTimestamp();
            emit.Update(0.016f);
            double firstEmitMs = ElapsedMs(firstEmitStart);
            int firstRequestCount = requests.Count;
            int firstStableCacheCount = stableDrawCache.Count;
            int firstContentRevision = stableDrawCache.ContentRevision;

            requests.Clear();
            double[] frameMs = new double[MeasuredFrames];
            double[] emitMs = new double[MeasuredFrames];
            int[] requestCounts = new int[MeasuredFrames];
            int[] stableCacheCounts = new int[MeasuredFrames];
            int[] contentRevisions = new int[MeasuredFrames];
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                long frameStart = Stopwatch.GetTimestamp();
                emit.Update(0.016f);
                frameMs[frame] = ElapsedMs(frameStart);
                emitMs[frame] = timings.LastPresenterEmitMs;
                requestCounts[frame] = requests.Count;
                stableCacheCounts[frame] = stableDrawCache.Count;
                contentRevisions[frame] = stableDrawCache.ContentRevision;
                requests.Clear();
            }

            SubtypeBenchmarkResult result = new(
                TotalPresenters: CountPerSubtype * 5,
                CountPerSubtype,
                CreateMs: createMs,
                FirstEmitMs: firstEmitMs,
                FirstRequestCount: firstRequestCount,
                FirstStableCacheCount: firstStableCacheCount,
                FirstContentRevision: firstContentRevision,
                FrameMs: frameMs,
                EmitMs: emitMs,
                RequestCounts: requestCounts,
                StableCacheCounts: stableCacheCounts,
                ContentRevisions: contentRevisions);

            WriteReport(result);

            Assert.That(result.FirstRequestCount, Is.EqualTo(CountPerSubtype * 3), "Spline, GroundOverlay, and SurfaceSource should emit once on first frame.");
            Assert.That(result.FirstStableCacheCount, Is.EqualTo(CountPerSubtype * 2), "Decal and VFX should enter StableDrawCache.");
            Assert.That(Max(result.RequestCounts), Is.EqualTo(0), "Retained subtypes must not re-emit steady-state requests.");
            Assert.That(Min(result.StableCacheCounts), Is.EqualTo(CountPerSubtype * 2));
            Assert.That(Max(result.StableCacheCounts), Is.EqualTo(CountPerSubtype * 2));
            Assert.That(Max(result.ContentRevisions), Is.EqualTo(result.FirstContentRevision), "Static cache content must stay unchanged in steady state.");
        }

        private static int RegisterStaticVisual(
            PresenterDefinitionRegistry definitions,
            string key,
            AssetKind kind,
            int assetId,
            int materialId)
        {
            return definitions.Register(key, new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = kind,
                            AssetId = assetId,
                            MaterialId = materialId,
                            Mobility = VisualMobility.Static,
                            RenderPath = VisualRenderPath.StaticMesh,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });
        }

        private static int RegisterRetainedAsset(
            PresenterDefinitionRegistry definitions,
            string key,
            AssetKind kind,
            int assetId)
        {
            return definitions.Register(key, new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = kind,
                            AssetId = assetId,
                            Mobility = VisualMobility.Movable,
                            RenderPath = VisualRenderPath.None,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });
        }

        private static void CreateSubtypeBatch(
            World world,
            PresenterEntityRuntime runtime,
            PresenterDefinitionRegistry definitions,
            int definitionId,
            int count,
            int stableIdBase)
        {
            PresenterDefinition definition = definitions.Get(definitionId);
            int side = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
            for (int i = 0; i < count; i++)
            {
                Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                int x = i % side;
                int y = i / side;
                Vector3 position = new(x * 1.5f, 0f, y * 1.5f);
                Entity presenter = runtime.Create(definitionId, owner, i, PresentationAnchorKind.WorldPosition, position, stableIdBase + i, Entity.Null, definition);
                world.Get<PresenterState>(presenter).BehaviorActiveMask = 1u;
            }
        }

        private static void WriteReport(in SubtypeBenchmarkResult result)
        {
            string artifactDir = Path.Combine(
                PresenterBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "presenter-subtype-retained-static-lanes");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            var sb = new StringBuilder();
            sb.AppendLine("# Presenter Subtype Retained/Static Lane Benchmark");
            sb.AppendLine();
            sb.AppendLine("- subtypes: `Decal`, `VFX`, `Spline`, `GroundOverlay`, `SurfaceSource`");
            sb.AppendLine("- production path: `PresenterEntityRuntime` -> `PresenterEmitSystem` -> `StableDrawCache` / `PresentationRequestBuffer`");
            sb.AppendLine("- steady-state requirement: retained subtypes emit no unchanged requests; static subtypes do not rewrite stable cache");
            sb.AppendLine();
            sb.AppendLine("| Total | Each subtype | Create | First Emit | First Requests | Stable Cache | Avg Tick | P95 Tick | Avg Emit | Max Steady Requests | Stable Cache Min/Max | Content Revision First/Max |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.TotalPresenters} | {result.CountPerSubtype} | {result.CreateMs:F4} ms | {result.FirstEmitMs:F4} ms | {result.FirstRequestCount} | {result.FirstStableCacheCount} | {Average(result.FrameMs):F4} ms | {Percentile(result.FrameMs, 0.95):F4} ms | {Average(result.EmitMs):F4} ms | {Max(result.RequestCounts)} | {Min(result.StableCacheCounts)} / {Max(result.StableCacheCounts)} | {result.FirstContentRevision} / {Max(result.ContentRevisions)} |");
            File.WriteAllText(reportPath, sb.ToString());
            TestContext.Out.WriteLine(sb.ToString());
        }

        private static double ElapsedMs(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

        private static double Average(double[] values)
        {
            double sum = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return values.Length == 0 ? 0d : sum / values.Length;
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

        private static int Min(int[] values)
        {
            int min = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < min)
                {
                    min = values[i];
                }
            }

            return values.Length == 0 ? 0 : min;
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

        private readonly record struct SubtypeBenchmarkResult(
            int TotalPresenters,
            int CountPerSubtype,
            double CreateMs,
            double FirstEmitMs,
            int FirstRequestCount,
            int FirstStableCacheCount,
            int FirstContentRevision,
            double[] FrameMs,
            double[] EmitMs,
            int[] RequestCounts,
            int[] StableCacheCounts,
            int[] ContentRevisions);
    }
}
