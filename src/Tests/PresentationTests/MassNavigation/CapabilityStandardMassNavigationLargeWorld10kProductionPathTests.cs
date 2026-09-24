using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.System;
using CapabilityStandardMassNavigationLargeWorld10kMod;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.MovePlanning;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CapabilityStandardMassNavigationLargeWorld10kProductionPathTests
    {
        private static readonly QueryDescription MassNavigationAgentQuery = new QueryDescription()
            .WithAll<MassNavigationAgent, MassNavigationAgentIndex, WorldPositionCm>();

        private const int ExpectedAgentCount = 10_000;
        private const int ExpectedTeamCount = 4;
        private const float FixedDeltaSeconds = 1f / 60f;
        private const int MaxWarmupFrames = 240;
        private const int MovementObservationFrames = 60;
        private const int HealthObservationTicks = 65;
        private const int HudStabilityObservationFrames = 12;
        private const float CommandTargetOffsetWindowScale = 0.25f;
        private const float MovementEpsilonCm = 1f;
        private const string MouseLeftButtonPath = "<Mouse>/LeftButton";
        private const string MouseRightButtonPath = "<Mouse>/RightButton";
        private const string LightCommandMarkerPresenterId = "presenter.case_e.selection_marker";
        private const string HeavyCommandMarkerPresenterId = "presenter.case_e.selection_marker";

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "SelectionInteractionMod",
            "MassNavigationMod",
            "CapabilityStandardMassNavigationLargeWorld10kMod"
        };

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
        }

        [Test]
        public void Showcase_ProjectsFourTeamAgentsToMinimapAndScreenHud()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var spatialQueries = engine.SpatialQueries as SpatialQueryService
                ?? throw new InvalidOperationException("Production engine must keep a stable SpatialQueryService instance.");
            ILoadedChunks loadedChunks = RequireService(engine, CoreServiceKeys.LoadedChunks);
            Assert.That(spatialQueries.LoadedChunks, Is.SameAs(loadedChunks),
                "GridBoard focus must publish one loaded-chunk SSOT to both Core services and spatial queries.");

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            var effectRequestQueue = RequireService(engine, CoreServiceKeys.EffectRequestQueue);
            Assert.That(
                effectRequestQueue.Capacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.EffectRequestQueueCapacity));
            Assert.That(
                effectRequestQueue.TotalCapacity,
                Is.GreaterThanOrEqualTo(expectedAgents),
                "Merged Mass Navigation capacity must cover every configured agent spawn effect.");
            var deferredTriggers = RequireService(engine, CoreServiceKeys.DeferredTriggerQueue);
            Assert.That(deferredTriggers.Capacity, Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.DeferredTriggerPerFrameCapacity));
            Assert.That(deferredTriggers.Capacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderQueueCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderAdmissionResultCapacity, Is.GreaterThanOrEqualTo(expectedAgents * 2));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderTerminalResultCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.Scenario.Teams.Length, Is.EqualTo(ExpectedTeamCount));

            var hudProjection = CreateHudProjection(engine);
            ProjectionSample sample = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            var minimapRuntime = RequireService(engine, CoreServiceKeys.MinimapRuntime);
            var minimapMarkers = RequireService(engine, CoreServiceKeys.MinimapMarkerBuffer);
            var minimapScreenMarkers = RequireService(engine, CoreServiceKeys.MinimapScreenMarkerBuffer);
            var worldHud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            string diagnostics = sample.Diagnostics;

            Assert.That(minimapRuntime.Visible, Is.True, diagnostics);
            Assert.That(minimapRuntime.Preset, Is.EqualTo(MinimapPreset.RtsFullMap), diagnostics);
            Assert.That(sample.MinimapSnapshot.ZoomBand, Is.EqualTo(MinimapZoomBand.Strategic), diagnostics);
            Assert.That(minimapMarkers.Count, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(minimapScreenMarkers.Count, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.MinimapSnapshot.VisibleMarkerCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.WorldHudBars, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.WorldHudText, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(screenHud.BarCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(screenHud.TextCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(worldHud.Count, Is.LessThanOrEqualTo(worldHud.Capacity), diagnostics);
            Assert.That(screenHud.Count, Is.LessThanOrEqualTo(screenHud.Capacity), diagnostics);
            Assert.That(minimapMarkers.Count, Is.LessThanOrEqualTo(minimapMarkers.Capacity), diagnostics);
            Assert.That(minimapScreenMarkers.Count, Is.LessThanOrEqualTo(minimapScreenMarkers.Capacity), diagnostics);
            Assert.That(worldHud.DroppedTotal, Is.Zero, diagnostics);
            Assert.That(screenHud.DroppedTotal, Is.Zero, diagnostics);
            Assert.That(minimapMarkers.DroppedTotal, Is.Zero, diagnostics);
            Assert.That(minimapScreenMarkers.DroppedTotal, Is.Zero, diagnostics);
            AssertFixedAnchorChain(engine, simulation, sampleCount: 64, toleranceCm: 25f);
        }

        [Test]
        public void Showcase_TerrainHudProjectionCost()
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
            var hud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screen = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudItem item = hud.GetSpan()[0];
            for (int i = 0; i < 100; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                projection.Update(0);
            }
            var samples = new double[31];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                projection.Update(0);
                samples[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Array.Sort(samples);
            TestContext.Out.WriteLine($"MassNav terrain: world_items={hud.Count}, screen_items={screen.Count}, median_ms={samples[15]:F4}, p95_ms={samples[29]:F4}, bytes={allocated}");
            Assert.That(allocated, Is.Zero);
        }

        // [DEBUG-a4f2] counting decorator: proves per-owner terrain raycast count per HUD projection frame.
        private sealed class CountingHeightmap : IContinuousHeightmap
        {
            private readonly IContinuousHeightmap _inner;
            public long RaycastCalls;
            public long SampleCalls;

            public CountingHeightmap(IContinuousHeightmap inner) => _inner = inner;

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
            {
                SampleCalls++;
                return _inner.TrySampleHeightCm(worldXCm, worldYCm, out heightCm, layerIndex);
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
            {
                SampleCalls += worldXCm.Length;
                return _inner.SampleHeightsCm(worldXCm, worldYCm, outHeightCm, layerIndex);
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
            {
                RaycastCalls++;
                return _inner.TryRaycastGround(in ray, out hit, layerIndex);
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = -1)
            {
                RaycastCalls += originXMeters.Length;
                return _inner.RaycastGroundBatch(
                    originXMeters, originYMeters, originZMeters,
                    directionX, directionY, directionZ,
                    outWorldXCm, outWorldYCm, outHeightCm, outDistanceMeters,
                    outNormalX, outNormalY, outNormalZ, outLayerIndex, outHitMask, layerIndex);
            }
        }

        // [DEBUG-a4f2] read-only audit probe for PR #1485 HUD projection cost.
        [TestCase(true)]
        [TestCase(false)]
        public void Probe_A4F2_TerrainOcclusionAb(bool terrainOcclusion)
        {
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var warm = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, warm, simulation, ExpectedAgentCount);

            var projection = new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState),
                terrainOcclusion ? () => engine.GetService(CoreServiceKeys.ContinuousHeightmap) : () => null);

            var hud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screen = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudItem item = hud.GetSpan()[0];
            for (int i = 0; i < 100; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                projection.Update(0);
            }

            var samples = new double[31];
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                projection.Update(0);
                samples[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            Array.Sort(samples);
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] terrain={terrainOcclusion} world_items={hud.Count} screen_items={screen.Count} " +
                $"min_ms={samples[0]:F4} median_ms={samples[15]:F4} p95_ms={samples[29]:F4} bytes={allocated} gen0={gen0} gen1={gen1}");
            projection.Dispose();
        }

        // [DEBUG-a4f2] counts terrain raycasts per HUD projection frame at 10K.
        [Test]
        public void Probe_A4F2_TerrainRaycastCountPerFrame()
        {
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var warm = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, warm, simulation, ExpectedAgentCount);

            var counting = new CountingHeightmap(
                RequireService(engine, CoreServiceKeys.ContinuousHeightmap));
            var projection = new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState),
                () => counting);

            var hud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            WorldHudItem item = hud.GetSpan()[0];
            for (int i = 0; i < 20; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                projection.Update(0);
            }

            counting.RaycastCalls = 0;
            counting.SampleCalls = 0;
            const int frames = 5;
            for (int i = 0; i < frames; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                projection.Update(0);
            }

            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] world_items={hud.Count} frames={frames} " +
                $"raycasts_total={counting.RaycastCalls} raycasts_per_frame={counting.RaycastCalls / (double)frames:F1} " +
                $"height_samples_total={counting.SampleCalls} samples_per_frame={counting.SampleCalls / (double)frames:F1}");
            projection.Dispose();
        }

        // [DEBUG-a4f2] full-frame breakdown with the terrain-occlusion A/B at 10K.
        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void Probe_A4F2_FullFrameBreakdown(bool terrainOcclusion, bool moving)
        {
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var warm = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, warm, simulation, ExpectedAgentCount);

            var counting = new CountingHeightmap(
                RequireService(engine, CoreServiceKeys.ContinuousHeightmap));
            var projection = new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState),
                terrainOcclusion ? () => counting : (Func<IContinuousHeightmap?>)(() => null));

            if (moving)
            {
                var sink = new MassNavigationMovePlanExecutionSink(simulation);
                Entity[] agents = CollectMassNavigationAgents(engine, ExpectedAgentCount);
                int applied = 0;
                for (int i = 0; i < agents.Length; i++)
                {
                    Vector2 here = simulation.GetAgentWorldPositionCm(
                        engine.World.Get<MassNavigationAgentIndex>(agents[i]).Value);
                    var intent = new MovePlanExecutionIntent
                    {
                        CommandGroupToken = 1,
                        TargetWorldCm = new Vector2(10_000f - here.X, 10_000f - here.Y),
                        ProjectionHintWorldCm = new Vector2(10_000f - here.X, 10_000f - here.Y),
                        SpeedCmPerSec = 300f,
                        StopRadiusCm = 40f,
                        MinimumClearanceCm = 0f,
                        HasTarget = 1,
                        ResolveNavigableTarget = 1,
                        Mode = MovePlanExecutionMode.Individual,
                    };
                    if (sink.TryApply(engine.World, agents[i], in intent))
                    {
                        applied++;
                    }
                }

                TestContext.Out.WriteLine($"[DEBUG-a4f2] move_intents_applied={applied}");
            }

            for (int i = 0; i < 30; i++)
            {
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
            }

            var diag = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            const int frames = 40;
            var tickMs = new double[frames];
            var projMs = new double[frames];
            double behavior = 0, sync = 0, emit = 0, hud = 0, cull = 0, minimap = 0;
            double mnPrep = 0, mnSteer = 0, mnHard = 0, mnFlow = 0, mnSync = 0, mnStep = 0;
            counting.RaycastCalls = 0;
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            long alloc = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                TickOnly(engine);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                projection.Update(FixedDeltaSeconds);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                tickMs[i] = System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
                projMs[i] = System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds;
                behavior += diag.LastPresenterBehaviorMs;
                sync += diag.LastPresenterEntityTransformSyncMs;
                emit += diag.LastPresenterEmitMs;
                hud += diag.LastWorldHudProjectionMs;
                cull += diag.LastCameraCullingMs;
                minimap += diag.LastMinimapProjectionMs;
                mnPrep += diag.LastMassNavigationPrepMs;
                mnSteer += diag.LastMassNavigationSteeringMs;
                mnHard += diag.LastMassNavigationHardResolveMs;
                mnFlow += diag.LastMassNavigationFlowMs;
                mnSync += diag.LastMassNavigationEntitySyncMs;
                mnStep += diag.LastMassNavigationStepMs;
            }
            alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            Array.Sort(tickMs);
            Array.Sort(projMs);
            double f = frames;
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] terrain={terrainOcclusion} moving={moving} agents={simulation.NavigationAgentCount} " +
                $"tick_median={tickMs[frames / 2]:F3} proj_median={projMs[frames / 2]:F3} " +
                $"total_median={tickMs[frames / 2] + projMs[frames / 2]:F3} " +
                $"| behavior={behavior / f:F3} sync={sync / f:F3} emit={emit / f:F3} hudproj={hud / f:F3} " +
                $"cull={cull / f:F3} minimap={minimap / f:F3} " +
                $"| mn_prep={mnPrep / f:F3} mn_steer={mnSteer / f:F3} mn_hard={mnHard / f:F3} mn_flow={mnFlow / f:F3} " +
                $"mn_sync={mnSync / f:F3} mn_step={mnStep / f:F3} " +
                $"| hard_cand={simulation.LastHardResolveCandidateAgentCount} hard_pairs={simulation.LastHardResolvePairCheckCount} " +
                $"pen_pairs={simulation.LastHardResolvePenetratingPairCount} " +
                $"| raycasts_per_frame={counting.RaycastCalls / f:F0} bytes_per_frame={alloc / f:F0} gen0={gen0} gen1={gen1}");
            projection.Dispose();
        }

        private static void TickOnly(GameEngine engine)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(FixedDeltaSeconds);
            HeadlessPresentationTestHost.UpdateCamera(engine);
            if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime mr &&
                engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is MinimapMarkerBuffer mb &&
                engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer) is MinimapScreenMarkerBuffer msb)
            {
                mr.Refresh(engine, mb, msb);
            }
        }

        // [DEBUG-a4f2] attributes per-frame managed allocation between engine tick, minimap refresh and HUD projection.
        [Test]
        public void Probe_A4F2_AllocationAttribution()
        {
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);

            for (int i = 0; i < 20; i++)
            {
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
            }

            const int frames = 30;
            long tickBytes = 0, minimapBytes = 0, projBytes = 0;
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime mr &&
                    engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is MinimapMarkerBuffer mb &&
                    engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer) is MinimapScreenMarkerBuffer msb)
                {
                    mr.Refresh(engine, mb, msb);
                }
                long a2 = GC.GetAllocatedBytesForCurrentThread();
                projection.Update(FixedDeltaSeconds);
                long a3 = GC.GetAllocatedBytesForCurrentThread();
                tickBytes += a1 - a0;
                minimapBytes += a2 - a1;
                projBytes += a3 - a2;
            }
            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            double f = frames;
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] alloc_per_frame tick={tickBytes / f:F0}B minimap_refresh={minimapBytes / f:F0}B " +
                $"hud_projection={projBytes / f:F0}B gen0={gen0} gen1={gen1}");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Probe_A4F2_GroundingCreationAb(bool skipInitialSampleTick)
        {
            PresenterEntityRuntime.AuditSkipInitialSampleTick = skipInitialSampleTick;
            PresenterEntityRuntime.AuditGroundingEnabled = true;
            PresenterEntityRuntime.AuditGroundingFirstStack = null;
            try
            {
                WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
                using var engine = CreateEngine();
                StartStartupMap(engine);
                var simulation = RequireMassNavigationSimulation(engine);
                var projection = CreateHudProjection(engine);
                _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
                PresenterEntityRuntime.AuditGroundingEnabled = false;
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_call_stack skip={skipInitialSampleTick}\n{PresenterEntityRuntime.AuditGroundingFirstStack}");
                var diag = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
                for (int i = 0; i < 30; i++)
                {
                    TickOnly(engine);
                    projection.Update(FixedDeltaSeconds);
                }
                const int frames = 60;
                var behaviorMs = new float[frames];
                var ticks = new int[frames];
                var projected = new int[frames];
                var heightDifferences = new float[frames];
                var checkedRoots = new int[frames];
                var query = new QueryDescription().WithAll<PresenterState, PresenterWorldPosition, PresenterParent>();
                for (int f = 0; f < frames; f++)
                {
                    TickOnly(engine);
                    projection.Update(FixedDeltaSeconds);
                    behaviorMs[f] = diag.LastPresenterBehaviorMs;
                    ticks[f] = diag.PresenterTickDrivenCountLastFrame;
                    projected[f] = diag.WorldHudProjectedLastFrame;
                    foreach (ref var chunk in engine.World.Query(in query))
                    {
                        var states = chunk.GetSpan<PresenterState>();
                        var positions = chunk.GetSpan<PresenterWorldPosition>();
                        var parents = chunk.GetSpan<PresenterParent>();
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            Entity owner = states[i].OwnerEntity;
                            if (parents[i].Parent != Entity.Null || owner == Entity.Null ||
                                !engine.World.IsAlive(owner) || !engine.World.Has<MassNavigationAgent>(owner)) continue;
                            float delta = MathF.Abs(positions[i].Value.Y - engine.World.Get<VisualTransform>(owner).Position.Y);
                            heightDifferences[f] = MathF.Max(heightDifferences[f], delta);
                            checkedRoots[f]++;
                        }
                    }
                }
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_ab skip={skipInitialSampleTick} warmup=30 frames={frames} agents={simulation.NavigationAgentCount}");
                TestContext.Out.WriteLine("[DEBUG-a4f2] frame,behavior_ms,tick_count,hud_projected,root_height_max_delta,checked_roots");
                for (int f = 0; f < frames; f++)
                {
                    TestContext.Out.WriteLine($"[DEBUG-a4f2] {f},{behaviorMs[f]:F6},{ticks[f]},{projected[f]},{heightDifferences[f]:F6},{checkedRoots[f]}");
                    Assert.That(checkedRoots[f], Is.EqualTo(ExpectedAgentCount));
                    Assert.That(heightDifferences[f], Is.LessThanOrEqualTo(0.0001f));
                    Assert.That(projected[f], Is.EqualTo(ExpectedAgentCount * 2));
                }
            }
            finally
            {
                PresenterEntityRuntime.AuditGroundingEnabled = false;
                PresenterEntityRuntime.AuditSkipInitialSampleTick = false;
            }
        }

        [Test]
        public void Probe_A4F2_ColdGroundingQualification()
        {
            PresenterEntityRuntime.AuditGroundingEnabled = true;
            PresenterEntityRuntime.AuditSingleRootCreates = 0;
            PresenterEntityRuntime.AuditSingleChildCreates = 0;
            PresenterEntityRuntime.AuditRootBatchCreates = 0;
            PresenterEntityRuntime.AuditRootBatchCalls = 0;
            PresenterEntityRuntime.AuditSyncGroundingTrue = 0;
            Array.Clear(PresenterEntityRuntime.AuditSyncCallers);
            Array.Clear(PresenterEntityRuntime.AuditSingleGroundingReasons);
            Array.Clear(PresenterEntityRuntime.AuditBatchGroundingReasons);
            try
            {
                WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
                using var engine = CreateEngine();
                StartStartupMap(engine);
                var simulation = RequireMassNavigationSimulation(engine);
                var projection = CreateHudProjection(engine);
                _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
                int grounding = 0, eligible = 0, otherTick = 0, unsampled = 0, noSample = 0;
                var query = new QueryDescription().WithAll<PresenterState>();
                foreach (ref var chunk in engine.World.Query(in query))
                {
                    var states = chunk.GetSpan<PresenterState>();
                    bool hasGrounding = chunk.Has<PerfHasGrounding>();
                    bool hasOtherTick = chunk.Has<PerfHasSpline>() || chunk.Has<PerfHasAttachmentTick>() ||
                        chunk.Has<PerfHasSound>() || chunk.Has<PerfHasOwnerFacingBinding>() ||
                        chunk.Has<PerfHasGraphParamBinding>() || chunk.Has<PerfHasLiveParamBinding>() ||
                        chunk.Has<PerfHasInteractionContextBinding>() || chunk.Has<PerfHasExtensionBehavior>() ||
                        chunk.Has<PerfHasTrailMesh>();
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (hasOtherTick) otherTick++;
                        if (!hasGrounding) continue;
                        grounding++;
                        Entity owner = states[i].OwnerEntity;
                        if (owner == Entity.Null || !engine.World.IsAlive(owner) ||
                            !engine.World.Has<ContinuousHeightmapSampleState>(owner)) noSample++;
                        else if (engine.World.Get<ContinuousHeightmapSampleState>(owner).Sampled == 0) unsampled++;
                        else eligible++;
                    }
                }
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_create single_roots={PresenterEntityRuntime.AuditSingleRootCreates} single_children={PresenterEntityRuntime.AuditSingleChildCreates} batch_roots={PresenterEntityRuntime.AuditRootBatchCreates} batch_calls={PresenterEntityRuntime.AuditRootBatchCalls} sync_grounding_true={PresenterEntityRuntime.AuditSyncGroundingTrue}");
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_sync_callers=create,active,child,rebind counts={string.Join(',', PresenterEntityRuntime.AuditSyncCallers)}");
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_single_reasons=config,anchor,null,dead,no_visual,no_sample,unsampled,no_source,other_source,eligible,reserved counts={string.Join(',', PresenterEntityRuntime.AuditSingleGroundingReasons)}");
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_batch_reasons=config,null,dead,no_visual,no_sample,unsampled,eligible,reserved counts={string.Join(',', PresenterEntityRuntime.AuditBatchGroundingReasons)}");
                TestContext.Out.WriteLine($"[DEBUG-a4f2] grounding_final markers={grounding} owner_sampled={eligible} owner_unsampled={unsampled} owner_no_sample={noSample} other_tick={otherTick}");
            }
            finally
            {
                PresenterEntityRuntime.AuditGroundingEnabled = false;
            }
        }

        [Test]
        public void Probe_A4F2_LongAllocationAndStructuralCounts()
        {
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = false;
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
            for (int i = 0; i < 300; i++)
            {
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
            }
            const int frames = 120;
            var totalBytes = new long[frames];
            var aggregatorBytes = new long[frames];
            var clearBytes = new long[frames];
            var spatialBytes = new long[frames];
            var aggregatorRemoved = new int[frames];
            var clearRemoved = new int[frames];
            var aggregatorPlaybackBytes = new long[frames];
            var clearPlaybackBytes = new long[frames];
            var createdCells = new int[frames];
            var createdCellBytes = new long[frames];
            int frame = -1;
            var diag = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            diag.AuditAllocationObserver = (lane, name, bytes) =>
            {
                if (frame < 0 || lane != 0) return;
                if (name == "AttributeAggregatorSystem")
                {
                    aggregatorBytes[frame] += bytes;
                    aggregatorRemoved[frame] += Ludots.Core.Gameplay.GAS.Systems.AttributeAggregatorSystem.AuditRemovedThisUpdate;
                    aggregatorPlaybackBytes[frame] += Ludots.Core.Gameplay.GAS.Systems.AttributeAggregatorSystem.AuditPlaybackBytesThisUpdate;
                }
                else if (name == "ClearPresentationFlagsSystem")
                {
                    clearBytes[frame] += bytes;
                    clearRemoved[frame] += Ludots.Core.Gameplay.GAS.Systems.ClearPresentationFlagsSystem.AuditRemovedThisUpdate;
                    clearPlaybackBytes[frame] += Ludots.Core.Gameplay.GAS.Systems.ClearPresentationFlagsSystem.AuditPlaybackBytesThisUpdate;
                }
                else if (name == "SpatialPartitionUpdateSystem") spatialBytes[frame] += bytes;
            };
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            World.AuditRemoveRangeCalls = 0;
            World.AuditRemoveRangeHashMisses = 0;
            World.AuditRemoveRangeSignatureBytes = 0;
            World.AuditRemoveRangeEventBytes = 0;
            World.AuditRemoveRangeMoveBytes = 0;
            Ludots.Core.Gameplay.GAS.AttributeMutationOps.AuditAggregateAdded = 0;
            Ludots.Core.Gameplay.GAS.AttributeMutationOps.AuditPresentationAdded = 0;
            for (frame = 0; frame < frames; frame++)
            {
                int cellCountStart = Ludots.Core.Spatial.ChunkedGridSpatialPartitionWorld.AuditCellCreates;
                long cellBytesStart = Ludots.Core.Spatial.ChunkedGridSpatialPartitionWorld.AuditCellCreateBytes;
                long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
                totalBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
                createdCells[frame] = Ludots.Core.Spatial.ChunkedGridSpatialPartitionWorld.AuditCellCreates - cellCountStart;
                createdCellBytes[frame] = Ludots.Core.Spatial.ChunkedGridSpatialPartitionWorld.AuditCellCreateBytes - cellBytesStart;
            }
            diag.AuditAllocationObserver = null;
            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            TestContext.Out.WriteLine($"[DEBUG-a4f2] longalloc warmup=300 frames={frames} agents={simulation.NavigationAgentCount} gen0={gen0} gen1={gen1}");
            TestContext.Out.WriteLine($"[DEBUG-a4f2] remove_range calls={World.AuditRemoveRangeCalls} hash_misses={World.AuditRemoveRangeHashMisses} signature_bytes={World.AuditRemoveRangeSignatureBytes} event_bytes={World.AuditRemoveRangeEventBytes} move_bytes={World.AuditRemoveRangeMoveBytes}");
            TestContext.Out.WriteLine($"[DEBUG-a4f2] direct_attribute_add aggregate={Ludots.Core.Gameplay.GAS.AttributeMutationOps.AuditAggregateAdded} presentation={Ludots.Core.Gameplay.GAS.AttributeMutationOps.AuditPresentationAdded}");
            TestContext.Out.WriteLine("[DEBUG-a4f2] frame,total_bytes,aggregator_bytes,aggregator_removes,aggregator_playback_bytes,clear_bytes,clear_removes,clear_playback_bytes,spatial_bytes,new_cells,new_cell_bytes");
            for (int i = 0; i < frames; i++)
                TestContext.Out.WriteLine($"[DEBUG-a4f2] {i},{totalBytes[i]},{aggregatorBytes[i]},{aggregatorRemoved[i]},{aggregatorPlaybackBytes[i]},{clearBytes[i]},{clearRemoved[i]},{clearPlaybackBytes[i]},{spatialBytes[i]},{createdCells[i]},{createdCellBytes[i]}");
        }

        [Test]
        public void Probe_A4F2_PerSystemAllocationAttribution()
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
            var diag = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            var bytesBySystem = new Dictionary<(int Lane, string Name), long>();
            diag.AuditAllocationObserver = (lane, name, bytes) =>
            {
                var key = (lane, name);
                bytesBySystem.TryGetValue(key, out long total);
                bytesBySystem[key] = total + bytes;
            };

            for (int i = 0; i < 10; i++)
            {
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
            }

            bytesBySystem.Clear();
            const int frames = 30;
            for (int i = 0; i < frames; i++)
            {
                TickOnly(engine);
                projection.Update(FixedDeltaSeconds);
            }

            diag.AuditAllocationObserver = null;
            foreach (var pair in bytesBySystem.OrderByDescending(static pair => pair.Value))
            {
                if (pair.Value == 0)
                {
                    continue;
                }

                TestContext.Out.WriteLine(
                    $"[DEBUG-a4f2] system_alloc lane={(pair.Key.Lane == 0 ? "simulation" : "presentation")} " +
                    $"system={pair.Key.Name} bytes_per_frame={pair.Value / (double)frames:F0}");
            }
        }

        // [DEBUG-a4f2] interleaved A/B/A: owner-shared occlusion verdict vs per-item, same process, same frames.
        [Test]
        public void Probe_A4F2_OwnerSharedInterleaved()
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var warm = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, warm, simulation, ExpectedAgentCount);

            var counting = new CountingHeightmap(
                RequireService(engine, CoreServiceKeys.ContinuousHeightmap));
            var projection = new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState),
                () => counting);

            var hud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screen = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            WorldHudItem item = hud.GetSpan()[0];

            for (int i = 0; i < 60; i++)
            {
                item.WorldPosition.X += i % 2 == 0 ? .001f : -.001f;
                hud.TryAdd(item);
                projection.Update(0);
            }

            // Interleave shared/per-item frame by frame so thermal drift hits both arms equally.
            const int perArm = 25;
            var shared = new double[perArm];
            var perItem = new double[perArm];
            int sharedHidden = -1, perItemHidden = -1, sharedScreen = -1, perItemScreen = -1;
            long sharedRays = 0, perItemRays = 0;
            long alloc = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < perArm * 2; i++)
            {
                bool useShared = (i & 1) == 0;
                WorldHudToScreenSystem.TerrainOcclusionOwnerShared = useShared;
                WorldHudToScreenSystem.TerrainOcclusionVerdictCount = 0;
                WorldHudToScreenSystem.TerrainOcclusionHiddenCount = 0;
                counting.RaycastCalls = 0;

                item.WorldPosition.X += i % 2 == 0 ? .0013f : -.0013f;
                hud.TryAdd(item);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                projection.Update(0);
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                if (useShared)
                {
                    shared[i / 2] = ms;
                    sharedRays = counting.RaycastCalls;
                    sharedHidden = WorldHudToScreenSystem.TerrainOcclusionHiddenCount;
                    sharedScreen = screen.Count;
                }
                else
                {
                    perItem[i / 2] = ms;
                    perItemRays = counting.RaycastCalls;
                    perItemHidden = WorldHudToScreenSystem.TerrainOcclusionHiddenCount;
                    perItemScreen = screen.Count;
                }
            }
            alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
            WorldHudToScreenSystem.TerrainOcclusionOwnerShared = true;
            Array.Sort(shared);
            Array.Sort(perItem);
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] INTERLEAVED shared_median={shared[perArm / 2]:F4} per_item_median={perItem[perArm / 2]:F4} " +
                $"shared_rays={sharedRays} per_item_rays={perItemRays} " +
                $"shared_hidden={sharedHidden} per_item_hidden={perItemHidden} " +
                $"shared_screen={sharedScreen} per_item_screen={perItemScreen} bytes={alloc}");
            projection.Dispose();
        }

        // [DEBUG-a4f2] counts the sync-system path breakdown at 10K (fast vs slow vs owner-payload children).
        [Test]
        public void Probe_A4F2_SyncPathBreakdown()
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);

            var sync = RequirePresentationSystem<PresenterEntityTransformSyncSystem>(engine);
            for (int i = 0; i < 20; i++)
            {
                TickProjectionFrames(engine, projection, 1);
            }

            sync.ResetDebugCounters();
            TickProjectionFrames(engine, projection, 1);
            SyncPathBreakdownSample sample = new(sync.DebugEntityAnchoredProcessed, sync.DebugFastPathChildren, sync.DebugSlowPathChildren, sync.DebugSkippedNoMarker, sync.DebugOwnerPayloadChildren, sync.DebugOwnerPayloadFastApplied);
            var timing = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] sync_paths entity_anchored_changed={sample.EntityAnchored} fast_children={sample.Fast} " +
                $"slow_children={sample.Slow} skipped_no_marker={sample.Skipped} owner_payload_children={sample.OwnerPayload} " +
                $"owner_payload_fast_applied={sample.OwnerPayloadFast} " +
                $"sync_ms={timing.LastPresenterEntityTransformSyncMs:F3}");
        }

        private readonly record struct SyncPathBreakdownSample(long EntityAnchored, long Fast, long Slow, long Skipped, long OwnerPayload, long OwnerPayloadFast);

        // [DEBUG-a4f2] counts the sync-system path breakdown at 10K under movement (fast vs slow vs owner-payload children).
        [Test]
        public void Probe_A4F2_SyncPathBreakdownMoving()
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);

            var sync = RequirePresentationSystem<PresenterEntityTransformSyncSystem>(engine);

            var sink = new MassNavigationMovePlanExecutionSink(simulation);
            Entity[] agents = CollectMassNavigationAgents(engine, ExpectedAgentCount);
            for (int i = 0; i < agents.Length; i++)
            {
                Vector2 here = simulation.GetAgentWorldPositionCm(
                    engine.World.Get<MassNavigationAgentIndex>(agents[i]).Value);
                var intent = new MovePlanExecutionIntent
                {
                    CommandGroupToken = 1,
                    TargetWorldCm = new Vector2(10_000f - here.X, 10_000f - here.Y),
                    ProjectionHintWorldCm = new Vector2(10_000f - here.X, 10_000f - here.Y),
                    SpeedCmPerSec = 300f,
                    StopRadiusCm = 40f,
                    MinimumClearanceCm = 0f,
                    HasTarget = 1,
                    ResolveNavigableTarget = 1,
                    Mode = MovePlanExecutionMode.Individual,
                };
                if (sink.TryApply(engine.World, agents[i], in intent))
                {
                }
            }

            for (int i = 0; i < 20; i++)
            {
                TickProjectionFrames(engine, projection, 1);
            }

            sync.ResetDebugCounters();
            TickProjectionFrames(engine, projection, 1);
            var sample = new SyncPathBreakdownSample(sync.DebugEntityAnchoredProcessed, sync.DebugFastPathChildren, sync.DebugSlowPathChildren, sync.DebugSkippedNoMarker, sync.DebugOwnerPayloadChildren, sync.DebugOwnerPayloadFastApplied);
            var timing = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            TestContext.Out.WriteLine(
                $"[DEBUG-a4f2] sync_paths_moving entity_anchored_changed={sample.EntityAnchored} fast_children={sample.Fast} " +
                $"slow_children={sample.Slow} skipped_no_marker={sample.Skipped} owner_payload_children={sample.OwnerPayload} " +
                $"owner_payload_fast_applied={sample.OwnerPayloadFast} " +
                $"sync_ms={timing.LastPresenterEntityTransformSyncMs:F3}");
        }

        [Test]
        public void Showcase_UnchangedRelationshipRevisionDoesNotRepeat10kDomainResolution()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, ExpectedAgentCount);

            MassNavigationAuthoredAgentBindingSystem bindingSystem = RequireSystem<MassNavigationAuthoredAgentBindingSystem>(
                engine,
                SystemGroup.RuntimeEntityBinding);
            ControlDomainQuery controlDomains = RequireService(engine, CoreServiceKeys.ControlDomainQuery);
            uint relationshipRevision = controlDomains.Revision;
            int resolutionCount = bindingSystem.DomainResolutionCount;
            Assert.That(resolutionCount, Is.GreaterThanOrEqualTo(ExpectedAgentCount));

            TickProjectionFrames(engine, hudProjection, 3);

            Assert.That(controlDomains.Revision, Is.EqualTo(relationshipRevision));
            Assert.That(bindingSystem.DomainResolutionCount, Is.EqualTo(resolutionCount),
                "Stable 10K fixed steps must consume the committed domain projection without per-agent relationship queries.");
        }

        [Test]
        public void Showcase_CommandSourceCapacityCoversAuthoredAgentSet()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            Assert.That(simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.ScenarioRuntime.RuntimeCapacity.MovePlanExecutionMemberCapacity, Is.GreaterThanOrEqualTo(expectedAgents));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            Entity[] agents = CollectMassNavigationAgents(engine, expectedAgents);
            Entity localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            ReplaceCommandSource(engine, localPlayer, agents);

            Assert.That(SnapshotCommandSource(engine), Has.Length.EqualTo(expectedAgents));
        }

        [Test]
        public void Showcase_PeriodicHealthChangesReachBarsAndNumbersWithStableIdentities()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            AssertScenarioAgentTemplatesDriveHealthPeriodically(engine, simulation);

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);
            AssertScreenHudIdentityStableAcrossProjectionFrames(engine, hudProjection, HudStabilityObservationFrames);

            Dictionary<int, AgentHealthSample> before = CaptureAgentHealth(engine, expectedAgents);
            Dictionary<int, int> effectTicksBefore = CaptureAgentEffectTicks(engine, expectedAgents);
            Dictionary<int, AgentHealthHudSample> hudBefore = CaptureHealthHud(engine, expectedAgents);
            for (int period = 0; period < 2; period++)
            {
                AdvanceFixedClock(engine, hudProjection, HealthObservationTicks);
                Dictionary<int, AgentHealthSample> after = CaptureAgentHealth(engine, expectedAgents);
                Dictionary<int, int> effectTicksAfter = CaptureAgentEffectTicks(engine, expectedAgents);
                Dictionary<int, AgentHealthHudSample> hudAfter = CaptureHealthHud(engine, expectedAgents);
                AssertEveryEffectAdvanced(effectTicksBefore, effectTicksAfter);
                AssertAgentHealthChanges(before, after);
                AssertHealthHudChanges(hudBefore, hudAfter);
                AssertScreenHudIdentityStableAcrossProjectionFrames(engine, hudProjection, 1);
                before = after;
                effectTicksBefore = effectTicksAfter;
                hudBefore = hudAfter;
            }
        }

        [TestCase(1_000)]
        [TestCase(5_000)]
        [TestCase(10_000)]
        public void Showcase_MovingAgentsKeepBothHudItemsWithoutAllocating(int movingAgents)
        {
            using var engine = CreateEngine();
            StartStartupMap(engine);
            var simulation = RequireMassNavigationSimulation(engine);
            var projection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, projection, simulation, ExpectedAgentCount);
            var agents = CollectMassNavigationAgents(engine, ExpectedAgentCount);
            var hud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var timing = RequireService(engine, CoreServiceKeys.PresentationTimingDiagnostics);
            var sync = RequirePresentationSystem<PresenterEntityTransformSyncSystem>(engine);
            var emit = RequirePresentationSystem<PresenterEmitSystem>(engine);
            var before = hud.GetSpan().ToArray();
            var movedOwners = new HashSet<Entity>(agents.AsSpan(0, movingAgents).ToArray());
            int movedHudPresenterCount = 0;
            var retainedQuery = new QueryDescription().WithAll<PresenterState, PresenterEmitCache, PerfRetainedPresentationRequest>();
            foreach (ref var chunk in engine.World.Query(in retainedQuery))
            {
                var states = chunk.GetSpan<PresenterState>();
                foreach (int index in chunk)
                {
                    if (movedOwners.Contains(states[index].OwnerEntity)) movedHudPresenterCount++;
                }
            }
            Assert.That(movedHudPresenterCount, Is.EqualTo(movingAgents * 2));
            const int warmup = 16;
            const int samples = 17;
            var syncMs = new double[samples];
            var emitMs = new double[samples];
            long allocatedBytes = 0;
            for (int frame = 0; frame < warmup + samples; frame++)
            {
                hud.ClearContentDeltas();
                for (int i = 0; i < movingAgents; i++)
                {
                    engine.World.Get<VisualTransform>(agents[i]).Position.X += 1f;
                }

                long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                sync.Update(FixedDeltaSeconds);
                long afterSync = Stopwatch.GetTimestamp();
                emit.Update(FixedDeltaSeconds);
                long afterEmit = Stopwatch.GetTimestamp();
                long frameAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
                if (frame >= warmup)
                {
                    int sample = frame - warmup;
                    syncMs[sample] = (afterSync - start) * 1000d / Stopwatch.Frequency;
                    emitMs[sample] = (afterEmit - afterSync) * 1000d / Stopwatch.Frequency;
                    allocatedBytes += frameAllocation;
                    Assert.That(timing.PresenterEmitRetainedCountLastFrame, Is.EqualTo(movingAgents * 2));
                    Assert.That(timing.PresenterRetainedHudPositionUpdatesLastFrame, Is.EqualTo(movingAgents * 2));
                    Assert.That(timing.PresenterRetainedHudProjectionReusesLastFrame, Is.Zero);
                }
            }

            int checkedItems = 0;
            foreach (var item in before)
            {
                if (!movedOwners.Contains(item.Owner)) continue;
                Assert.That(hud.TryGetByStableId(item.StableId, out var after), Is.True);
                Assert.That(after.WorldPosition.X, Is.EqualTo(item.WorldPosition.X + warmup + samples).Within(0.001f));
                Assert.That(after.Owner, Is.EqualTo(item.Owner));
                Assert.That(after.Kind, Is.EqualTo(item.Kind));
                Assert.That(after.Value0, Is.EqualTo(item.Value0));
                checkedItems++;
            }

            Assert.That(checkedItems, Is.EqualTo(movingAgents * 2));
            Assert.That(hud.Count, Is.EqualTo(before.Length));
            Assert.That(hud.DroppedTotal, Is.Zero);
            Array.Sort(syncMs);
            Array.Sort(emitMs);
            TestContext.Out.WriteLine($"Moving agents={movingAgents} sync median={syncMs[samples / 2]:F3}ms p95={syncMs[^1]:F3}ms; emit median={emitMs[samples / 2]:F3}ms p95={emitMs[^1]:F3}ms; allocated={allocatedBytes / samples} B/frame");
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Showcase_MouseBoxAcquisition_AcquiresVisibleMassNavigationAgents()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);
            AssertLocalScenarioAgentsAreCommandable(engine);

            var backend = RequireMutableInputBackend(engine);
            CommandSourceDragGesture gesture = ResolveVisibleAgentDragGesture(engine);
            CommandSourceDiagnostics before = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(before.VisibleSelectable, Is.GreaterThan(0), before.ToString());
            Assert.That(before.ScreenIntersecting, Is.GreaterThan(0), before.ToString());
            Assert.That(before.EligibleIntersecting, Is.GreaterThan(0), before.ToString());

            DriveCommandSourceBoxAcquisition(engine, hudProjection, backend, gesture);
            TickProjectionFrames(engine, hudProjection, 2);

            Entity[] commandActors = SnapshotCommandSource(engine);
            CommandSourceDiagnostics after = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(commandActors.Length, Is.GreaterThan(0), after.ToString());
            Assert.That(CountActiveCommandMarkers(engine), Is.EqualTo(commandActors.Length), after.ToString());
        }

        [Test]
        public void Showcase_MouseBoxAcquisition_RightClickIssuesOrdersForCommandableAgents()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            var backend = RequireMutableInputBackend(engine);
            CommandSourceDragGesture gesture = ResolveVisibleAgentDragGesture(engine);
            DriveCommandSourceBoxAcquisition(engine, hudProjection, backend, gesture);
            TickProjectionFrames(engine, hudProjection, 2);

            Entity[] commandActors = SnapshotCommandSource(engine);
            CommandSourceDiagnostics commandSourceDiagnostics = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(commandActors.Length, Is.GreaterThan(0), commandSourceDiagnostics.ToString());
            AssertCommandActorsAreCommandable(engine, commandActors);

            int activeOrdersBefore = CountActiveMoveOrders(engine, commandActors);
            Vector2[] positionsBefore = CaptureCommandActorWorldPositions(engine, simulation, commandActors);
            Vector2 commandScreenPoint = ResolveCommandTargetScreenPoint(engine, simulation, commandActors);

            int appliedCommands = DriveRightClickCommandFrame(engine, hudProjection, backend, commandScreenPoint);

            string orderDebug = engine.GlobalContext.TryGetValue(LocalOrderSourceHelper.LastOrderDebugKey, out object? order)
                ? order?.ToString() ?? "<null>" : "<missing>";
            string groundDebug = engine.GlobalContext.TryGetValue(LocalOrderSourceHelper.LastGroundWorldDebugKey, out object? ground)
                ? ground?.ToString() ?? "<null>" : "<missing>";
            Assert.That(appliedCommands, Is.GreaterThan(0), commandSourceDiagnostics + $"; order={orderDebug}; ground={groundDebug}");
            Assert.That(simulation.LastOrderMemberCount, Is.EqualTo(commandActors.Length), commandSourceDiagnostics.ToString());
            Assert.That(CountActiveMoveOrders(engine, commandActors), Is.GreaterThan(activeOrdersBefore), commandSourceDiagnostics.ToString());

            TickProjectionFrames(engine, hudProjection, MovementObservationFrames);
            Assert.That(
                CountMovedCommandActors(engine, simulation, commandActors, positionsBefore),
                Is.GreaterThan(0),
                commandSourceDiagnostics.ToString());

            backend.SetButton(MouseRightButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 2);
        }

        private static void StartStartupMap(GameEngine engine)
        {
            Assert.That(engine.MergedConfig.StartupMapId, Is.Not.Empty);
            Assert.That(engine.MergedConfig.HasStartupLocalSeats, Is.True);
            Assert.That(engine.MergedConfig.StartupLocalSeats[0].PlayerId, Is.GreaterThan(0));

            engine.Start();
            engine.LoadStartupMap();
            AssertStartupParticipantBindings(engine);
            WaitForMassNavigationRuntimeReady(engine);
        }

        private static MassNavigationSimulationRuntime RequireMassNavigationSimulation(GameEngine engine)
        {
            return RequireService(engine, MassNavigationKeys.RuntimeBinding).RequireCurrent();
        }

        private static void WaitForMassNavigationRuntimeReady(GameEngine engine)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (MassNavigationIds.IsCurrentNavigationRuntimeReady(engine))
                {
                    return;
                }

                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }

            MassNavigationRuntimeBinding binding = RequireService(engine, MassNavigationKeys.RuntimeBinding);
            Assert.Fail(
                $"MassNavigation runtime did not become prepared within {MaxWarmupFrames} frames. " +
                $"currentMap={engine.CurrentMapSession?.MapId.Value ?? "<none>"}, bindingMap={binding.CurrentMapId.Value ?? "<none>"}, revision={binding.Revision}, preparedRevision={binding.PreparedRevision}.");
        }

        private static void AssertStartupParticipantBindings(GameEngine engine)
        {
            int playerId = engine.MergedConfig.StartupLocalSeats[0].PlayerId;
            Entity localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.IsAlive(localPlayer), Is.True);

            var players = RequireService(engine, CoreServiceKeys.PlayerEntityLookup);
            Assert.That(players.TryGet(playerId, out Entity playerEntity), Is.True);
            Assert.That(playerEntity, Is.EqualTo(localPlayer));

            var session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Startup map session is missing.");
            PlayerBindingData? playerBinding = null;
            for (int i = 0; i < session.MapConfig.Players.Count; i++)
            {
                PlayerBindingData binding = session.MapConfig.Players[i];
                if (binding.PlayerId == playerId)
                {
                    playerBinding = binding;
                    break;
                }
            }

            Assert.That(playerBinding, Is.Not.Null);

            var teams = RequireService(engine, CoreServiceKeys.TeamEntityLookup);
            Assert.That(teams.TryGet(playerBinding!.TeamId, out Entity teamEntity), Is.True);
            Assert.That(engine.World.IsAlive(teamEntity), Is.True);
            Assert.That(engine.World.TryGet(localPlayer, out PlayerIdentity identity), Is.True);
            Assert.That(identity.PlayerId, Is.EqualTo(playerId));
            Assert.That(engine.World.TryGet(localPlayer, out PlayerOwner owner), Is.True);
            Assert.That(owner.PlayerId, Is.EqualTo(playerId));
            Assert.That(engine.World.TryGet(localPlayer, out Team team), Is.True);
            Assert.That(team.Id, Is.EqualTo(playerBinding.TeamId));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods),
                Path.Combine(repoRoot, "assets"));
            ApplyHostAssets(engine);
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine);
            return engine;
        }

        private static TSystem RequireSystem<TSystem>(GameEngine engine, SystemGroup group)
            where TSystem : class, ISystem<float>
        {
            FieldInfo field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine system groups field is unavailable.");
            var groups = field.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>
                ?? throw new InvalidOperationException("GameEngine system groups could not be inspected.");
            if (!groups.TryGetValue(group, out List<ISystem<float>>? systems))
            {
                throw new InvalidOperationException($"System group {group} is not registered.");
            }

            for (int i = 0; i < systems.Count; i++)
            {
                if (systems[i] is TSystem system)
                {
                    return system;
                }
            }

            throw new InvalidOperationException($"System {typeof(TSystem).Name} is not registered in group {group}.");
        }

        private static TSystem RequirePresentationSystem<TSystem>(GameEngine engine)
            where TSystem : class, ISystem<float>
        {
            FieldInfo field = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine presentation systems field is unavailable.");
            var systems = (List<ISystem<float>>)field.GetValue(engine)!;
            foreach (var system in systems)
            {
                if (system is TSystem result) return result;
            }

            throw new InvalidOperationException($"Presentation system {typeof(TSystem).Name} is not registered.");
        }

        private static void ApplyHostAssets(GameEngine engine)
        {
            var meshAssets = RequireService(engine, CoreServiceKeys.PresentationMeshAssetRegistry);
            var materialAssets = RequireService(engine, CoreServiceKeys.PresentationMaterialRegistry);
            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new MutableInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static MutableInputBackend RequireMutableInputBackend(GameEngine engine)
        {
            return RequireService(engine, CoreServiceKeys.InputBackend) as MutableInputBackend
                ?? throw new InvalidOperationException("MassNavigation production path test requires the mutable input backend.");
        }

        private static WorldHudToScreenSystem CreateHudProjection(GameEngine engine)
        {
            return new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState),
                () => engine.GetService(CoreServiceKeys.ContinuousHeightmap));
        }

        private static ProjectionSample WaitForProductionProjection(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MassNavigationSimulationRuntime simulation,
            int expectedAgents)
        {
            ProjectionSample lastSample = default;
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                lastSample = CaptureProjectionSample(engine, simulation);
                if (simulation.NavigationAgentCount == expectedAgents &&
                    lastSample.MinimapSnapshot.ZoomBand == MinimapZoomBand.Strategic &&
                    lastSample.MinimapScreenMarkers >= expectedAgents &&
                    lastSample.MinimapSnapshot.VisibleMarkerCount >= expectedAgents &&
                    lastSample.WorldHudBars >= expectedAgents &&
                    lastSample.WorldHudText >= expectedAgents &&
                    lastSample.ScreenHudBars >= expectedAgents &&
                    lastSample.ScreenHudText >= expectedAgents)
                {
                    return lastSample;
                }
            }

            Assert.Fail(
                $"MassNavigation showcase did not project {expectedAgents} agents to minimap and HUD within {MaxWarmupFrames} frames; {lastSample.Diagnostics}");
            return default;
        }

        private static ProjectionSample CaptureProjectionSample(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            var minimapRuntime = RequireService(engine, CoreServiceKeys.MinimapRuntime);
            var minimapMarkers = RequireService(engine, CoreServiceKeys.MinimapMarkerBuffer);
            var minimapScreenMarkers = RequireService(engine, CoreServiceKeys.MinimapScreenMarkerBuffer);
            var worldHud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            MinimapDebugSnapshot minimapSnapshot = minimapRuntime.CaptureDebugSnapshot();
            int worldHudBars = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);
            int worldHudText = CountWorldHudItems(worldHud, WorldHudItemKind.Text);
            return new ProjectionSample(
                minimapSnapshot,
                minimapScreenMarkers.Count,
                worldHudBars,
                worldHudText,
                screenHud.BarCount,
                screenHud.TextCount,
                BuildDiagnostics(
                    simulation,
                    minimapRuntime,
                    minimapSnapshot,
                    minimapMarkers,
                    minimapScreenMarkers,
                    worldHud,
                    screenHud,
                    worldHudBars,
                    worldHudText));
        }

        private static void AssertFixedAnchorChain(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int sampleCount,
            float toleranceCm)
        {
            int sampled = 0;
            float toleranceSq = toleranceCm * toleranceCm;
            engine.World.Query(in MassNavigationAgentQuery, (Entity entity, ref MassNavigationAgent agent, ref MassNavigationAgentIndex agentIndex, ref WorldPositionCm worldPosition) =>
            {
                if (sampled >= sampleCount)
                {
                    return;
                }

                Assert.That(engine.World.TryGet(entity, out VisualTransform visual), Is.True,
                    $"Agent {agentIndex.Value} is missing VisualTransform.");
                Assert.That(engine.World.TryGet(entity, out PresentationOwnerHasPresenterPayload payload), Is.True,
                    $"Agent {agentIndex.Value} is missing presenter payload.");
                Assert.That(payload.RootCount, Is.EqualTo(1),
                    $"Agent {agentIndex.Value} must have exactly one presenter root.");
                Assert.That(engine.World.IsAlive(payload.SingleRootPresenter), Is.True,
                    $"Agent {agentIndex.Value} presenter root is not alive.");
                Assert.That(engine.World.TryGet(payload.SingleRootPresenter, out PresenterState presenterState), Is.True,
                    $"Agent {agentIndex.Value} presenter root has no PresenterState.");
                Assert.That(presenterState.OwnerEntity, Is.EqualTo(entity),
                    $"Agent {agentIndex.Value} presenter root owner mismatch.");
                Assert.That(presenterState.StableId, Is.GreaterThan(0),
                    $"Agent {agentIndex.Value} presenter root has no stable id.");
                Assert.That(engine.World.TryGet(payload.SingleRootPresenter, out PresenterWorldPlanePosition presenterPosition), Is.True,
                    $"Agent {agentIndex.Value} presenter root has no plane position.");

                Vector2 solverCm = simulation.GetAgentWorldPositionCm(agentIndex.Value);
                Vector2 ecsCm = worldPosition.Value.ToVector2();
                Vector2 visualCm = WorldPlane2D.VisualMetersToLogicCm(in visual.Position);
                Vector2 presenterCm = presenterPosition.ValueCm;
                Assert.That(Vector2.DistanceSquared(solverCm, ecsCm), Is.LessThanOrEqualTo(toleranceSq),
                    $"Agent {agentIndex.Value} solver/ECS anchor diverged: {solverCm} vs {ecsCm}.");
                Assert.That(Vector2.DistanceSquared(ecsCm, visualCm), Is.LessThanOrEqualTo(toleranceSq),
                    $"Agent {agentIndex.Value} ECS/VisualTransform anchor diverged: {ecsCm} vs {visualCm}.");
                Assert.That(Vector2.DistanceSquared(visualCm, presenterCm), Is.LessThanOrEqualTo(toleranceSq),
                    $"Agent {agentIndex.Value} VisualTransform/presenter root anchor diverged: {visualCm} vs {presenterCm}.");
                sampled++;
            });

            Assert.That(sampled, Is.EqualTo(sampleCount), $"Expected {sampleCount} fixed anchor samples, got {sampled}.");
        }

        private static void TickProjectionFrames(GameEngine engine, WorldHudToScreenSystem hudProjection, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimapRuntime &&
                    engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is MinimapMarkerBuffer minimapMarkers &&
                    engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer) is MinimapScreenMarkerBuffer minimapScreenMarkers)
                {
                    minimapRuntime.Refresh(engine, minimapMarkers, minimapScreenMarkers);
                }

                hudProjection.Update(FixedDeltaSeconds);
            }
        }

        private static void AdvanceFixedClock(GameEngine engine, WorldHudToScreenSystem hudProjection, int ticks)
        {
            IClock clock = RequireService(engine, CoreServiceKeys.Clock);
            int startTick = clock.Now(ClockDomainId.FixedFrame);
            int hostFrames = 0;
            while (clock.Now(ClockDomainId.FixedFrame) - startTick < ticks)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                Assert.That(++hostFrames, Is.LessThanOrEqualTo(ticks * 8),
                    "MassNavigation simulation did not advance the requested FixedFrame window.");
            }
        }

        private static void DriveCommandSourceBoxAcquisition(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MutableInputBackend backend,
            in CommandSourceDragGesture gesture)
        {
            backend.SetMousePosition(gesture.Start);
            backend.SetButton(MouseLeftButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseLeftButtonPath, true);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetMousePosition(gesture.End);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseLeftButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);
        }

        private static int DriveRightClickCommandFrame(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MutableInputBackend backend,
            Vector2 position)
        {
            Entity player = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            ref InteractionContextInstance context = ref engine.World.Get<InteractionContextInstance>(player);
            var collections = RequireService(engine, CoreServiceKeys.EntityCollectionStore);
            Assert.That(context.ContextEntity, Is.EqualTo(player));
            Assert.That(context.ActiveCollectionKeyId, Is.EqualTo(collections.KeyRegistry.GetId("selected")));
            Assert.That(context.CommandIntentProfileId, Is.GreaterThan(0));
            backend.SetMousePosition(position);
            backend.SetButton(MouseRightButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseRightButtonPath, true);
            int applied = 0;
            TickProjectionFrames(engine, hudProjection, 2);
            applied += RequireMassNavigationSimulation(engine).CommandCountFrame;
            Assert.That(RequireService(engine, CoreServiceKeys.InputHandler).IsDown("Command"), Is.True);
            var groups = (Dictionary<SystemGroup, List<ISystem<float>>>)typeof(GameEngine)
                .GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
            foreach (var systems in groups.Values)
            foreach (var system in systems)
            {
                if (system.GetType().Name != "MassNavigationLargeWorldLocalOrderSourceSystem") continue;
                var mapping = (InputOrderMappingSystem)system.GetType()
                    .GetField("_mapping", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system)!;
                Assert.That(mapping.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Submitted),
                    $"Command routing: {mapping.LastActivationResult.State}, {mapping.LastActivationResult.Rejection}");
            }

            backend.SetButton(MouseRightButtonPath, false);
            for (int frame = 0; frame < 4; frame++)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                applied += RequireMassNavigationSimulation(engine).CommandCountFrame;
            }
            return applied;
        }

        private static Vector2 ResolveCommandTargetScreenPoint(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 targetWorldCm = ResolveCommandTargetWorldCm(engine, simulation, commandActors);
            Vector2 candidate = WorldToScreen(engine, targetWorldCm);
            if (AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                    engine.GlobalContext,
                    candidate,
                    out WorldCmInt2 worldCm) &&
                simulation.ContainsWorldPoint(worldCm.X, worldCm.Y))
            {
                return candidate;
            }

            throw new InvalidOperationException("MassNavigation production path test could not resolve the command target to commandable ground.");
        }

        private static Vector2 ResolveCommandTargetWorldCm(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 center = ResolveCommandActorCenterWorldCm(engine, simulation, commandActors);
            float offsetCm = MathF.Min(simulation.SolverWindowWidthCm, simulation.SolverWindowHeightCm) * CommandTargetOffsetWindowScale;
            ReadOnlySpan<Vector2> directions = stackalloc Vector2[]
            {
                new(1f, 0f),
                new(0f, 1f),
                new(-1f, 0f),
                new(0f, -1f),
                Vector2.Normalize(new Vector2(1f, 1f)),
                Vector2.Normalize(new Vector2(1f, -1f)),
                Vector2.Normalize(new Vector2(-1f, 1f)),
                Vector2.Normalize(new Vector2(-1f, -1f))
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 candidate = ClampWorldPoint(simulation.WorldBounds, center + (directions[i] * offsetCm));
                if (Vector2.DistanceSquared(candidate, center) < offsetCm * offsetCm * 0.25f)
                {
                    continue;
                }

                Vector2 screen = WorldToScreen(engine, candidate);
                if (!IsScreenPointInsideView(engine, screen) ||
                    IsScreenPointInsideMinimap(engine, screen))
                {
                    continue;
                }

                if (AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                        engine.GlobalContext,
                        screen,
                        out WorldCmInt2 resolved) &&
                    simulation.ContainsWorldPoint(resolved.X, resolved.Y))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("MassNavigation production path test could not find a visible off-center command target.");
        }

        private static Vector2 ResolveCommandActorCenterWorldCm(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out Vector2 worldCm))
                {
                    sum += worldCm;
                    count++;
                }
            }

            if (count <= 0)
            {
                throw new InvalidOperationException("MassNavigation production path test has no command actor positions.");
            }

            return sum / count;
        }

        private static Vector2 ClampWorldPoint(WorldAabbCm bounds, Vector2 point)
        {
            return new Vector2(
                Math.Clamp(point.X, bounds.Left, bounds.Right),
                Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
        }

        private static Vector2 WorldToScreen(GameEngine engine, Vector2 worldCm)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            return projector.WorldToScreen(new Vector3(worldCm.X / 100f, 0f, worldCm.Y / 100f));
        }

        private static bool IsScreenPointInsideView(GameEngine engine, Vector2 screen)
        {
            var view = RequireService(engine, CoreServiceKeys.ViewController);
            return float.IsFinite(screen.X) &&
                float.IsFinite(screen.Y) &&
                screen.X >= 0f &&
                screen.X <= view.Resolution.X &&
                screen.Y >= 0f &&
                screen.Y <= view.Resolution.Y;
        }

        private static bool IsScreenPointInsideMinimap(GameEngine engine, Vector2 screen)
        {
            return engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimapRuntime &&
                (minimapRuntime.ContainsField(screen) ||
                 minimapRuntime.ContainsZoomSlider(screen) ||
                 minimapRuntime.ContainsPresetToggle(screen) ||
                 minimapRuntime.ContainsRotateToggle(screen));
        }

        private static CommandSourceDragGesture ResolveVisibleAgentDragGesture(GameEngine engine)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            var view = RequireService(engine, CoreServiceKeys.ViewController);
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            IContinuousHeightmap? heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Vector2 resolution = view.Resolution;
            float padding = commandSourceConfig.ClickPickRadiusPixels + commandSourceConfig.DragThresholdPixels;
            bool hasBounds = false;
            ScreenRect bounds = default;

            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible ||
                    !SpatialBoundsUtility.TryProjectScreenBounds(
                        engine.World,
                        entity,
                        projector,
                        out ScreenRect candidate,
                        new ScreenProjectionPoseContext(1f, heightmap)))
                {
                    return;
                }

                if (!hasBounds)
                {
                    bounds = candidate;
                    hasBounds = true;
                    return;
                }

                bounds = new ScreenRect(
                    MathF.Min(bounds.MinX, candidate.MinX),
                    MathF.Min(bounds.MinY, candidate.MinY),
                    MathF.Max(bounds.MaxX, candidate.MaxX),
                    MathF.Max(bounds.MaxY, candidate.MaxY));
            });

            if (!hasBounds)
            {
                Assert.Fail("MassNavigation showcase has no projected selectable agents to drive a box selection gesture.");
            }

            Vector2 start = new(
                Clamp(bounds.MinX - padding, 0f, resolution.X),
                Clamp(bounds.MinY - padding, 0f, resolution.Y));
            Vector2 end = new(
                Clamp(bounds.MaxX + padding, 0f, resolution.X),
                Clamp(bounds.MaxY + padding, 0f, resolution.Y));

            float minExtent = commandSourceConfig.DragThresholdPixels + padding;
            if (MathF.Abs(end.X - start.X) <= commandSourceConfig.DragThresholdPixels)
            {
                end.X = Clamp(start.X + minExtent, 0f, resolution.X);
            }

            if (MathF.Abs(end.Y - start.Y) <= commandSourceConfig.DragThresholdPixels)
            {
                end.Y = Clamp(start.Y + minExtent, 0f, resolution.Y);
            }

            return new CommandSourceDragGesture(start, end, ScreenRect.FromPoints(start, end));
        }

        private static CommandSourceDiagnostics CaptureCommandSourceDiagnostics(GameEngine engine, in ScreenRect marquee)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            IContinuousHeightmap? diagHeightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Entity localPlayer = ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity local) &&
                engine.World.IsAlive(local)
                    ? local
                    : default;
            bool hasCurrentView = TryDescribeCommandSourceView(engine, out _);
            bool hasKnowledgeResolver = KnowledgeProjectionConsumer.HasResolver(engine.GlobalContext);
            bool localPlayerResolved = localPlayer != default && engine.World.IsAlive(localPlayer);
            Team localTeam = default;
            bool localHasTeam = localPlayerResolved && engine.World.TryGet(localPlayer, out localTeam);
            int localTeamId = localHasTeam ? localTeam.Id : 0;
            int selectedCount = SnapshotCommandSource(engine).Length;
            int visibleSelectable = 0;
            int projected = 0;
            int screenIntersecting = 0;
            int eligible = 0;
            int eligibleIntersecting = 0;
            int liveVisible = 0;
            ScreenRect dragRect = marquee;

            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                visibleSelectable++;
                bool hasProjectedBounds = SpatialBoundsUtility.TryProjectScreenBounds(
                    engine.World,
                    entity,
                    projector,
                    out ScreenRect bounds,
                    new ScreenProjectionPoseContext(1f, diagHeightmap));
                if (hasProjectedBounds)
                {
                    projected++;
                }

                bool intersects = hasProjectedBounds && bounds.Intersects(in dragRect);
                if (intersects)
                {
                    screenIntersecting++;
                }

                if (hasKnowledgeResolver &&
                    localPlayerResolved &&
                    KnowledgeProjectionConsumer.CanReadPositionForViewer(
                        engine.World,
                        engine.GlobalContext,
                        localPlayer,
                        entity,
                        KnowledgePositionAccess.Live,
                        out KnowledgeProjection projection) &&
                    projection.Presence == KnowledgePresence.LiveVisible)
                {
                    liveVisible++;
                }

                bool canAcquire = localPlayerResolved &&
                    CommandSourceEligibility.CanAcquire(
                        engine.World,
                        engine.GlobalContext,
                        localPlayer,
                        entity,
                        (commandSourceConfig.TargetFilter ?? throw new InvalidOperationException("commandSource.targetFilter is missing.")).ParseRelationFilter());
                if (canAcquire)
                {
                    eligible++;
                }

                if (intersects && canAcquire)
                {
                    eligibleIntersecting++;
                }
            });

            return new CommandSourceDiagnostics(
                localPlayer,
                localTeamId,
                localHasTeam,
                hasCurrentView,
                hasKnowledgeResolver,
                visibleSelectable,
                projected,
                screenIntersecting,
                liveVisible,
                eligible,
                eligibleIntersecting,
                selectedCount);
        }

        private static float Clamp(float value, float min, float max)
        {
            return MathF.Min(MathF.Max(value, min), max);
        }

        private static int CountActiveCommandMarkers(GameEngine engine)
        {
            var presenters = RequireService(engine, CoreServiceKeys.PresenterEntityRuntime);
            var definitions = RequireService(engine, CoreServiceKeys.PresenterDefinitionRegistry);
            int lightMarkerId = definitions.GetId(LightCommandMarkerPresenterId);
            int heavyMarkerId = definitions.GetId(HeavyCommandMarkerPresenterId);
            if (lightMarkerId <= 0 || heavyMarkerId <= 0)
            {
                throw new InvalidOperationException("MassNavigation command marker presenter definitions are not registered.");
            }

            int count = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (ref PresenterState state) =>
            {
                if (state.DefId == lightMarkerId ||
                    state.DefId == heavyMarkerId)
                {
                    count++;
                }
            });

            return count;
        }

        private static Vector2[] CaptureCommandActorWorldPositions(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            var positions = new Vector2[commandActors.Length];
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (!simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out positions[i]))
                {
                    throw new InvalidOperationException(DescribeEntityCommandState(engine, commandActors[i]));
                }
            }

            return positions;
        }

        private static int CountMovedCommandActors(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors,
            ReadOnlySpan<Vector2> positionsBefore)
        {
            if (positionsBefore.Length != commandActors.Length)
            {
                throw new InvalidOperationException("MassNavigation movement sample must match command actor count.");
            }

            int moved = 0;
            float epsilonSquared = MovementEpsilonCm * MovementEpsilonCm;
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out Vector2 current) &&
                    Vector2.DistanceSquared(current, positionsBefore[i]) > epsilonSquared)
                {
                    moved++;
                }
            }

            return moved;
        }

        private static void AssertCommandActorsAreCommandable(GameEngine engine, ReadOnlySpan<Entity> commandActors)
        {
            Entity localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            var controlDomains = RequireService(engine, CoreServiceKeys.ControlDomainQuery);
            for (int i = 0; i < commandActors.Length; i++)
            {
                Entity entity = commandActors[i];
                string diagnostics = DescribeEntityCommandState(engine, entity);
                Assert.That(engine.World.IsAlive(entity), Is.True, diagnostics);
                Assert.That(controlDomains.IsControllableBy(localPlayer, entity), Is.True, diagnostics);
                Assert.That(engine.World.Has<PlayerOwner>(entity), Is.False, diagnostics);
                Assert.That(engine.World.Has<Team>(entity), Is.False, diagnostics);
            }
        }

        private static void AssertLocalScenarioAgentsAreCommandable(GameEngine engine)
        {
            Entity localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            var controlDomains = RequireService(engine, CoreServiceKeys.ControlDomainQuery);
            MassNavigationSimulationRuntime simulation = RequireService(engine, MassNavigationKeys.RuntimeBinding).RequireCurrent();

            int totalAgents = 0;
            int controllableAgents = 0;
            int legacyIdentityMirrors = 0;
            var query = new QueryDescription().WithAll<MassNavigationAgent>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _) =>
            {
                totalAgents++;
                if (controlDomains.IsControllableBy(localPlayer, entity))
                {
                    controllableAgents++;
                }

                if (engine.World.Has<PlayerOwner>(entity) || engine.World.Has<Team>(entity))
                {
                    legacyIdentityMirrors++;
                }
            });

            Assert.That(totalAgents, Is.EqualTo(ExpectedAgentCount));
            Assert.That(
                controllableAgents,
                Is.EqualTo(simulation.AgentsPerTeam),
                "Exactly one scenario domain must be controllable by the startup player through ownership relationships.");
            Assert.That(
                legacyIdentityMirrors,
                Is.Zero,
                "MassNavigation agents must not mirror ownership or membership into PlayerOwner/Team components.");
        }

        private static string DescribeEntityCommandState(GameEngine engine, Entity entity)
        {
            string alive = engine.World.IsAlive(entity) ? "alive" : "dead";
            Entity localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            ControlDomainQuery? controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery);
            bool controllable = controlDomains?.IsControllableBy(localPlayer, entity) == true;
            string controlDomain = controlDomains != null && controlDomains.TryResolveControlDomain(entity, out Entity domain)
                ? domain.Id.ToString()
                : "none";
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            string relationFilter = commandSourceConfig.TargetFilter?.RelationFilter ?? string.Empty;
            return $"commandActor={entity.Id}, state={alive}, controlDomain={controlDomain}, controllable={controllable}, targetRelationFilter={relationFilter}";
        }

        private static int CountActiveMoveOrders(GameEngine engine, ReadOnlySpan<Entity> commandActors)
        {
            int count = 0;
            for (int i = 0; i < commandActors.Length; i++)
            {
                Entity entity = commandActors[i];
                if (engine.World.IsAlive(entity) &&
                    engine.World.TryGet(entity, out OrderBuffer orders) &&
                    orders.HasActive)
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity[] SnapshotCommandSource(GameEngine engine)
        {
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            return EntityCollectionContextRuntime.Snapshot(engine.GlobalContext, owner, "selected");
        }

        private static bool TryDescribeCommandSourceView(GameEngine engine, out EntityCollectionView view)
        {
            view = default;
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore collections))
            {
                return false;
            }

            return EntityCollectionContextRuntime.TryDescribeView(collections, owner, "selected", out view);
        }

        private static void ReplaceCommandSource(GameEngine engine, Entity owner, ReadOnlySpan<Entity> members)
        {
            EntityCollectionStore collections = RequireService(engine, CoreServiceKeys.EntityCollectionStore);
            var descriptor = EntityCollectionDescriptor.Create(
                "selected",
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                $"{members.Length} entity(s)");
            collections.Replace(owner, descriptor, members, owner);
        }

        private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        {
            T value = engine.GetService(key);
            return value ?? throw new InvalidOperationException($"{key.Name} service is missing.");
        }

        private static int CountWorldHudItems(WorldHudBatchBuffer worldHud, WorldHudItemKind kind)
        {
            int count = 0;
            ReadOnlySpan<WorldHudItem> span = worldHud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity[] CollectMassNavigationAgents(GameEngine engine, int expectedAgents)
        {
            var agents = new Entity[expectedAgents];
            int count = 0;
            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                Assert.That(count, Is.LessThan(agents.Length), "MassNavigation showcase authored more selectable visible agents than its scenario count.");
                agents[count++] = entity;
            });

            Assert.That(count, Is.EqualTo(expectedAgents));
            return agents;
        }

        private static void AssertScenarioAgentTemplatesDriveHealthPeriodically(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation)
        {
            var checkedTemplateIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < simulation.Config.Presentation.Teams.Length; i++)
            {
                MassNavigationTeamPresentationConfig team = simulation.Config.Presentation.Teams[i];
                AssertAgentTemplateDrivesHealthPeriodically(engine, team.LightTemplateId, checkedTemplateIds);
                AssertAgentTemplateDrivesHealthPeriodically(engine, team.HeavyTemplateId, checkedTemplateIds);
            }
        }

        private static void AssertAgentTemplateDrivesHealthPeriodically(
            GameEngine engine,
            string templateId,
            HashSet<string> checkedTemplateIds)
        {
            Assert.That(templateId, Is.Not.Empty);
            if (!checkedTemplateIds.Add(templateId))
            {
                return;
            }

            EntityTemplate template = engine.MapLoader.TemplateRegistry.Get(templateId)
                ?? throw new InvalidOperationException($"MassNavigation showcase agent template '{templateId}' is not registered.");
            Assert.That(
                template.OnSpawnEffect,
                Is.EqualTo("Effect.MassNavigation.Agent.HealthDrift"),
                $"MassNavigation 10k showcase template '{templateId}' must retain its authored periodic health effect.");
        }

        private static Dictionary<int, AgentHealthSample> CaptureAgentHealth(GameEngine engine, int expectedAgents)
        {
            int healthAttributeId = AttributeRegistry.GetId("Health");
            Assert.That(healthAttributeId, Is.GreaterThanOrEqualTo(0), "MassNavigation HUD health stability requires the Health attribute id.");

            var samples = new Dictionary<int, AgentHealthSample>(expectedAgents);
            var query = new QueryDescription().WithAll<MassNavigationAgent, AttributeBuffer>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref AttributeBuffer attributes) =>
            {
                samples.Add(
                    entity.Id,
                    new AgentHealthSample(
                        attributes.GetCurrent(healthAttributeId),
                        attributes.GetBase(healthAttributeId)));
            });

            Assert.That(samples.Count, Is.EqualTo(expectedAgents));
            return samples;
        }

        private static Dictionary<int, int> CaptureAgentEffectTicks(GameEngine engine, int expectedAgents)
        {
            var samples = new Dictionary<int, int>(expectedAgents);
            var query = new QueryDescription().WithAll<MassNavigationAgent, ActiveEffectContainer>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref ActiveEffectContainer effects) =>
            {
                Assert.That(effects.Count, Is.EqualTo(1), $"Agent {entity.Id} must have one periodic health effect.");
                Entity effectEntity = effects.GetEntity(0);
                Assert.That(engine.World.IsAlive(effectEntity), Is.True);
                Assert.That(engine.World.Has<GameplayEffect>(effectEntity), Is.True);
                samples.Add(entity.Id, engine.World.Get<GameplayEffect>(effectEntity).NextTickAtTick);
            });
            Assert.That(samples.Count, Is.EqualTo(expectedAgents));
            return samples;
        }

        private static void AssertEveryEffectAdvanced(
            IReadOnlyDictionary<int, int> before,
            IReadOnlyDictionary<int, int> after)
        {
            Assert.That(after.Count, Is.EqualTo(before.Count));
            foreach (KeyValuePair<int, int> pair in before)
            {
                Assert.That(after.TryGetValue(pair.Key, out int nextTick), Is.True);
                Assert.That(nextTick, Is.GreaterThan(pair.Value), $"Agent {pair.Key} periodic effect did not advance.");
            }
        }

        private static void AssertAgentHealthChanges(
            IReadOnlyDictionary<int, AgentHealthSample> before,
            IReadOnlyDictionary<int, AgentHealthSample> after)
        {
            Assert.That(after.Count, Is.EqualTo(before.Count));
            int changed = 0;
            foreach (KeyValuePair<int, AgentHealthSample> pair in before)
            {
                Assert.That(after.TryGetValue(pair.Key, out AgentHealthSample actual), Is.True);
                if (MathF.Abs(actual.Current - pair.Value.Current) > 0.0001f) changed++;
                Assert.That(actual.Base, Is.EqualTo(pair.Value.Base).Within(0.0001f), $"Agent {pair.Key} Health base changed.");
            }
            Assert.That(changed, Is.GreaterThan(before.Count / 2), "Periodic health drift must remain visible across the crowd.");
            TestContext.Out.WriteLine($"Health changed: {changed}/{before.Count}");
        }

        private static Dictionary<int, AgentHealthHudSample> CaptureHealthHud(GameEngine engine, int expectedAgents)
        {
            var worldHud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            var screenItems = new Dictionary<int, ScreenHudItem>(screenHud.Count);
            foreach (var item in screenHud.GetSpan()) screenItems.Add(item.StableId, item);
            var samples = new Dictionary<int, AgentHealthHudSample>(expectedAgents);
            foreach (var item in worldHud.GetSpan())
            {
                if (!engine.World.Has<MassNavigationAgent>(item.Owner)) continue;
                Assert.That(screenItems.TryGetValue(item.StableId, out var screen), Is.True);
                Assert.That(screen.DirtySerial, Is.EqualTo(item.DirtySerial));
                samples.TryGetValue(item.Owner.Id, out AgentHealthHudSample sample);
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    Assert.That(screen.Value0, Is.EqualTo(item.Value0).Within(0.0001f), $"Screen bar for agent {item.Owner.Id}");
                    sample = sample with { BarStableId = item.StableId, BarValue = item.Value0 };
                }
                else
                {
                    Assert.That(screen.Text, Is.EqualTo(item.Text));
                    sample = sample with { TextStableId = item.StableId, TextValue = item.Text.Arg0.AsInt32() };
                }
                samples[item.Owner.Id] = sample;
            }
            Assert.That(samples.Count, Is.EqualTo(expectedAgents));
            foreach (KeyValuePair<int, AgentHealthHudSample> pair in samples)
            {
                Assert.That(pair.Value.BarStableId, Is.GreaterThan(0), $"Agent {pair.Key} has no health bar.");
                Assert.That(pair.Value.TextStableId, Is.GreaterThan(0), $"Agent {pair.Key} has no health number.");
            }
            return samples;
        }

        private static void AssertHealthHudChanges(
            IReadOnlyDictionary<int, AgentHealthHudSample> before,
            IReadOnlyDictionary<int, AgentHealthHudSample> after)
        {
            int changedBars = 0;
            int changedTexts = 0;
            foreach (KeyValuePair<int, AgentHealthHudSample> pair in before)
            {
                Assert.That(after.TryGetValue(pair.Key, out AgentHealthHudSample actual), Is.True);
                Assert.That(actual.BarStableId, Is.EqualTo(pair.Value.BarStableId));
                Assert.That(actual.TextStableId, Is.EqualTo(pair.Value.TextStableId));
                if (MathF.Abs(actual.BarValue - pair.Value.BarValue) > 0.0001f) changedBars++;
                if (actual.TextValue != pair.Value.TextValue) changedTexts++;
            }
            Assert.That(changedBars, Is.GreaterThan(before.Count / 2), "Health bars must visibly follow periodic health changes.");
            Assert.That(changedTexts, Is.GreaterThan(before.Count / 2), "Health numbers must visibly follow periodic health changes.");
            TestContext.Out.WriteLine($"HUD changed: bars={changedBars}/{before.Count}, text={changedTexts}/{before.Count}");
        }

        private static void AssertScreenHudIdentityStableAcrossProjectionFrames(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            int frames)
        {
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            ScreenHudIdentitySnapshot before = CaptureScreenHudIdentity(screenHud);
            for (int i = 0; i < frames; i++)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                ScreenHudIdentitySnapshot after = CaptureScreenHudIdentity(screenHud);
                Assert.That(after.BarCount, Is.EqualTo(before.BarCount), $"Screen HUD bar count changed on frame {i + 1}.");
                Assert.That(after.TextCount, Is.EqualTo(before.TextCount), $"Screen HUD text count changed on frame {i + 1}.");
                Assert.That(after.BarIdentities, Is.EqualTo(before.BarIdentities), $"Screen HUD bar identities changed on frame {i + 1}.");
                Assert.That(after.TextIdentities, Is.EqualTo(before.TextIdentities), $"Screen HUD text identities changed on frame {i + 1}.");
            }
        }

        private static ScreenHudIdentitySnapshot CaptureScreenHudIdentity(ScreenHudBatchBuffer screenHud)
        {
            ReadOnlySpan<ScreenHudBarItem> bars = screenHud.GetBarSpan();
            ReadOnlySpan<ScreenHudTextItem> texts = screenHud.GetTextSpan();
            long[] barIdentities = new long[bars.Length];
            long[] textIdentities = new long[texts.Length];
            for (int i = 0; i < bars.Length; i++)
            {
                barIdentities[i] = bars[i].StableId;
            }

            for (int i = 0; i < texts.Length; i++)
            {
                textIdentities[i] = texts[i].StableId;
            }

            Array.Sort(barIdentities);
            Array.Sort(textIdentities);
            return new ScreenHudIdentitySnapshot(bars.Length, texts.Length, barIdentities, textIdentities);
        }

        private static string BuildDiagnostics(
            MassNavigationSimulationRuntime simulation,
            MinimapRuntime minimapRuntime,
            MinimapDebugSnapshot minimapSnapshot,
            MinimapMarkerBuffer minimapMarkers,
            MinimapScreenMarkerBuffer minimapScreenMarkers,
            WorldHudBatchBuffer worldHud,
            ScreenHudBatchBuffer screenHud,
            int worldHudBars,
            int worldHudText)
        {
            return string.Join(
                ", ",
                $"agents={simulation.AgentState.TotalAgents}",
                $"minimapVisible={minimapRuntime.Visible}",
                $"minimapPreset={minimapRuntime.Preset}",
                $"minimapBand={minimapSnapshot.ZoomBand}",
                $"minimapHalfExtentCm={minimapSnapshot.HalfExtentCm:0.###}",
                $"minimapMarkers={minimapMarkers.Count}",
                $"minimapScreenMarkers={minimapScreenMarkers.Count}",
                $"minimapVisibleMarkers={minimapSnapshot.VisibleMarkerCount}",
                $"worldHud={worldHud.Count}",
                $"worldHudBars={worldHudBars}",
                $"worldHudText={worldHudText}",
                $"screenHudBars={screenHud.BarCount}",
                $"screenHudText={screenHud.TextCount}");
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

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private readonly record struct ProjectionSample(
            MinimapDebugSnapshot MinimapSnapshot,
            int MinimapScreenMarkers,
            int WorldHudBars,
            int WorldHudText,
            int ScreenHudBars,
            int ScreenHudText,
            string Diagnostics);

        private readonly record struct CommandSourceDragGesture(Vector2 Start, Vector2 End, ScreenRect Marquee);

        private readonly record struct AgentHealthSample(float Current, float Base);

        private readonly record struct AgentHealthHudSample(int BarStableId, float BarValue, int TextStableId, int TextValue);

        private readonly record struct ScreenHudIdentitySnapshot(
            int BarCount,
            int TextCount,
            long[] BarIdentities,
            long[] TextIdentities);

        private readonly record struct CommandSourceDiagnostics(
            Entity LocalPlayer,
            int LocalTeamId,
            bool LocalHasTeam,
            bool HasCurrentView,
            bool HasKnowledgeResolver,
            int VisibleSelectable,
            int Projected,
            int ScreenIntersecting,
            int LiveVisible,
            int Eligible,
            int EligibleIntersecting,
            int CurrentCommandSourceCount)
        {
            public override string ToString()
            {
                return string.Join(
                    ", ",
                    $"localPlayer={LocalPlayer}",
                    $"localHasTeam={LocalHasTeam}",
                    $"localTeamId={LocalTeamId}",
                    $"hasCurrentView={HasCurrentView}",
                    $"hasKnowledgeResolver={HasKnowledgeResolver}",
                    $"visibleSelectable={VisibleSelectable}",
                    $"projected={Projected}",
                    $"screenIntersecting={ScreenIntersecting}",
                    $"liveVisible={LiveVisible}",
                    $"eligible={Eligible}",
                    $"eligibleIntersecting={EligibleIntersecting}",
                    $"currentCommandSourceCount={CurrentCommandSourceCount}");
            }
        }

        private sealed class MutableInputBackend : IInputBackend
        {
            private readonly HashSet<string> _pressedButtons = new(StringComparer.OrdinalIgnoreCase);
            private Vector2 _mousePosition;

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetMousePosition(Vector2 mousePosition)
            {
                _mousePosition = mousePosition;
            }

            public void SetButton(string devicePath, bool pressed)
            {
                if (pressed)
                {
                    _pressedButtons.Add(devicePath);
                    return;
                }

                _pressedButtons.Remove(devicePath);
            }
        }
    }
}
