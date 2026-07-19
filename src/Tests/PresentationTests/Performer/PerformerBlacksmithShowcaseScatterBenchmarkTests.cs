using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using PerformerBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PerformerBlacksmithShowcaseScatterBenchmarkTests
    {
        private const int DeclaredWarmupFrames = 12;
        private const int DeclaredMeasuredFrames = 120;

        private static int WarmupFrames => ReadPositiveEnvIntOrDeclaredDefault("LUDOTS_BLACKSMITH_BENCH_WARMUP_FRAMES", DeclaredWarmupFrames);
        private static int MeasuredFrames => ReadPositiveEnvIntOrDeclaredDefault("LUDOTS_BLACKSMITH_BENCH_MEASURED_FRAMES", DeclaredMeasuredFrames);

        private static readonly ScatterScenario[] Scenarios =
        {
            new("scatter_25", 25, 24681357, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_100", 100, 97531864, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_1000", 1000, 41592653, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_3000_tight", 3000, 14142135, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_5000", 5000, 27182818, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_10000_tight", 10000, 17320508, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_30000_tight", 30000, 31415926, 750f, 2400f, ExpectFullVisibility: true),
            new("scatter_30000_wide", 30000, 16180339, 5000f, 12000f, ExpectFullVisibility: false),
        };

        [Test]
        public void Benchmark_ScatterBlacksmithShowcase_WritesReportAndValidatesCounts()
        {
            ScatterScenario[] scenarios = ResolveScenarios();
            var results = new ScatterScenarioResult[scenarios.Length];
            for (int i = 0; i < scenarios.Length; i++)
            {
                results[i] = RunScenario(scenarios[i]);
            }

            string artifactDir = Path.Combine(
                PerformerBlacksmithShowcaseTestHarness.FindRepoRoot(),
                "artifacts",
                "benchmarks",
                "performer-blacksmith-showcase-scatter");
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "benchmark-report.md");
            string tracePath = Path.Combine(artifactDir, "trace.jsonl");

            File.WriteAllText(reportPath, BuildReport(results));
            File.WriteAllText(tracePath, BuildTrace(results));

            TestContext.Out.WriteLine(File.ReadAllText(reportPath));

            Assert.That(File.Exists(reportPath), Is.True);
            Assert.That(File.Exists(tracePath), Is.True);

            for (int i = 0; i < results.Length; i++)
            {
                ScatterScenarioResult result = results[i];
                Assert.That(result.QueuedExtraBuildings, Is.EqualTo(result.TotalBuildings - 1), $"{result.Name}: queued extra buildings mismatch.");
                Assert.That(result.BlacksmithEntities, Is.EqualTo(result.TotalBuildings), $"{result.Name}: blacksmith entity count mismatch.");
                Assert.That(result.RootPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: root performer count mismatch.");
                Assert.That(result.WorkshopLeftPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: left workshop performer count mismatch.");
                Assert.That(result.WorkshopRightPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: right workshop performer count mismatch.");
                Assert.That(result.ChimneyPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: chimney performer count mismatch.");
                Assert.That(result.RouteSplinePerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: route spline performer count mismatch.");
                Assert.That(result.DecalPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: decal performer count mismatch.");
                Assert.That(result.WorkerPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: worker performer count mismatch.");
                Assert.That(result.BarPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: world HUD bar performer count mismatch.");
                Assert.That(result.TextPerformerCount, Is.EqualTo(result.TotalBuildings), $"{result.Name}: world HUD text performer count mismatch.");
                int expectedVisibleOwners = result.ExpectFullVisibility ? result.TotalBuildings : result.VisibleBlacksmithEntities;
                Assert.That(result.VisibleBlacksmithEntities, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: visible blacksmith entity count mismatch.");
                Assert.That(result.VisibleWorkshopPrimitives, Is.EqualTo(expectedVisibleOwners * 2), $"{result.Name}: visible workshop primitive count mismatch.");
                Assert.That(result.VisibleChimneyPrimitives, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: visible chimney primitive count mismatch.");
                Assert.That(result.WorldHudBarCount, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: world HUD bar count mismatch.");
                Assert.That(result.WorldHudTextCount, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: world HUD text count mismatch.");
                Assert.That(result.RoadSplineCount, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: road spline count mismatch.");
                Assert.That(result.GroundOverlayCount, Is.EqualTo(expectedVisibleOwners), $"{result.Name}: decal count mismatch.");
                if (!result.ExpectFullVisibility)
                {
                    Assert.That(result.VisibleBlacksmithEntities, Is.GreaterThan(0), $"{result.Name}: wide scenario should still keep a visible subset.");
                    Assert.That(result.VisibleBlacksmithEntities, Is.LessThan(result.TotalBuildings), $"{result.Name}: wide scenario should prove culling by hiding some roots.");
                }
                Assert.That(result.PresentationEventDrops, Is.EqualTo(0), $"{result.Name}: presentation events should not drop.");
                Assert.That(result.PerformerCommandDrops, Is.EqualTo(0), $"{result.Name}: performer commands should not drop.");
                Assert.That(result.PrimitiveDrops, Is.EqualTo(0), $"{result.Name}: primitive buffer should not drop.");
                Assert.That(result.WorldHudDrops, Is.EqualTo(0), $"{result.Name}: world HUD buffer should not drop.");
                Assert.That(result.ScreenHudDrops, Is.EqualTo(0), $"{result.Name}: screen HUD buffer should not drop.");
                Assert.That(result.SkinnedDrops, Is.EqualTo(0), $"{result.Name}: skinned buffer should not drop.");
            }
        }

        [Test]
        public void ScatterHudTextBenchmarkMap_AppliesMetadataInitialTarget()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PerformerBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);

            engine.LoadMap(PerformerBlacksmithShowcaseIds.ScatterHudTextBenchmarkMapId);

            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");
            int expected = engine.CurrentMapSession!.MapConfig.Metadata["performerBlacksmith"]!["scatterInitialTarget"]!.GetValue<int>();

            Assert.That(expected, Is.EqualTo(30000));
            Assert.That(spawnQueue.Count, Is.EqualTo(expected),
                "The dedicated HUD-text benchmark map must use its metadata scatterInitialTarget as the runtime spawn SSOT.");

            InitializationPhaseResult init = WaitForInitialization(engine, hudProjection);
            Assert.That(init.QueueCountAfterSettle, Is.EqualTo(0));
            Assert.That(init.StableFramesReached, Is.True);

            CountHudTextBenchmark(
                engine,
                out int entities,
                out int rootPerformers,
                out int worldHudBars,
                out int worldHudText,
                out int screenHudBars,
                out int screenHudText);

            Assert.That(entities, Is.EqualTo(expected));
            Assert.That(rootPerformers, Is.EqualTo(expected));
            Assert.That(worldHudBars, Is.EqualTo(expected));
            Assert.That(worldHudText, Is.EqualTo(expected));
            Assert.That(screenHudBars, Is.EqualTo(expected));
            Assert.That(screenHudText, Is.EqualTo(expected));
        }

        [Test]
        public void Scatter_3000_ProductionPathRandomDrift_PropagatesToIsmAndScreenHud()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PerformerBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int queued = PerformerBlacksmithShowcaseTestHarness.EnqueueScatter(
                engine,
                totalBuildings: 3000,
                seed: 31415926,
                minRadiusCm: 750f,
                maxRadiusCm: 2400f);
            Assert.That(queued, Is.EqualTo(2999));

            PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 24);

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
            int durabilityAttributeId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Durability");
            int randomDriftEffectId = Ludots.Core.Gameplay.GAS.Registry.EffectTemplateIdRegistry.GetId("Effect.Showcase.Blacksmith.RandomDrift");

            int northAssetId = meshes.GetId("blacksmith.building.north.intact");
            int southAssetId = meshes.GetId("blacksmith.building.south.intact");
            int damagedAssetId = meshes.GetId("blacksmith.building.damaged");
            int ruinedAssetId = meshes.GetId("blacksmith.building.ruined");
            int chimneyAssetId = meshes.GetId("blacksmith.furnace");

            int workshopPrimitiveCount = 0;
            int chimneyPrimitiveCount = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                Assert.That(item.RenderPath, Is.EqualTo(VisualRenderPath.InstancedStaticMesh),
                    "Crowd showcase structures must remain on the production ISM lane.");

                if (item.MeshAssetId == northAssetId ||
                    item.MeshAssetId == southAssetId ||
                    item.MeshAssetId == damagedAssetId ||
                    item.MeshAssetId == ruinedAssetId)
                {
                    workshopPrimitiveCount++;
                }

                if (item.MeshAssetId == chimneyAssetId)
                {
                    chimneyPrimitiveCount++;
                }
            }

            Assert.That(workshopPrimitiveCount, Is.EqualTo(6000));
            Assert.That(chimneyPrimitiveCount, Is.EqualTo(3000));

            int blacksmithAttributeCount = 0;
            int blacksmithWithActiveEffects = 0;
            float minDurability = float.MaxValue;
            float maxDurability = float.MinValue;
            var attributeQuery = new QueryDescription().WithAll<Name, AttributeBuffer>();
            engine.World.Query(in attributeQuery, (Entity entity, ref Name name, ref AttributeBuffer attributes) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    return;
                }

                blacksmithAttributeCount++;
                float current = attributes.GetCurrent(durabilityAttributeId);
                minDurability = MathF.Min(minDurability, current);
                maxDurability = MathF.Max(maxDurability, current);

                if (!engine.World.Has<ActiveEffectContainer>(entity))
                {
                    return;
                }

                ref ActiveEffectContainer activeEffects = ref engine.World.Get<ActiveEffectContainer>(entity);
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    Entity effectEntity = activeEffects.GetEntity(i);
                    if (!engine.World.IsAlive(effectEntity) || !engine.World.Has<EffectTemplateRef>(effectEntity))
                    {
                        continue;
                    }

                    if (engine.World.Get<EffectTemplateRef>(effectEntity).TemplateId == randomDriftEffectId)
                    {
                        blacksmithWithActiveEffects++;
                        break;
                    }
                }
            });

            TestContext.Out.WriteLine(
                $"Blacksmith durability spread: count={blacksmithAttributeCount}, min={minDurability:F2}, max={maxDurability:F2}, activeRandomDrift={blacksmithWithActiveEffects}");

            int worldBarCount = 0;
            int worldTextCount = 0;
            float minBar = float.MaxValue;
            float maxBar = float.MinValue;
            float minCurrent = float.MaxValue;
            float maxCurrent = float.MinValue;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    worldBarCount++;
                    minBar = MathF.Min(minBar, item.Value0);
                    maxBar = MathF.Max(maxBar, item.Value0);
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    worldTextCount++;
                    minCurrent = MathF.Min(minCurrent, item.Value0);
                    maxCurrent = MathF.Max(maxCurrent, item.Value0);
                }
            }

            Assert.That(worldBarCount, Is.EqualTo(3000));
            Assert.That(worldTextCount, Is.EqualTo(3000));
            Assert.That(maxBar - minBar, Is.GreaterThan(0.01f),
                "Random durability effect should produce a spread of bar values across the crowd.");
            Assert.That(maxCurrent - minCurrent, Is.GreaterThan(0.5f),
                "Random durability effect should produce a spread of text current values across the crowd.");

            Assert.That(screenHud.BarCount, Is.EqualTo(3000));
            Assert.That(screenHud.TextCount, Is.EqualTo(3000));

            float minScreenBar = float.MaxValue;
            float maxScreenBar = float.MinValue;
            foreach (ref readonly ScreenHudBarItem item in screenHud.GetBarSpan())
            {
                minScreenBar = MathF.Min(minScreenBar, item.Value0);
                maxScreenBar = MathF.Max(maxScreenBar, item.Value0);
            }

            float minScreenCurrent = float.MaxValue;
            float maxScreenCurrent = float.MinValue;
            foreach (ref readonly ScreenHudTextItem item in screenHud.GetTextSpan())
            {
                minScreenCurrent = MathF.Min(minScreenCurrent, item.Value0);
                maxScreenCurrent = MathF.Max(maxScreenCurrent, item.Value0);
            }

            Assert.That(maxScreenBar - minScreenBar, Is.GreaterThan(0.01f));
            Assert.That(maxScreenCurrent - minScreenCurrent, Is.GreaterThan(0.5f));
            Assert.That(worldHud.DroppedTotal, Is.EqualTo(0));
            Assert.That(screenHud.DroppedTotal, Is.EqualTo(0));
        }

        [Test]
        public void Scatter_30000_ProductionPathRandomDrift_PublishesEffectForEverySpawnedEntity()
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PerformerBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int queued = PerformerBlacksmithShowcaseTestHarness.EnqueueScatter(
                engine,
                totalBuildings: 30000,
                seed: 16180339,
                minRadiusCm: 5000f,
                maxRadiusCm: 12000f);
            Assert.That(queued, Is.EqualTo(29999));

            PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 90);

            var effectQueue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue missing.");
            int randomDriftEffectId = Ludots.Core.Gameplay.GAS.Registry.EffectTemplateIdRegistry.GetId("Effect.Showcase.Blacksmith.RandomDrift");
            int blacksmithCount = 0;
            int blacksmithWithRandomDrift = 0;
            var attributeQuery = new QueryDescription().WithAll<Name, AttributeBuffer>();
            engine.World.Query(in attributeQuery, (Entity entity, ref Name name, ref AttributeBuffer _) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    return;
                }

                blacksmithCount++;
                if (!engine.World.Has<ActiveEffectContainer>(entity))
                {
                    return;
                }

                ref ActiveEffectContainer activeEffects = ref engine.World.Get<ActiveEffectContainer>(entity);
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    Entity effectEntity = activeEffects.GetEntity(i);
                    if (!engine.World.IsAlive(effectEntity) || !engine.World.Has<EffectTemplateRef>(effectEntity))
                    {
                        continue;
                    }

                    if (engine.World.Get<EffectTemplateRef>(effectEntity).TemplateId == randomDriftEffectId)
                    {
                        blacksmithWithRandomDrift++;
                        break;
                    }
                }
            });

            TestContext.Out.WriteLine(
                $"30K random drift coverage: blacksmiths={blacksmithCount}, activeRandomDrift={blacksmithWithRandomDrift}, fxQueue={effectQueue.Count}, fxOverflow={effectQueue.OverflowCount}, fxDropped={effectQueue.DroppedCount}, fxCapacity={effectQueue.Capacity}");

            Assert.That(blacksmithCount, Is.EqualTo(30000));
            Assert.That(blacksmithWithRandomDrift, Is.EqualTo(30000));
            Assert.That(effectQueue.DroppedCount, Is.EqualTo(0));
        }

        [Test]
        public void BenchmarkFrameEnvOverride_UnsetUsesDeclaredDefault_InvalidValueFailsFast()
        {
            const string key = "LUDOTS_BLACKSMITH_BENCH_WARMUP_FRAMES";
            string? previous = Environment.GetEnvironmentVariable(key);
            try
            {
                Environment.SetEnvironmentVariable(key, null);
                Assert.That(WarmupFrames, Is.EqualTo(DeclaredWarmupFrames));

                Environment.SetEnvironmentVariable(key, " ");
                Assert.Throws<InvalidOperationException>(() => { _ = WarmupFrames; });

                Environment.SetEnvironmentVariable(key, "0");
                Assert.Throws<InvalidOperationException>(() => { _ = WarmupFrames; });

                Environment.SetEnvironmentVariable(key, "abc");
                Assert.Throws<InvalidOperationException>(() => { _ = WarmupFrames; });
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, previous);
            }
        }

        private static ScatterScenarioResult RunScenario(ScatterScenario scenario)
        {
            using var engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine();
            var hudProjection = PerformerBlacksmithShowcaseTestHarness.CreateHeadlessHudProjection(engine);
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, PerformerBlacksmithShowcaseIds.ShowcaseMapId, frames: 8);

            int queued = PerformerBlacksmithShowcaseTestHarness.EnqueueScatter(
                engine,
                scenario.TotalBuildings,
                scenario.Seed,
                scenario.MinRadiusCm,
                scenario.MaxRadiusCm);

            InitializationPhaseResult initialization = WaitForInitialization(engine, hudProjection);

            SnapshotCounts(
                engine,
                out int blacksmithEntities,
                out int rootPerformerCount,
                out int workshopLeftPerformerCount,
                out int workshopRightPerformerCount,
                out int chimneyPerformerCount,
                out int routeSplinePerformerCount,
                out int decalPerformerCount,
                out int workerPerformerCount,
                out int barPerformerCount,
                out int textPerformerCount,
                out int visibleBlacksmithEntities,
                out int visibleWorkshopPrimitives,
                out int visibleChimneyPrimitives,
                out int worldHudBarCount,
                out int worldHudTextCount,
                out int roadSplineCount,
                out int groundOverlayCount);

            PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, WarmupFrames);

            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics)
                ?? throw new InvalidOperationException("PresentationTimingDiagnostics missing.");
            timings.SystemBreakdownEnabled = true;
            var eventStream = engine.GetService(CoreServiceKeys.PresentationEventStream)
                ?? throw new InvalidOperationException("PresentationEventStream missing.");
            var commandBuffer = engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
                ?? throw new InvalidOperationException("PerformerCommandBuffer missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");
            var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer)
                ?? throw new InvalidOperationException("PresentationSkinnedVisualBatchBuffer missing.");

            double[] frameTotals = new double[MeasuredFrames];
            double[] simulationMs = new double[MeasuredFrames];
            double[] presentationMs = new double[MeasuredFrames];
            double[] cullingMs = new double[MeasuredFrames];
            double[] behaviorMs = new double[MeasuredFrames];
            double[] animatorMs = new double[MeasuredFrames];
            double[] emitMs = new double[MeasuredFrames];
            double[] requestFlushMs = new double[MeasuredFrames];
            double[] hudProjectionMs = new double[MeasuredFrames];
            string[] presentationTop1Names = new string[MeasuredFrames];
            double[] presentationTop1Ms = new double[MeasuredFrames];
            string[] simulationTop1Names = new string[MeasuredFrames];
            double[] simulationTop1Ms = new double[MeasuredFrames];
            int[] performerBootstrapCounts = new int[MeasuredFrames];
            int[] performerOwnerChanges = new int[MeasuredFrames];
            int[] performerOwnerAttributeChanges = new int[MeasuredFrames];
            int[] performerOwnerTagChanges = new int[MeasuredFrames];
            int[] performerTickDrivenCounts = new int[MeasuredFrames];
            int[] performerActiveSoundTrackingCounts = new int[MeasuredFrames];
            int[] performerDestroyEventScanCounts = new int[MeasuredFrames];
            int[] visibleEntities = new int[MeasuredFrames];
            int[] primitiveInstances = new int[MeasuredFrames];
            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                long start = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 1);
                frameTotals[frame] = ElapsedMs(start);
                simulationMs[frame] = timings.LastSimulationMs;
                presentationMs[frame] = timings.LastPresentationMs;
                cullingMs[frame] = timings.LastCameraCullingMs;
                behaviorMs[frame] = timings.LastPerformerBehaviorMs;
                animatorMs[frame] = timings.LastPerformerAnimatorMs;
                emitMs[frame] = timings.LastPerformerEmitMs;
                requestFlushMs[frame] = timings.LastPresentationRequestFlushMs;
                hudProjectionMs[frame] = timings.LastWorldHudProjectionMs;
                presentationTop1Names[frame] = timings.LastPresentationTopSystem1Name;
                presentationTop1Ms[frame] = timings.LastPresentationTopSystem1Ms;
                simulationTop1Names[frame] = timings.LastSimulationTopSystem1Name;
                simulationTop1Ms[frame] = timings.LastSimulationTopSystem1Ms;
                performerBootstrapCounts[frame] = timings.PerformerBootstrapCountLastFrame;
                performerOwnerChanges[frame] = timings.PerformerOwnerChangesLastFrame;
                performerOwnerAttributeChanges[frame] = timings.PerformerOwnerAttributeChangesLastFrame;
                performerOwnerTagChanges[frame] = timings.PerformerOwnerTagChangesLastFrame;
                performerTickDrivenCounts[frame] = timings.PerformerTickDrivenCountLastFrame;
                performerActiveSoundTrackingCounts[frame] = timings.PerformerActiveSoundTrackingCountLastFrame;
                performerDestroyEventScanCounts[frame] = timings.PerformerDestroyEventScanCountLastFrame;
                visibleEntities[frame] = timings.VisibleEntitiesLastFrame;
                primitiveInstances[frame] = primitives.Count;
            }

            return new ScatterScenarioResult(
                scenario.Name,
                scenario.TotalBuildings,
                scenario.Seed,
                scenario.MinRadiusCm,
                scenario.MaxRadiusCm,
                scenario.ExpectFullVisibility,
                queued,
                initialization.Frames,
                initialization.TotalMs,
                initialization.MaxFrameMs,
                initialization.QueueCountAfterSettle,
                initialization.StableFramesReached,
                blacksmithEntities,
                rootPerformerCount,
                workshopLeftPerformerCount,
                workshopRightPerformerCount,
                chimneyPerformerCount,
                routeSplinePerformerCount,
                decalPerformerCount,
                workerPerformerCount,
                barPerformerCount,
                textPerformerCount,
                visibleBlacksmithEntities,
                visibleWorkshopPrimitives,
                visibleChimneyPrimitives,
                worldHudBarCount,
                worldHudTextCount,
                roadSplineCount,
                groundOverlayCount,
                eventStream.DroppedTotal,
                commandBuffer.DroppedTotal,
                primitives.DroppedTotal,
                worldHud.DroppedTotal,
                screenHud.DroppedTotal,
                skinned.DroppedTotal,
                frameTotals,
                simulationMs,
                presentationMs,
                cullingMs,
                behaviorMs,
                animatorMs,
                emitMs,
                requestFlushMs,
                hudProjectionMs,
                presentationTop1Names,
                presentationTop1Ms,
                simulationTop1Names,
                simulationTop1Ms,
                performerBootstrapCounts,
                performerOwnerChanges,
                performerOwnerAttributeChanges,
                performerOwnerTagChanges,
                performerTickDrivenCounts,
                performerActiveSoundTrackingCounts,
                performerDestroyEventScanCounts,
                visibleEntities,
                primitiveInstances);
        }

        private static InitializationPhaseResult WaitForInitialization(
            Ludots.Core.Engine.GameEngine engine,
            WorldHudToScreenSystem hudProjection)
        {
            const int MaxInitializationFrames = 240;
            const int StableFrameTarget = 8;

            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("RuntimeEntitySpawnQueue missing.");

            int stableFrames = 0;
            int previousSignature = int.MinValue;
            double totalMs = 0d;
            double maxFrameMs = 0d;
            int executedFrames = 0;

            while (executedFrames < MaxInitializationFrames)
            {
                long start = Stopwatch.GetTimestamp();
                PerformerBlacksmithShowcaseTestHarness.TickWithHudProjection(engine, hudProjection, 1);
                double frameMs = ElapsedMs(start);
                totalMs += frameMs;
                if (frameMs > maxFrameMs)
                {
                    maxFrameMs = frameMs;
                }

                executedFrames++;

                SnapshotCounts(
                    engine,
                    out int blacksmithEntities,
                    out int rootPerformerCount,
                    out int workshopLeftPerformerCount,
                    out int workshopRightPerformerCount,
                    out int chimneyPerformerCount,
                    out int routeSplinePerformerCount,
                    out int decalPerformerCount,
                    out int workerPerformerCount,
                    out int barPerformerCount,
                    out int textPerformerCount,
                    out int visibleBlacksmithEntities,
                    out int visibleWorkshopPrimitives,
                    out int visibleChimneyPrimitives,
                    out int worldHudBarCount,
                    out int worldHudTextCount,
                    out int roadSplineCount,
                    out int groundOverlayCount);

                var signatureBuilder = new HashCode();
                signatureBuilder.Add(spawnQueue.Count);
                signatureBuilder.Add(blacksmithEntities);
                signatureBuilder.Add(rootPerformerCount);
                signatureBuilder.Add(workshopLeftPerformerCount);
                signatureBuilder.Add(workshopRightPerformerCount);
                signatureBuilder.Add(chimneyPerformerCount);
                signatureBuilder.Add(routeSplinePerformerCount);
                signatureBuilder.Add(decalPerformerCount);
                signatureBuilder.Add(workerPerformerCount);
                signatureBuilder.Add(barPerformerCount);
                signatureBuilder.Add(textPerformerCount);
                signatureBuilder.Add(visibleBlacksmithEntities);
                signatureBuilder.Add(visibleWorkshopPrimitives);
                signatureBuilder.Add(visibleChimneyPrimitives);
                signatureBuilder.Add(worldHudBarCount);
                signatureBuilder.Add(worldHudTextCount);
                signatureBuilder.Add(roadSplineCount);
                signatureBuilder.Add(groundOverlayCount);
                int signature = signatureBuilder.ToHashCode();

                if (spawnQueue.Count == 0 && signature == previousSignature)
                {
                    stableFrames++;
                    if (stableFrames >= StableFrameTarget)
                    {
                        break;
                    }
                }
                else
                {
                    stableFrames = 0;
                }

                previousSignature = signature;
            }

            return new InitializationPhaseResult(
                executedFrames,
                totalMs,
                maxFrameMs,
                spawnQueue.Count,
                stableFrames >= StableFrameTarget);
        }

        private static void SnapshotCounts(
            Ludots.Core.Engine.GameEngine engine,
            out int blacksmithEntities,
            out int rootPerformerCount,
            out int workshopLeftPerformerCount,
            out int workshopRightPerformerCount,
            out int chimneyPerformerCount,
            out int routeSplinePerformerCount,
            out int decalPerformerCount,
            out int workerPerformerCount,
            out int barPerformerCount,
            out int textPerformerCount,
            out int visibleBlacksmithEntities,
            out int visibleWorkshopPrimitives,
            out int visibleChimneyPrimitives,
            out int worldHudBarCount,
            out int worldHudTextCount,
            out int roadSplineCount,
            out int groundOverlayCount)
        {
            var performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime)
                ?? throw new InvalidOperationException("PerformerEntityRuntime missing.");
            var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
                ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
            var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var roadSplines = engine.GetService(CoreServiceKeys.RoadSplineBuffer)
                ?? throw new InvalidOperationException("RoadSplineBuffer missing.");
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");

            int rootId = definitions.GetId(PerformerBlacksmithShowcaseIds.RootDefinitionId);
            int leftId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int rightId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
            int routeId = definitions.GetId(PerformerBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = definitions.GetId(PerformerBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = definitions.GetId(PerformerBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = definitions.GetId(PerformerBlacksmithShowcaseIds.DurabilityTextDefinitionId);
            int northAssetId = meshes.GetId("blacksmith.building.north.intact");
            int southAssetId = meshes.GetId("blacksmith.building.south.intact");
            int damagedAssetId = meshes.GetId("blacksmith.building.damaged");
            int ruinedAssetId = meshes.GetId("blacksmith.building.ruined");
            int chimneyAssetId = meshes.GetId("blacksmith.furnace");

            int blacksmithEntityCount = 0;
            int visibleBlacksmithEntityCount = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    blacksmithEntityCount++;
                    if (engine.World.Has<CullState>(entity) && engine.World.Get<CullState>(entity).IsVisible)
                    {
                        visibleBlacksmithEntityCount++;
                    }
                }
            });
            blacksmithEntities = blacksmithEntityCount;
            visibleBlacksmithEntities = visibleBlacksmithEntityCount;

            int localRoot = 0, localLeft = 0, localRight = 0, localChimney = 0;
            int localRoute = 0, localDecal = 0, localWorker = 0, localBar = 0, localText = 0;
            var perfQuery = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in perfQuery, (Entity entity, ref PerformerState state) =>
            {
                if (state.DefId == rootId) localRoot++;
                if (state.DefId == leftId) localLeft++;
                if (state.DefId == rightId) localRight++;
                if (state.DefId == chimneyId) localChimney++;
                if (state.DefId == routeId) localRoute++;
                if (state.DefId == decalId) localDecal++;
                if (state.DefId == workerId) localWorker++;
                if (state.DefId == barId) localBar++;
                if (state.DefId == textId) localText++;
            });
            rootPerformerCount = localRoot;
            workshopLeftPerformerCount = localLeft;
            workshopRightPerformerCount = localRight;
            chimneyPerformerCount = localChimney;
            routeSplinePerformerCount = localRoute;
            decalPerformerCount = localDecal;
            workerPerformerCount = localWorker;
            barPerformerCount = localBar;
            textPerformerCount = localText;

            visibleWorkshopPrimitives = 0;
            visibleChimneyPrimitives = 0;
            foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
            {
                if (item.Visibility != VisualVisibility.Visible)
                {
                    continue;
                }

                if (item.MeshAssetId == northAssetId ||
                    item.MeshAssetId == southAssetId ||
                    item.MeshAssetId == damagedAssetId ||
                    item.MeshAssetId == ruinedAssetId)
                {
                    visibleWorkshopPrimitives++;
                }

                if (item.MeshAssetId == chimneyAssetId)
                {
                    visibleChimneyPrimitives++;
                }
            }

            worldHudBarCount = 0;
            worldHudTextCount = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    worldHudBarCount++;
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    worldHudTextCount++;
                }
            }

            roadSplineCount = roadSplines.Count;
            groundOverlayCount = overlays.Count;
        }

        private static void CountHudTextBenchmark(
            Ludots.Core.Engine.GameEngine engine,
            out int entities,
            out int rootPerformers,
            out int worldHudBars,
            out int worldHudText,
            out int screenHudBars,
            out int screenHudText)
        {
            var definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer missing.");
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer missing.");

            int rootId = definitions.GetId(PerformerBlacksmithShowcaseIds.MeshHudTextBenchmarkDefinitionId);
            int entityCount = 0;
            var entityQuery = new QueryDescription().WithAll<Name>();
            engine.World.Query(in entityQuery, (ref Name name) =>
            {
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.Ordinal))
                {
                    entityCount++;
                }
            });

            int performerCount = 0;
            var performerQuery = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in performerQuery, (ref PerformerState state) =>
            {
                if (state.DefId == rootId)
                {
                    performerCount++;
                }
            });

            int barCount = 0;
            int textCount = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                }
            }

            entities = entityCount;
            rootPerformers = performerCount;
            worldHudBars = barCount;
            worldHudText = textCount;
            screenHudBars = screenHud.BarCount;
            screenHudText = screenHud.TextCount;
        }

        private static string BuildReport(ScatterScenarioResult[] results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Performer Blacksmith Showcase Scatter Benchmark");
            sb.AppendLine();
            sb.AppendLine("- workload: random-scattered `blacksmith_building` templates on the showcase map");
            sb.AppendLine($"- measured frames: `{MeasuredFrames}` after warmup");
            sb.AppendLine("- initialization is measured separately until runtime spawn + performer/presentation counts stop changing");
            sb.AppendLine("- focus: canonical performer tree + HUD + spline + decal stability under many blacksmith roots");
            sb.AppendLine("- note: `tight` scenarios are full-visibility stress; `wide` scenarios validate camera culling / LOD under the same production actor graph");
            sb.AppendLine();

            for (int i = 0; i < results.Length; i++)
            {
                ScatterScenarioResult result = results[i];
                sb.AppendLine($"## {result.Name}");
                sb.AppendLine();
                sb.AppendLine($"- seed: `{result.Seed}`");
                sb.AppendLine($"- total buildings: `{result.TotalBuildings}`");
                sb.AppendLine($"- scatter radius cm: `{result.MinRadiusCm:F0}` -> `{result.MaxRadiusCm:F0}`");
                sb.AppendLine($"- full visibility expected: `{result.ExpectFullVisibility}`");
                sb.AppendLine($"- queued extras: `{result.QueuedExtraBuildings}`");
                sb.AppendLine($"- initialization: frames `{result.InitializationFrames}` | total `{result.InitializationTotalMs:F4} ms` | max frame `{result.InitializationMaxFrameMs:F4} ms` | queue after settle `{result.QueueCountAfterInitialization}` | stable settle `{result.InitializationStable}`");
                sb.AppendLine($"- blacksmith entities: `{result.BlacksmithEntities}`");
                sb.AppendLine($"- visible blacksmith entities: `{result.VisibleBlacksmithEntities}`");
                sb.AppendLine($"- performers: root `{result.RootPerformerCount}` | left `{result.WorkshopLeftPerformerCount}` | right `{result.WorkshopRightPerformerCount}` | chimney `{result.ChimneyPerformerCount}` | route `{result.RouteSplinePerformerCount}` | decal `{result.DecalPerformerCount}` | worker `{result.WorkerPerformerCount}` | bar `{result.BarPerformerCount}` | text `{result.TextPerformerCount}`");
                sb.AppendLine($"- presentation: workshop primitives `{result.VisibleWorkshopPrimitives}` | chimney primitives `{result.VisibleChimneyPrimitives}` | HUD bars `{result.WorldHudBarCount}` | HUD text `{result.WorldHudTextCount}` | splines `{result.RoadSplineCount}` | overlays `{result.GroundOverlayCount}`");
                sb.AppendLine($"- drops: events `{result.PresentationEventDrops}` | commands `{result.PerformerCommandDrops}` | primitives `{result.PrimitiveDrops}` | world HUD `{result.WorldHudDrops}` | screen HUD `{result.ScreenHudDrops}` | skinned `{result.SkinnedDrops}`");
                sb.AppendLine($"- avg tick: `{result.AverageTickMs:F4} ms`");
                sb.AppendLine($"- p95 tick: `{result.P95TickMs:F4} ms`");
                sb.AppendLine($"- max tick: `{result.MaxTickMs:F4} ms`");
                sb.AppendLine($"- avg simulation: `{result.AverageSimulationMs:F4} ms` | avg presentation: `{result.AveragePresentationMs:F4} ms`");
                sb.AppendLine($"- avg performer behavior: `{result.AverageBehaviorMs:F4} ms` | avg animator: `{result.AverageAnimatorMs:F4} ms` | avg emit: `{result.AverageEmitMs:F4} ms` | avg request flush: `{result.AverageRequestFlushMs:F4} ms`");
                sb.AppendLine($"- hottest presentation system: `{result.HottestPresentationSystemName}` avg `{result.HottestPresentationSystemAverageMs:F4} ms`");
                sb.AppendLine($"- hottest simulation system: `{result.HottestSimulationSystemName}` avg `{result.HottestSimulationSystemAverageMs:F4} ms`");
                sb.AppendLine($"- performer behavior counts avg/max: bootstrap `{result.AveragePerformerBootstrapCount:F1}`/`{result.MaxPerformerBootstrapCount}` | owner changes `{result.AveragePerformerOwnerChanges:F1}`/`{result.MaxPerformerOwnerChanges}` | attr changes `{result.AveragePerformerOwnerAttributeChanges:F1}`/`{result.MaxPerformerOwnerAttributeChanges}` | tag changes `{result.AveragePerformerOwnerTagChanges:F1}`/`{result.MaxPerformerOwnerTagChanges}`");
                sb.AppendLine($"- performer behavior counts avg/max: tick-driven `{result.AveragePerformerTickDrivenCount:F1}`/`{result.MaxPerformerTickDrivenCount}` | active sound tracking `{result.AveragePerformerActiveSoundTrackingCount:F1}`/`{result.MaxPerformerActiveSoundTrackingCount}` | destroy-scan `{result.AveragePerformerDestroyEventScanCount:F1}`/`{result.MaxPerformerDestroyEventScanCount}`");
                sb.AppendLine($"- avg culling: `{result.AverageCameraCullingMs:F4} ms` | p95 culling: `{result.P95CameraCullingMs:F4} ms` | max culling: `{result.MaxCameraCullingMs:F4} ms`");
                sb.AppendLine($"- avg HUD projection: `{result.AverageHudProjectionMs:F4} ms` | p95 HUD projection: `{result.P95HudProjectionMs:F4} ms` | max HUD projection: `{result.MaxHudProjectionMs:F4} ms`");
                sb.AppendLine($"- visible entities avg/max: `{AverageInt(result.VisibleEntities):F1}` / `{MaxInt(result.VisibleEntities):F0}`");
                sb.AppendLine($"- primitive instances avg/max: `{AverageInt(result.PrimitiveInstances):F1}` / `{MaxInt(result.PrimitiveInstances):F0}`");
                sb.AppendLine($"- avg fps equivalent: `{result.AverageFps:F1}`");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildTrace(ScatterScenarioResult[] results)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < results.Length; i++)
            {
                ScatterScenarioResult result = results[i];
                for (int frame = 0; frame < result.FrameTotals.Length; frame++)
                {
                    sb.Append("{");
                    sb.Append("\"scenario\":\"").Append(result.Name).Append("\",");
                    sb.Append("\"frame\":").Append(frame).Append(",");
                    sb.Append("\"tick_ms\":").Append(result.FrameTotals[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"total_buildings\":").Append(result.TotalBuildings).Append(",");
                    sb.Append("\"scatter_min_radius_cm\":").Append(result.MinRadiusCm.ToString("F0", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"scatter_max_radius_cm\":").Append(result.MaxRadiusCm.ToString("F0", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"expect_full_visibility\":").Append(result.ExpectFullVisibility ? "true" : "false").Append(",");
                    sb.Append("\"blacksmith_entities\":").Append(result.BlacksmithEntities).Append(",");
                    sb.Append("\"visible_blacksmith_entities\":").Append(result.VisibleBlacksmithEntities).Append(",");
                    sb.Append("\"init_frames\":").Append(result.InitializationFrames).Append(",");
                    sb.Append("\"init_total_ms\":").Append(result.InitializationTotalMs.ToString("F4", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"init_max_frame_ms\":").Append(result.InitializationMaxFrameMs.ToString("F4", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"init_queue_after_settle\":").Append(result.QueueCountAfterInitialization).Append(",");
                    sb.Append("\"init_stable\":").Append(result.InitializationStable ? "true" : "false").Append(",");
                    sb.Append("\"root_performers\":").Append(result.RootPerformerCount).Append(",");
                    sb.Append("\"visible_workshops\":").Append(result.VisibleWorkshopPrimitives).Append(",");
                    sb.Append("\"visible_chimneys\":").Append(result.VisibleChimneyPrimitives).Append(",");
                    sb.Append("\"world_hud_bars\":").Append(result.WorldHudBarCount).Append(",");
                    sb.Append("\"world_hud_text\":").Append(result.WorldHudTextCount).Append(",");
                    sb.Append("\"road_splines\":").Append(result.RoadSplineCount).Append(",");
                    sb.Append("\"ground_overlays\":").Append(result.GroundOverlayCount).Append(",");
                    sb.Append("\"camera_culling_ms\":").Append(result.CameraCullingMs[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"world_hud_projection_ms\":").Append(result.HudProjectionMs[frame].ToString("F4", CultureInfo.InvariantCulture)).Append(",");
                    sb.Append("\"visible_entities\":").Append(result.VisibleEntities[frame]).Append(",");
                    sb.Append("\"primitive_instances\":").Append(result.PrimitiveInstances[frame]).Append(",");
                    sb.Append("\"events_dropped_total\":").Append(result.PresentationEventDrops).Append(",");
                    sb.Append("\"commands_dropped_total\":").Append(result.PerformerCommandDrops).Append(",");
                    sb.Append("\"primitives_dropped_total\":").Append(result.PrimitiveDrops).Append(",");
                    sb.Append("\"world_hud_dropped_total\":").Append(result.WorldHudDrops).Append(",");
                    sb.Append("\"screen_hud_dropped_total\":").Append(result.ScreenHudDrops).Append(",");
                    sb.Append("\"skinned_dropped_total\":").Append(result.SkinnedDrops);
                    sb.Append(",\"simulation_ms\":").Append(result.SimulationMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"presentation_ms\":").Append(result.PresentationMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"performer_behavior_ms\":").Append(result.BehaviorMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"performer_animator_ms\":").Append(result.AnimatorMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"performer_emit_ms\":").Append(result.EmitMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"presentation_request_flush_ms\":").Append(result.RequestFlushMs[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"presentation_top1\":\"").Append(EscapeJson(result.PresentationTop1Names[frame])).Append("\"");
                    sb.Append(",\"presentation_top1_ms\":").Append(result.PresentationTop1Ms[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"simulation_top1\":\"").Append(EscapeJson(result.SimulationTop1Names[frame])).Append("\"");
                    sb.Append(",\"simulation_top1_ms\":").Append(result.SimulationTop1Ms[frame].ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append(",\"performer_bootstrap_count\":").Append(result.PerformerBootstrapCounts[frame]);
                    sb.Append(",\"performer_owner_changes\":").Append(result.PerformerOwnerChanges[frame]);
                    sb.Append(",\"performer_owner_attribute_changes\":").Append(result.PerformerOwnerAttributeChanges[frame]);
                    sb.Append(",\"performer_owner_tag_changes\":").Append(result.PerformerOwnerTagChanges[frame]);
                    sb.Append(",\"performer_tick_driven_count\":").Append(result.PerformerTickDrivenCounts[frame]);
                    sb.Append(",\"performer_active_sound_tracking_count\":").Append(result.PerformerActiveSoundTrackingCounts[frame]);
                    sb.Append(",\"performer_destroy_event_scan_count\":").Append(result.PerformerDestroyEventScanCounts[frame]);
                    sb.AppendLine("}");
                }
            }

            return sb.ToString();
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static double AverageInt(int[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            long sum = 0L;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return (double)sum / values.Length;
        }

        private static double MaxInt(int[] values)
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

        private readonly record struct ScatterScenario(string Name, int TotalBuildings, int Seed, float MinRadiusCm, float MaxRadiusCm, bool ExpectFullVisibility);

        private readonly record struct InitializationPhaseResult(
            int Frames,
            double TotalMs,
            double MaxFrameMs,
            int QueueCountAfterSettle,
            bool StableFramesReached);

        private sealed class ScatterScenarioResult
        {
            public ScatterScenarioResult(
                string name,
                int totalBuildings,
                int seed,
                float minRadiusCm,
                float maxRadiusCm,
                bool expectFullVisibility,
                int queuedExtraBuildings,
                int initializationFrames,
                double initializationTotalMs,
                double initializationMaxFrameMs,
                int queueCountAfterInitialization,
                bool initializationStable,
                int blacksmithEntities,
                int rootPerformerCount,
                int workshopLeftPerformerCount,
                int workshopRightPerformerCount,
                int chimneyPerformerCount,
                int routeSplinePerformerCount,
                int decalPerformerCount,
                int workerPerformerCount,
                int barPerformerCount,
                int textPerformerCount,
                int visibleBlacksmithEntities,
                int visibleWorkshopPrimitives,
                int visibleChimneyPrimitives,
                int worldHudBarCount,
                int worldHudTextCount,
                int roadSplineCount,
                int groundOverlayCount,
                int presentationEventDrops,
                int performerCommandDrops,
                int primitiveDrops,
                int worldHudDrops,
                int screenHudDrops,
                int skinnedDrops,
                double[] frameTotals,
                double[] simulationMs,
                double[] presentationMs,
                double[] cameraCullingMs,
                double[] behaviorMs,
                double[] animatorMs,
                double[] emitMs,
                double[] requestFlushMs,
                double[] hudProjectionMs,
                string[] presentationTop1Names,
                double[] presentationTop1Ms,
                string[] simulationTop1Names,
                double[] simulationTop1Ms,
                int[] performerBootstrapCounts,
                int[] performerOwnerChanges,
                int[] performerOwnerAttributeChanges,
                int[] performerOwnerTagChanges,
                int[] performerTickDrivenCounts,
                int[] performerActiveSoundTrackingCounts,
                int[] performerDestroyEventScanCounts,
                int[] visibleEntities,
                int[] primitiveInstances)
            {
                Name = name;
                TotalBuildings = totalBuildings;
                Seed = seed;
                MinRadiusCm = minRadiusCm;
                MaxRadiusCm = maxRadiusCm;
                ExpectFullVisibility = expectFullVisibility;
                QueuedExtraBuildings = queuedExtraBuildings;
                InitializationFrames = initializationFrames;
                InitializationTotalMs = initializationTotalMs;
                InitializationMaxFrameMs = initializationMaxFrameMs;
                QueueCountAfterInitialization = queueCountAfterInitialization;
                InitializationStable = initializationStable;
                BlacksmithEntities = blacksmithEntities;
                RootPerformerCount = rootPerformerCount;
                WorkshopLeftPerformerCount = workshopLeftPerformerCount;
                WorkshopRightPerformerCount = workshopRightPerformerCount;
                ChimneyPerformerCount = chimneyPerformerCount;
                RouteSplinePerformerCount = routeSplinePerformerCount;
                DecalPerformerCount = decalPerformerCount;
                WorkerPerformerCount = workerPerformerCount;
                BarPerformerCount = barPerformerCount;
                TextPerformerCount = textPerformerCount;
                VisibleBlacksmithEntities = visibleBlacksmithEntities;
                VisibleWorkshopPrimitives = visibleWorkshopPrimitives;
                VisibleChimneyPrimitives = visibleChimneyPrimitives;
                WorldHudBarCount = worldHudBarCount;
                WorldHudTextCount = worldHudTextCount;
                RoadSplineCount = roadSplineCount;
                GroundOverlayCount = groundOverlayCount;
                PresentationEventDrops = presentationEventDrops;
                PerformerCommandDrops = performerCommandDrops;
                PrimitiveDrops = primitiveDrops;
                WorldHudDrops = worldHudDrops;
                ScreenHudDrops = screenHudDrops;
                SkinnedDrops = skinnedDrops;
                FrameTotals = frameTotals;
                SimulationMs = simulationMs;
                PresentationMs = presentationMs;
                CameraCullingMs = cameraCullingMs;
                BehaviorMs = behaviorMs;
                AnimatorMs = animatorMs;
                EmitMs = emitMs;
                RequestFlushMs = requestFlushMs;
                HudProjectionMs = hudProjectionMs;
                PresentationTop1Names = presentationTop1Names;
                PresentationTop1Ms = presentationTop1Ms;
                SimulationTop1Names = simulationTop1Names;
                SimulationTop1Ms = simulationTop1Ms;
                PerformerBootstrapCounts = performerBootstrapCounts;
                PerformerOwnerChanges = performerOwnerChanges;
                PerformerOwnerAttributeChanges = performerOwnerAttributeChanges;
                PerformerOwnerTagChanges = performerOwnerTagChanges;
                PerformerTickDrivenCounts = performerTickDrivenCounts;
                PerformerActiveSoundTrackingCounts = performerActiveSoundTrackingCounts;
                PerformerDestroyEventScanCounts = performerDestroyEventScanCounts;
                VisibleEntities = visibleEntities;
                PrimitiveInstances = primitiveInstances;
            }

            public string Name { get; }
            public int TotalBuildings { get; }
            public int Seed { get; }
            public float MinRadiusCm { get; }
            public float MaxRadiusCm { get; }
            public bool ExpectFullVisibility { get; }
            public int QueuedExtraBuildings { get; }
            public int InitializationFrames { get; }
            public double InitializationTotalMs { get; }
            public double InitializationMaxFrameMs { get; }
            public int QueueCountAfterInitialization { get; }
            public bool InitializationStable { get; }
            public int BlacksmithEntities { get; }
            public int RootPerformerCount { get; }
            public int WorkshopLeftPerformerCount { get; }
            public int WorkshopRightPerformerCount { get; }
            public int ChimneyPerformerCount { get; }
            public int RouteSplinePerformerCount { get; }
            public int DecalPerformerCount { get; }
            public int WorkerPerformerCount { get; }
            public int BarPerformerCount { get; }
            public int TextPerformerCount { get; }
            public int VisibleBlacksmithEntities { get; }
            public int VisibleWorkshopPrimitives { get; }
            public int VisibleChimneyPrimitives { get; }
            public int WorldHudBarCount { get; }
            public int WorldHudTextCount { get; }
            public int RoadSplineCount { get; }
            public int GroundOverlayCount { get; }
            public int PresentationEventDrops { get; }
            public int PerformerCommandDrops { get; }
            public int PrimitiveDrops { get; }
            public int WorldHudDrops { get; }
            public int ScreenHudDrops { get; }
            public int SkinnedDrops { get; }
            public double[] FrameTotals { get; }
            public double[] SimulationMs { get; }
            public double[] PresentationMs { get; }
            public double[] CameraCullingMs { get; }
            public double[] BehaviorMs { get; }
            public double[] AnimatorMs { get; }
            public double[] EmitMs { get; }
            public double[] RequestFlushMs { get; }
            public double[] HudProjectionMs { get; }
            public string[] PresentationTop1Names { get; }
            public double[] PresentationTop1Ms { get; }
            public string[] SimulationTop1Names { get; }
            public double[] SimulationTop1Ms { get; }
            public int[] PerformerBootstrapCounts { get; }
            public int[] PerformerOwnerChanges { get; }
            public int[] PerformerOwnerAttributeChanges { get; }
            public int[] PerformerOwnerTagChanges { get; }
            public int[] PerformerTickDrivenCounts { get; }
            public int[] PerformerActiveSoundTrackingCounts { get; }
            public int[] PerformerDestroyEventScanCounts { get; }
            public int[] VisibleEntities { get; }
            public int[] PrimitiveInstances { get; }

            public double AverageTickMs => Average(FrameTotals);
            public double P95TickMs => Percentile(FrameTotals, 0.95);
            public double MaxTickMs => Max(FrameTotals);
            public double AverageSimulationMs => Average(SimulationMs);
            public double AveragePresentationMs => Average(PresentationMs);
            public double AverageCameraCullingMs => Average(CameraCullingMs);
            public double P95CameraCullingMs => Percentile(CameraCullingMs, 0.95);
            public double MaxCameraCullingMs => Max(CameraCullingMs);
            public double AverageBehaviorMs => Average(BehaviorMs);
            public double AverageAnimatorMs => Average(AnimatorMs);
            public double AverageEmitMs => Average(EmitMs);
            public double AverageRequestFlushMs => Average(RequestFlushMs);
            public double AverageHudProjectionMs => Average(HudProjectionMs);
            public double P95HudProjectionMs => Percentile(HudProjectionMs, 0.95);
            public double MaxHudProjectionMs => Max(HudProjectionMs);
            public string HottestPresentationSystemName => MostFrequentNonEmpty(PresentationTop1Names);
            public double HottestPresentationSystemAverageMs => AverageForName(PresentationTop1Names, PresentationTop1Ms, HottestPresentationSystemName);
            public string HottestSimulationSystemName => MostFrequentNonEmpty(SimulationTop1Names);
            public double HottestSimulationSystemAverageMs => AverageForName(SimulationTop1Names, SimulationTop1Ms, HottestSimulationSystemName);
            public double AveragePerformerBootstrapCount => Average(PerformerBootstrapCounts);
            public int MaxPerformerBootstrapCount => Max(PerformerBootstrapCounts);
            public double AveragePerformerOwnerChanges => Average(PerformerOwnerChanges);
            public int MaxPerformerOwnerChanges => Max(PerformerOwnerChanges);
            public double AveragePerformerOwnerAttributeChanges => Average(PerformerOwnerAttributeChanges);
            public int MaxPerformerOwnerAttributeChanges => Max(PerformerOwnerAttributeChanges);
            public double AveragePerformerOwnerTagChanges => Average(PerformerOwnerTagChanges);
            public int MaxPerformerOwnerTagChanges => Max(PerformerOwnerTagChanges);
            public double AveragePerformerTickDrivenCount => Average(PerformerTickDrivenCounts);
            public int MaxPerformerTickDrivenCount => Max(PerformerTickDrivenCounts);
            public double AveragePerformerActiveSoundTrackingCount => Average(PerformerActiveSoundTrackingCounts);
            public int MaxPerformerActiveSoundTrackingCount => Max(PerformerActiveSoundTrackingCounts);
            public double AveragePerformerDestroyEventScanCount => Average(PerformerDestroyEventScanCounts);
            public int MaxPerformerDestroyEventScanCount => Max(PerformerDestroyEventScanCounts);
            public double AverageFps => AverageTickMs <= 0d ? 0d : 1000d / AverageTickMs;

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

                long sum = 0L;
                for (int i = 0; i < values.Length; i++)
                {
                    sum += values[i];
                }

                return (double)sum / values.Length;
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
                string bestName = string.Empty;
                int bestCount = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    string candidate = values[i];
                    if (string.IsNullOrEmpty(candidate))
                    {
                        continue;
                    }

                    int count = 0;
                    for (int j = 0; j < values.Length; j++)
                    {
                        if (values[j] == candidate)
                        {
                            count++;
                        }
                    }

                    if (count > bestCount)
                    {
                        bestName = candidate;
                        bestCount = count;
                    }
                }

                return bestName;
            }

            private static double AverageForName(string[] names, double[] values, string targetName)
            {
                if (string.IsNullOrEmpty(targetName))
                {
                    return 0d;
                }

                double sum = 0d;
                int count = 0;
                for (int i = 0; i < names.Length && i < values.Length; i++)
                {
                    if (names[i] != targetName)
                    {
                        continue;
                    }

                    sum += values[i];
                    count++;
                }

                return count == 0 ? 0d : sum / count;
            }
        }

        private static ScatterScenario[] ResolveScenarios()
        {
            string? filter = Environment.GetEnvironmentVariable("LUDOTS_BLACKSMITH_BENCH_SCENARIO");
            if (string.IsNullOrWhiteSpace(filter))
            {
                return Scenarios;
            }

            var filtered = new System.Collections.Generic.List<ScatterScenario>(Scenarios.Length);
            for (int i = 0; i < Scenarios.Length; i++)
            {
                if (string.Equals(Scenarios[i].Name, filter, StringComparison.Ordinal))
                {
                    filtered.Add(Scenarios[i]);
                }
            }

            if (filtered.Count == 0)
            {
                throw new InvalidOperationException($"No blacksmith benchmark scenario matched filter '{filter}'.");
            }

            return filtered.ToArray();
        }

        private static int ReadPositiveEnvIntOrDeclaredDefault(string key, int declaredDefault)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (raw is null)
            {
                return declaredDefault;
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
            {
                return parsed;
            }

            throw new InvalidOperationException(
                $"Environment variable {key} must be an integer > 0 when declared.");
        }
    }
}
