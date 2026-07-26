using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Scripting;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Feature: open-world local wall edits stay as fast as the RTS fort for the same algorithm
/// Given the player authors RTS 8x8 and open-world 64x64 showcases with the same resident window
/// When they build then demolish the same authored wall through the formal GameEngine chain
/// Then open-world dirty publish and matched halo-safe interior full-bake throughput stay inside the authored ratio gates
/// And the full 64-tile algorithm-switch bootstrap still proves resident count/atomicity (diagnostic only across scenes)
/// And failure/overflow counters stay zero
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class DynamicNavBakeShowcasePerformanceAcceptanceTests
{
    [TestCase(NavBakeAlgorithmKind.LayeredSpan, TestName = "Feature_OpenWorldLocalWall_MatchesRtsPerformanceGates_LayeredSpan")]
    [TestCase(NavBakeAlgorithmKind.Cdt, TestName = "Feature_OpenWorldLocalWall_MatchesRtsPerformanceGates_Cdt")]
    [TestCase(NavBakeAlgorithmKind.Recast, TestName = "Feature_OpenWorldLocalWall_MatchesRtsPerformanceGates_Recast")]
    public void Feature_OpenWorldLocalWall_MatchesRtsPerformanceGates(NavBakeAlgorithmKind algorithm)
    {
        DynamicNavBakeShowcaseConfig rtsConfig = LoadShowcaseConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig openConfig = LoadShowcaseConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        Assert.That(rtsConfig.Benchmark, Is.Not.Null);
        Assert.That(openConfig.Benchmark, Is.Not.Null);
        AssertBenchmarkParity(rtsConfig.Benchmark, openConfig.Benchmark);

        DynamicNavBakeShowcaseBenchmarkConfig gates = rtsConfig.Benchmark;
        // Full GC before each scene so managed heap pressure from a prior scene does not
        // amplify the next scene's measured allocations. This does not compact the LOH
        // (no LargeObjectHeapCompactionMode) — Recast allocation evidence stays honest.
        StabilizeManagedHeapPrecondition();
        ScenePerfEvidence rts = MeasureSceneProduction(
            "NavBakeDynamicRtsShowcaseMod",
            DynamicNavBakeShowcaseIds.RtsMapId,
            rtsConfig,
            algorithm,
            gates);
        StabilizeManagedHeapPrecondition();
        ScenePerfEvidence open = MeasureSceneProduction(
            "NavBakeOpenWorld64x64ShowcaseMod",
            DynamicNavBakeShowcaseIds.OpenWorldMapId,
            openConfig,
            algorithm,
            gates);

        WriteEvidence(rts);
        WriteEvidence(open);

        Assert.That(open.ProcessedTileCounts, Is.EqualTo(rts.ProcessedTileCounts),
            "RTS/open per-generation rebuilt tile counts must match; unequal dirty work invalidates the locality gate. " +
            $"rts=[{string.Join(',', rts.ProcessedTileCounts)}] open=[{string.Join(',', open.ProcessedTileCounts)}]");
        Assert.That(open.TotalProcessedTiles, Is.EqualTo(rts.TotalProcessedTiles),
            $"RTS/open dirtyTilesProcessed must match (rts={rts.TotalProcessedTiles}, open={open.TotalProcessedTiles}).");
        Assert.That(open.LastRebuiltTileCount, Is.EqualTo(rts.LastRebuiltTileCount),
            $"RTS/open LastRebuiltTileCount must match (rts={rts.LastRebuiltTileCount}, open={open.LastRebuiltTileCount}).");

        Assert.That(open.DirtyPublishMsP95, Is.LessThanOrEqualTo(
            (rts.DirtyPublishMsP95 * gates.DirtyPublishP95RatioMax) + gates.DirtyPublishP95FixedNoiseMs),
            $"Open-world dirty publish p95 {open.DirtyPublishMsP95:F3}ms exceeds RTS {rts.DirtyPublishMsP95:F3}ms * {gates.DirtyPublishP95RatioMax} + {gates.DirtyPublishP95FixedNoiseMs}ms.");

        Assert.That(open.MatchedInteriorProcessedTiles, Is.EqualTo(rts.MatchedInteriorProcessedTiles),
            "RTS/open matched-interior processed tile counts must match; unequal halo-safe work invalidates the throughput ratio. " +
            $"rts={rts.MatchedInteriorProcessedTiles} open={open.MatchedInteriorProcessedTiles}.");
        Assert.That(open.MatchedInteriorTriangleReferenceCount, Is.EqualTo(rts.MatchedInteriorTriangleReferenceCount),
            "RTS/open matched-interior triangle-reference counts must match (equivalent halo input). " +
            $"rts={rts.MatchedInteriorTriangleReferenceCount} open={open.MatchedInteriorTriangleReferenceCount}.");
        Assert.That(open.BootstrapTriangleReferenceCount, Is.GreaterThan(rts.BootstrapTriangleReferenceCount),
            "Full 64-tile bootstrap triangle refs must be unequal across scenes (open interior halo > RTS world-boundary truncation); " +
            "bootstrap remains diagnostic only, not the cross-scene throughput ratio gate. " +
            $"rts={rts.BootstrapTriangleReferenceCount} open={open.BootstrapTriangleReferenceCount}.");

        double throughputFloor = rts.MatchedInteriorTilesPerSecond * gates.SteadyStateThroughputRatioMin;
        Assert.That(open.MatchedInteriorTilesPerSecond, Is.GreaterThanOrEqualTo(throughputFloor),
            $"Open-world matched-interior full-bake throughput {open.MatchedInteriorTilesPerSecond:F2} t/s is below RTS " +
            $"{rts.MatchedInteriorTilesPerSecond:F2} * {gates.SteadyStateThroughputRatioMin}.");

        Assert.That(rts.CollectMsP95, Is.LessThanOrEqualTo(gates.CollectP95BudgetMs));
        Assert.That(open.CollectMsP95, Is.LessThanOrEqualTo(gates.CollectP95BudgetMs));
        Assert.That(rts.CommitMsP95, Is.LessThanOrEqualTo(gates.CommitP95BudgetMs));
        Assert.That(open.CommitMsP95, Is.LessThanOrEqualTo(gates.CommitP95BudgetMs));

        Assert.That(rts.PeakResidentTileCount, Is.LessThanOrEqualTo(gates.PeakResidentTileCountMax));
        Assert.That(open.PeakResidentTileCount, Is.LessThanOrEqualTo(gates.PeakResidentTileCountMax));
        Assert.That(rts.PeakResidentTileCount, Is.LessThan(4096));
        Assert.That(open.PeakResidentTileCount, Is.LessThan(4096));
        Assert.That(rts.WorldTileCount, Is.EqualTo(64));
        Assert.That(open.WorldTileCount, Is.EqualTo(4096));
        Assert.That(rts.LastDirtyVisitedCandidateCount, Is.LessThanOrEqualTo(gates.MaxDirtyVisitedCandidateCount));
        Assert.That(open.LastDirtyVisitedCandidateCount, Is.LessThanOrEqualTo(gates.MaxDirtyVisitedCandidateCount));
        Assert.That(open.LastDirtyVisitedCandidateCount, Is.LessThan(4096),
            "Open-world dirty work must not scan all 4096 world tiles.");

        if (algorithm == NavBakeAlgorithmKind.LayeredSpan)
        {
            Assert.That(rts.PeakWorkerScratchBytes, Is.LessThanOrEqualTo(gates.PeakWorkerScratchBytesMax));
            Assert.That(open.PeakWorkerScratchBytes, Is.LessThanOrEqualTo(gates.PeakWorkerScratchBytesMax));
            Assert.That(rts.PeakWorkerScratchBytes, Is.GreaterThan(0L));
            Assert.That(open.PeakWorkerScratchBytes, Is.GreaterThan(0L));
        }
        else
        {
            Assert.That(rts.PeakWorkerScratchBytes, Is.EqualTo(RuntimeNavMeshTelemetryService.AdapterScratchNotOwned));
            Assert.That(open.PeakWorkerScratchBytes, Is.EqualTo(RuntimeNavMeshTelemetryService.AdapterScratchNotOwned));
        }

        Assert.That(rts.PeakResidentBytes, Is.LessThanOrEqualTo(gates.PeakResidentBytesMax));
        Assert.That(open.PeakResidentBytes, Is.LessThanOrEqualTo(gates.PeakResidentBytesMax));

        AssertInvariantCounters(rts);
        AssertInvariantCounters(open);

        if (algorithm == NavBakeAlgorithmKind.LayeredSpan)
        {
            Assert.That(rts.AllocatedBytesP95, Is.EqualTo(gates.LayeredSpanSteadyStateAllocBytesMax));
            Assert.That(open.AllocatedBytesP95, Is.EqualTo(gates.LayeredSpanSteadyStateAllocBytesMax));
        }

        AssertWorkerDeterminism(rtsConfig, algorithm, gates);
        AssertWorkerDeterminism(openConfig, algorithm, gates);
    }

    [Test]
    public void BenchmarkConfig_MissingSection_FailsFastNamingOwner()
    {
        string repoRoot = DynamicNavBakeShowcaseAcceptanceHarness.FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw.Remove("benchmark");
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("benchmark"));
        Assert.That(ex.Message, Does.Contain("DynamicNavBakeShowcaseConfig"));
    }

    private static ScenePerfEvidence MeasureSceneProduction(
        string sceneModId,
        string mapId,
        DynamicNavBakeShowcaseConfig showcase,
        NavBakeAlgorithmKind algorithm,
        DynamicNavBakeShowcaseBenchmarkConfig gates)
    {
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            sceneModId,
            registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(engine, mapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

        int expectedResidentTiles = checked(showcase.ResidentWidthChunks * showcase.ResidentHeightChunks);

        // Force a complete resident-window rebuild for the measured algorithm (same 8x8 window on both scenes).
        // Epoch starts only after spawn/nav are quiescent so structural first-capture cannot append
        // extra generations onto this full-resident switch measurement.
        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);
        Assert.That(actions.TrySwitchAlgorithm(engine, algorithm, out string switchError), Is.True, switchError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(algorithm));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedResidentTiles));

        RuntimeNavMeshTelemetryService telemetry = DynamicNavBakeShowcaseAcceptanceHarness.RequireTelemetry(engine);
        RuntimeNavMeshTelemetrySnapshot bootstrapSnap = telemetry.CaptureSnapshot();
        Assert.That(telemetry.HasOpenGeneration, Is.False, "Bootstrap epoch must not leave a partial open generation.");
        Assert.That(bootstrapSnap.SampleCount, Is.EqualTo(1),
            "Bootstrap epoch must publish exactly one complete generation (algorithm switch over the resident window).");
        Assert.That(bootstrapSnap.TotalProcessedTiles, Is.EqualTo(expectedResidentTiles),
            "Bootstrap throughput must measure exactly one full resident-window generation; extra generations make RTS/open work unequal.");
        Assert.That(bootstrapSnap.LastRebuiltTileCount, Is.EqualTo(expectedResidentTiles));
        Assert.That(bootstrapSnap.FailedBatchCount, Is.EqualTo(0));
        Assert.That(bootstrapSnap.MixedGenerationCount, Is.EqualTo(0));
        Assert.That(bootstrapSnap.DroppedSampleCount, Is.EqualTo(0));
        Assert.That(bootstrapSnap.FallbackCount, Is.EqualTo(0));
        Assert.That(bootstrapSnap.DroppedDirtyCommandCount, Is.EqualTo(0));
        Assert.That(bootstrapSnap.CapacityGrowthCount, Is.EqualTo(0));
        if (algorithm == NavBakeAlgorithmKind.LayeredSpan)
        {
            Assert.That(
                bootstrapSnap.AllocatedBytesP95,
                Is.EqualTo(gates.LayeredSpanSteadyStateAllocBytesMax));
        }
        double bootstrapThroughput = bootstrapSnap.SteadyStateTilesPerSecond;
        if (!(bootstrapThroughput > 0d))
        {
            throw new InvalidOperationException(
                $"Bootstrap throughput unavailable for {algorithm}/{showcase.SceneKind}: " +
                $"tiles={bootstrapSnap.TotalProcessedTiles}, tps={bootstrapThroughput}.");
        }

        engine.TryGetService(CoreServiceKeys.NavTriangleSurface, out NavTriangleSurfaceTileIndex? surface);
        engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig? bakeConfig);
        if (surface == null || bakeConfig?.RuntimeIncremental == null)
        {
            throw new InvalidOperationException("Production perf evidence requires triangle surface and NavMeshBakeConfig.");
        }

        Assert.That(surface.Grid.TileCount, Is.EqualTo(checked(showcase.WidthChunks * showcase.HeightChunks)));
        Assert.That(surface.Grid.TileWidthCm, Is.EqualTo(showcase.ChunkSizeCm));
        Assert.That(surface.Grid.TileHeightCm, Is.EqualTo(showcase.ChunkSizeCm));

        ResolveCommittedResidentTileTargets(showcase, queue, surface.Grid, out NavBakeTileCoord[] residentTargets);
        long bootstrapTriangleRefs = CountTriangleReferences(surface, residentTargets);
        SelectHaloSafeInteriorTargets(
            showcase,
            surface.Grid,
            residentTargets,
            out NavBakeTileCoord[] matchedInteriorTargets,
            out int insetTilesX,
            out int insetTilesZ);
        long matchedInteriorTriangleRefs = CountTriangleReferences(surface, matchedInteriorTargets);
        Assert.That(matchedInteriorTargets.Length, Is.GreaterThan(0),
            $"Matched halo-safe interior must be non-empty (insetX={insetTilesX}, insetZ={insetTilesZ}, " +
            $"resident={showcase.ResidentWidthChunks}x{showcase.ResidentHeightChunks}, " +
            $"halo={surface.Grid.HaloPaddingCm}cm, tile={surface.Grid.TileWidthCm}x{surface.Grid.TileHeightCm}cm).");

        // Matched halo-safe interior full-bake: same triangle/halo input on both scenes.
        // Full 64-tile bootstrap above stays as residency/atomicity evidence only.
        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);
        for (int i = 0; i < matchedInteriorTargets.Length; i++)
        {
            Assert.That(
                queue.EnqueueDirtyTile(matchedInteriorTargets[i]),
                Is.True,
                $"Matched-interior enqueue must succeed for tile " +
                $"({matchedInteriorTargets[i].ChunkX},{matchedInteriorTargets[i].ChunkY}).");
        }

        actions.DrainUntilIdle(engine, maxTicks: 8192);
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));
        Assert.That(queue.PendingTileCount, Is.EqualTo(0));
        Assert.That(queue.SealedRemainingCount, Is.EqualTo(0));

        RuntimeNavMeshTelemetrySnapshot matchedSnap = telemetry.CaptureSnapshot();
        Assert.That(telemetry.HasOpenGeneration, Is.False, "Matched-interior epoch must not leave a partial open generation.");
        Assert.That(matchedSnap.SampleCount, Is.EqualTo(1),
            "Matched-interior epoch must publish exactly one complete generation.");
        Assert.That(matchedSnap.LastCommitted, Is.True);
        Assert.That(matchedSnap.LastAborted, Is.False);
        Assert.That(matchedSnap.TotalProcessedTiles, Is.EqualTo(matchedInteriorTargets.Length),
            "Matched-interior throughput must measure exactly the selected halo-safe target count.");
        Assert.That(matchedSnap.LastRebuiltTileCount, Is.EqualTo(matchedInteriorTargets.Length));
        Assert.That(matchedSnap.FailedBatchCount, Is.EqualTo(0));
        Assert.That(matchedSnap.MixedGenerationCount, Is.EqualTo(0));
        Assert.That(matchedSnap.DroppedSampleCount, Is.EqualTo(0));
        Assert.That(matchedSnap.FallbackCount, Is.EqualTo(0));
        Assert.That(matchedSnap.DroppedDirtyCommandCount, Is.EqualTo(0));
        Assert.That(matchedSnap.CapacityGrowthCount, Is.EqualTo(0));
        if (algorithm == NavBakeAlgorithmKind.LayeredSpan)
        {
            Assert.That(
                matchedSnap.AllocatedBytesP95,
                Is.EqualTo(gates.LayeredSpanSteadyStateAllocBytesMax));
        }
        double matchedInteriorThroughput = matchedSnap.SteadyStateTilesPerSecond;
        if (!(matchedInteriorThroughput > 0d))
        {
            throw new InvalidOperationException(
                $"Matched-interior throughput unavailable for {algorithm}/{showcase.SceneKind}: " +
                $"tiles={matchedSnap.TotalProcessedTiles}, tps={matchedInteriorThroughput}.");
        }

        // Warmup complete generations (build/demolish of the authored wall).
        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);
        for (int i = 0; i < gates.WarmupSampleCount; i++)
        {
            RunAuthoredWallGeneration(actions, engine, build: (i % 2) == 0);
        }

        // Equivalent heap precondition before the measured epoch: CDT/Recast allocate on the bake
        // path (Recast hundreds of MB). A mid-window GC would spike one sample and falsify the p95
        // locality gate. This does not exclude measured work, retry, reorder, change gates, or
        // compact the LOH — it only runs normal full collections so RTS vs open start measured
        // dirty samples with equivalent generational heap pressure. LayeredSpan stays 0B after
        // this. Recast remains an allocating baseline (not 0Alloc).
        StabilizeManagedHeapPrecondition();

        // Measured epoch: sampleWindow complete generations; percentiles from this epoch only.
        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);
        for (int i = 0; i < gates.SampleWindowCount; i++)
        {
            RunAuthoredWallGeneration(actions, engine, build: (i % 2) == 0);
        }

        RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
        Assert.That(snap.SampleCount, Is.EqualTo(gates.SampleWindowCount),
            "Sample window must contain exactly one telemetry sample per completed generation.");
        Assert.That(telemetry.HasOpenGeneration, Is.False, "Measured epoch must not leave a partial open generation.");

        var checksums = new ulong[snap.SampleCount];
        telemetry.CopyGenerationChecksumSequence(checksums);
        var processedTileCounts = new int[snap.SampleCount];
        telemetry.CopyProcessedTileCountSequence(processedTileCounts);

        return new ScenePerfEvidence(
            showcase.MapId,
            showcase.SceneKind,
            NavBakeNames.FormatAlgorithm(algorithm),
            DynamicNavBakeShowcaseEvidenceCapture.ComputeSceneHash(showcase),
            DynamicNavBakeShowcaseEvidenceCapture.ComputeConfigHash(bakeConfig),
            DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(surface),
            workerCount: 1,
            fixedStepBudgetMs: gates.FixedStepBudgetMs,
            tileBudget: bakeConfig.RuntimeIncremental.TileBudgetPerFixedTick,
            snap,
            checksums,
            processedTileCounts,
            queue.LastDirtyVisitedCandidateCount,
            queue.DroppedDirtyCommandCount,
            queue.CapacityGrowthCount,
            bootstrapThroughput,
            bootstrapTotalProcessedTiles: bootstrapSnap.TotalProcessedTiles,
            bootstrapSampleCount: bootstrapSnap.SampleCount,
            bootstrapTriangleReferenceCount: bootstrapTriangleRefs,
            matchedInteriorTilesPerSecond: matchedInteriorThroughput,
            matchedInteriorSampleCount: matchedSnap.SampleCount,
            matchedInteriorProcessedTiles: matchedSnap.TotalProcessedTiles,
            matchedInteriorTriangleReferenceCount: matchedInteriorTriangleRefs,
            worldTileCount: surface.Grid.TileCount);
    }

    private static void RunAuthoredWallGeneration(
        DynamicNavBakeShowcaseActions actions,
        GameEngine engine,
        bool build)
    {
        RuntimeNavMeshTelemetryService telemetry = DynamicNavBakeShowcaseAcceptanceHarness.RequireTelemetry(engine);
        int samplesBefore = telemetry.SampleCount;
        ulong generationBefore = telemetry.CaptureSnapshot().LastGeneration;

        if (build)
        {
            Assert.That(actions.TryBuildWall(engine, out string buildError), Is.True, buildError);
            Assert.That(actions.WallDeployedCount, Is.EqualTo(actions.ActiveConfig.Gate.SegmentCount),
                "Performance evidence must use the authored multi-segment wall, not a one-circle substitute.");
        }
        else
        {
            Assert.That(actions.TryDemolishWall(engine, out string demolishError), Is.True, demolishError);
        }

        actions.DrainUntilIdle(engine, maxTicks: 4096);
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));
        Assert.That(queue.PendingTileCount, Is.EqualTo(0));
        Assert.That(queue.SealedRemainingCount, Is.EqualTo(0));
        Assert.That(telemetry.HasOpenGeneration, Is.False);

        RuntimeNavMeshTelemetrySnapshot snap = telemetry.CaptureSnapshot();
        Assert.That(snap.SampleCount, Is.EqualTo(samplesBefore + 1),
            "Each authored build/demolish must commit exactly one complete generation sample.");
        Assert.That(snap.LastGeneration, Is.GreaterThan(generationBefore));
        Assert.That(snap.LastCommitted, Is.True);
        Assert.That(snap.LastAborted, Is.False);
        Assert.That(snap.FailedBatchCount, Is.EqualTo(0));
    }

    private static void AssertWorkerDeterminism(
        DynamicNavBakeShowcaseConfig config,
        NavBakeAlgorithmKind algorithm,
        DynamicNavBakeShowcaseBenchmarkConfig gates)
    {
        // Runtime queue stays single-worker. Determinism across worker counts uses formal offline NavBakeService.
        using GameEngine engine = DynamicNavBakeShowcaseAcceptanceHarness.CreateEngine(
            config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld
                ? "NavBakeOpenWorld64x64ShowcaseMod"
                : "NavBakeDynamicRtsShowcaseMod",
            registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(config.MapId);
        DynamicNavBakeShowcaseActions actions = DynamicNavBakeShowcaseAcceptanceHarness.WaitForActions(engine, config.MapId);
        DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);
        DynamicNavBakeShowcaseAcceptanceHarness.AssertAlgorithmSwitch(engine, actions, algorithm);

        NavTriangleSurfaceTileIndex surface = engine.GetService(CoreServiceKeys.NavTriangleSurface)
            ?? throw new InvalidOperationException("Determinism requires NavTriangleSurface.");
        NavMeshBakeConfig bakeConfig = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
            ?? throw new InvalidOperationException("Determinism requires NavMeshBakeConfig.");
        AgentProfileRegistry profiles = engine.GetService(CoreServiceKeys.AgentProfiles)
            ?? throw new InvalidOperationException("Determinism requires AgentProfiles.");
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException("Determinism requires RuntimeNavMeshRebuildQueue.");

        ResolveCommittedResidentTileTargets(config, queue, surface.Grid, out NavBakeTileCoord[] targets);
        int expectedResidentTiles = checked(config.ResidentWidthChunks * config.ResidentHeightChunks);
        Assert.That(targets.Length, Is.EqualTo(expectedResidentTiles),
            "Determinism targets must be the full committed resident window, not a wall-local substitute.");

        int expectedResultsPerTarget = checked(bakeConfig.Layers.Count * bakeConfig.Profiles.Count);
        Assert.That(expectedResultsPerTarget, Is.GreaterThan(0),
            "Determinism requires at least one authored layer and profile.");

        OrderedBakeEvidence baseline = default;
        bool hasBaseline = false;
        for (int i = 0; i < gates.DeterminismWorkerCounts.Length; i++)
        {
            int workers = gates.DeterminismWorkerCounts[i];
            OrderedBakeEvidence evidence = BakeOfflineChecksums(
                config,
                bakeConfig,
                surface,
                profiles,
                algorithm,
                workers,
                targets);
            AssertOrderedResidentWindowEvidence(
                evidence,
                targets,
                expectedResidentTiles,
                expectedResultsPerTarget,
                bakeConfig,
                config);

            if (!hasBaseline)
            {
                baseline = evidence;
                hasBaseline = true;
            }
            else
            {
                Assert.That(evidence.ChunkXs, Is.EqualTo(baseline.ChunkXs),
                    $"Worker count {workers} ordered ChunkX diverged.");
                Assert.That(evidence.ChunkYs, Is.EqualTo(baseline.ChunkYs),
                    $"Worker count {workers} ordered ChunkY diverged.");
                Assert.That(evidence.Layers, Is.EqualTo(baseline.Layers),
                    $"Worker count {workers} ordered layer diverged.");
                Assert.That(evidence.ProfileIds, Is.EqualTo(baseline.ProfileIds),
                    $"Worker count {workers} ordered ProfileId diverged.");
                Assert.That(evidence.TileChecksums, Is.EqualTo(baseline.TileChecksums),
                    $"Worker count {workers} ordered tile checksums diverged.");
                Assert.That(evidence.GenerationChecksum, Is.EqualTo(baseline.GenerationChecksum),
                    $"Worker count {workers} generation checksum diverged.");
            }

            TestContext.WriteLine(
                $"determinism algorithm={NavBakeNames.FormatAlgorithm(algorithm)} " +
                $"scene={config.SceneKind} workers={workers} " +
                $"generationChecksum={evidence.GenerationChecksum:X16} tiles={evidence.TileChecksums.Length}");
        }
    }

    private static OrderedBakeEvidence BakeOfflineChecksums(
        DynamicNavBakeShowcaseConfig showcase,
        NavMeshBakeConfig authoredBakeConfig,
        NavTriangleSurfaceTileIndex surface,
        AgentProfileRegistry profiles,
        NavBakeAlgorithmKind algorithm,
        int workers,
        NavBakeTileCoord[] targets)
    {
        NavMeshBakeConfig bakeConfig = CloneBakeConfigForOffline(authoredBakeConfig, algorithm);
        INavBakeAlgorithm adapter = CreateAlgorithm(algorithm, bakeConfig, workers);
        var service = new NavBakeService(adapter);
        var context = new NavBakeContext
        {
            MapId = showcase.MapId + "_determinism",
            SourceUri = "Core:Maps/" + showcase.MapId + "_determinism.tris",
            TriangleSurface = surface,
            Obstacles = new NavObstacleSet(),
            Config = bakeConfig,
            AgentProfiles = profiles,
            Targets = targets,
            BuildConfig = new NavBuildConfig(1f, 0.6f, 1),
            TileVersion = 1,
            Mode = NavBakeMode.Offline,
            Algorithm = algorithm,
            Execution = new NavBakeExecutionOptions
            {
                Parallel = workers > 1,
                MaxDegreeOfParallelism = workers
            }
        };

        NavBakeResult result = service.Bake(context);
        Assert.That(result.FailureCount, Is.EqualTo(0), result.Entries.Count > 0 ? result.Entries[0].Artifact.Message : "offline bake failed");

        var ordered = new NavBakeResultEntry[result.Entries.Count];
        for (int i = 0; i < result.Entries.Count; i++)
        {
            ordered[i] = result.Entries[i];
            Assert.That(ordered[i].Success, Is.True, ordered[i].Artifact.Message);
        }

        Array.Sort(ordered, NavBakeCanonicalHash.CompareOfflineResultEntries);
        NavBakeCanonicalHash.ComputeOfflineResultChecksums(
            result,
            out ulong[] tileChecksums,
            out ulong generationChecksum);

        var chunkXs = new int[ordered.Length];
        var chunkYs = new int[ordered.Length];
        var layers = new int[ordered.Length];
        var profileIds = new string[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            chunkXs[i] = ordered[i].Target.ChunkX;
            chunkYs[i] = ordered[i].Target.ChunkY;
            layers[i] = ordered[i].Layer;
            profileIds[i] = ordered[i].ProfileId;
            Assert.That(tileChecksums[i], Is.EqualTo(ordered[i].Tile.Checksum),
                $"Canonical ordered checksum[{i}] must match sorted entry tile checksum.");
        }

        return new OrderedBakeEvidence(chunkXs, chunkYs, layers, profileIds, tileChecksums, generationChecksum);
    }

    private static void ResolveCommittedResidentTileTargets(
        DynamicNavBakeShowcaseConfig showcase,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        NavTriangleSurfaceTileGrid grid,
        out NavBakeTileCoord[] targets)
    {
        int expectedCount = checked(showcase.ResidentWidthChunks * showcase.ResidentHeightChunks);
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedCount),
            "Committed resident window must equal authored residentWidthChunks*residentHeightChunks.");

        targets = new NavBakeTileCoord[expectedCount];
        int copied = queue.CopyCommittedResidentWindow(targets);
        Assert.That(copied, Is.EqualTo(expectedCount));
        AssertDistinctTileCoords(targets, "committed resident-window targets");

        ResolveAuthoredResidentOrigin(showcase, out int originChunkX, out int originChunkZ);
        if (originChunkX < 0 || originChunkZ < 0 ||
            originChunkX + showcase.ResidentWidthChunks > grid.TileCountX ||
            originChunkZ + showcase.ResidentHeightChunks > grid.TileCountZ)
        {
            throw new InvalidOperationException(
                $"Authored resident window origin ({originChunkX},{originChunkZ}) size " +
                $"{showcase.ResidentWidthChunks}x{showcase.ResidentHeightChunks} exceeds grid " +
                $"{grid.TileCountX}x{grid.TileCountZ} for map '{showcase.MapId}'.");
        }

        AssertResidentWindowBounds(
            targets,
            originChunkX,
            originChunkZ,
            showcase.ResidentWidthChunks,
            showcase.ResidentHeightChunks,
            "committed resident-window targets");
    }

    private static void ResolveAuthoredResidentOrigin(
        DynamicNavBakeShowcaseConfig showcase,
        out int originChunkX,
        out int originChunkZ)
    {
        if (showcase.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            DynamicNavBakeShowcaseOpenWorldConfig openWorld = showcase.OpenWorld
                ?? throw new InvalidOperationException(
                    "Open-world determinism requires DynamicNavBakeShowcaseConfig.openWorld.");
            if ((uint)openWorld.InitialHotspotIndex >= (uint)openWorld.Hotspots.Length)
            {
                throw new InvalidOperationException(
                    $"Open-world initialHotspotIndex {openWorld.InitialHotspotIndex} is out of range " +
                    $"for {openWorld.Hotspots.Length} hotspots.");
            }

            DynamicNavBakeShowcaseHotspotConfig hotspot = openWorld.Hotspots[openWorld.InitialHotspotIndex];
            originChunkX = hotspot.ResidentOriginChunkX;
            originChunkZ = hotspot.ResidentOriginChunkZ;
            return;
        }

        // RTS: resident window is the full authored board (resident dims must equal world dims).
        if (showcase.ResidentWidthChunks != showcase.WidthChunks ||
            showcase.ResidentHeightChunks != showcase.HeightChunks)
        {
            throw new InvalidOperationException(
                $"RTS determinism requires resident window to equal world chunks " +
                $"({showcase.WidthChunks}x{showcase.HeightChunks}); got " +
                $"{showcase.ResidentWidthChunks}x{showcase.ResidentHeightChunks}.");
        }

        originChunkX = 0;
        originChunkZ = 0;
    }

    private static void SelectHaloSafeInteriorTargets(
        DynamicNavBakeShowcaseConfig showcase,
        NavTriangleSurfaceTileGrid grid,
        NavBakeTileCoord[] residentTargets,
        out NavBakeTileCoord[] matchedInteriorTargets,
        out int insetTilesX,
        out int insetTilesZ)
    {
        insetTilesX = CeilDivPositive(grid.HaloPaddingCm, grid.TileWidthCm);
        insetTilesZ = CeilDivPositive(grid.HaloPaddingCm, grid.TileHeightCm);
        if (checked(insetTilesX * 2) >= showcase.ResidentWidthChunks ||
            checked(insetTilesZ * 2) >= showcase.ResidentHeightChunks)
        {
            throw new InvalidOperationException(
                $"Halo-safe interior is empty for resident {showcase.ResidentWidthChunks}x{showcase.ResidentHeightChunks} " +
                $"with inset ({insetTilesX},{insetTilesZ}) from halo={grid.HaloPaddingCm}cm / " +
                $"tile={grid.TileWidthCm}x{grid.TileHeightCm}cm.");
        }

        ResolveAuthoredResidentOrigin(showcase, out int originChunkX, out int originChunkZ);
        int localMaxExclusiveX = checked(showcase.ResidentWidthChunks - insetTilesX);
        int localMaxExclusiveZ = checked(showcase.ResidentHeightChunks - insetTilesZ);
        int expectedCount = checked(
            (localMaxExclusiveX - insetTilesX) * (localMaxExclusiveZ - insetTilesZ));
        matchedInteriorTargets = new NavBakeTileCoord[expectedCount];
        int written = 0;
        for (int i = 0; i < residentTargets.Length; i++)
        {
            int localX = checked(residentTargets[i].ChunkX - originChunkX);
            int localZ = checked(residentTargets[i].ChunkY - originChunkZ);
            if (localX < insetTilesX || localX >= localMaxExclusiveX ||
                localZ < insetTilesZ || localZ >= localMaxExclusiveZ)
            {
                continue;
            }

            matchedInteriorTargets[written++] = residentTargets[i];
        }

        Assert.That(written, Is.EqualTo(expectedCount),
            "Halo-safe interior selection must cover every local [inset, width-inset) x [inset, height-inset) tile.");
        AssertDistinctTileCoords(matchedInteriorTargets, "matched halo-safe interior targets");
    }

    private static long CountTriangleReferences(
        NavTriangleSurfaceTileIndex surface,
        NavBakeTileCoord[] targets)
    {
        long total = 0L;
        for (int i = 0; i < targets.Length; i++)
        {
            total = checked(total + surface.GetTriangleIndices(targets[i]).Length);
        }

        return total;
    }

    private static int CeilDivPositive(int numerator, int denominator)
    {
        if (numerator < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "Numerator must be nonnegative.");
        }

        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "Denominator must be positive.");
        }

        if (numerator == 0)
        {
            return 0;
        }

        return checked((numerator + denominator - 1) / denominator);
    }

    private static void AssertOrderedResidentWindowEvidence(
        OrderedBakeEvidence evidence,
        NavBakeTileCoord[] targets,
        int expectedResidentTiles,
        int expectedResultsPerTarget,
        NavMeshBakeConfig bakeConfig,
        DynamicNavBakeShowcaseConfig showcase)
    {
        int expectedResultCount = checked(expectedResidentTiles * expectedResultsPerTarget);
        Assert.That(evidence.TileChecksums.Length, Is.EqualTo(expectedResultCount),
            "Successful ordered results must equal residentTargets * layers * profiles.");
        Assert.That(evidence.ChunkXs.Length, Is.EqualTo(expectedResultCount));
        Assert.That(evidence.ChunkYs.Length, Is.EqualTo(expectedResultCount));
        Assert.That(evidence.Layers.Length, Is.EqualTo(expectedResultCount));
        Assert.That(evidence.ProfileIds.Length, Is.EqualTo(expectedResultCount));

        var targetCounts = new Dictionary<(int X, int Y), int>(expectedResidentTiles);
        var fullKeys = new HashSet<(int X, int Y, int Layer, string Profile)>(expectedResultCount);
        for (int i = 0; i < expectedResultCount; i++)
        {
            var key = (evidence.ChunkXs[i], evidence.ChunkYs[i], evidence.Layers[i], evidence.ProfileIds[i]);
            Assert.That(fullKeys.Add(key), Is.True,
                $"Ordered result[{i}] has duplicate full key (ChunkX,ChunkY,Layer,ProfileId)={key}.");

            var targetKey = (evidence.ChunkXs[i], evidence.ChunkYs[i]);
            targetCounts.TryGetValue(targetKey, out int count);
            targetCounts[targetKey] = count + 1;

            if (i > 0)
            {
                Assert.That(
                    NavBakeCanonicalHash.CompareOfflineResultEntries(
                        CreateOrderKeyEntry(
                            evidence.ChunkXs[i - 1],
                            evidence.ChunkYs[i - 1],
                            evidence.Layers[i - 1],
                            evidence.ProfileIds[i - 1]),
                        CreateOrderKeyEntry(
                            evidence.ChunkXs[i],
                            evidence.ChunkYs[i],
                            evidence.Layers[i],
                            evidence.ProfileIds[i])),
                    Is.LessThan(0),
                    $"Ordered evidence must be strictly canonical at index {i}.");
            }
        }

        Assert.That(targetCounts.Count, Is.EqualTo(expectedResidentTiles),
            "Ordered results must cover exactly the committed resident tile target count.");
        for (int t = 0; t < targets.Length; t++)
        {
            var targetKey = (targets[t].ChunkX, targets[t].ChunkY);
            Assert.That(targetCounts.ContainsKey(targetKey), Is.True,
                $"Committed resident target ({targets[t].ChunkX},{targets[t].ChunkY}) missing from ordered results.");
            Assert.That(targetCounts[targetKey], Is.EqualTo(expectedResultsPerTarget),
                $"Target ({targets[t].ChunkX},{targets[t].ChunkY}) must appear exactly layers*profiles times.");
        }

        for (int i = 0; i < expectedResultCount; i++)
        {
            bool layerFound = false;
            for (int li = 0; li < bakeConfig.Layers.Count; li++)
            {
                if (bakeConfig.Layers[li].Layer == evidence.Layers[i])
                {
                    layerFound = true;
                    break;
                }
            }

            Assert.That(layerFound, Is.True, $"Ordered layer {evidence.Layers[i]} is not an authored bake layer.");

            bool profileFound = false;
            for (int pi = 0; pi < bakeConfig.Profiles.Count; pi++)
            {
                if (string.Equals(bakeConfig.Profiles[pi].Id, evidence.ProfileIds[i], StringComparison.Ordinal))
                {
                    profileFound = true;
                    break;
                }
            }

            Assert.That(profileFound, Is.True, $"Ordered ProfileId '{evidence.ProfileIds[i]}' is not an authored bake profile.");
        }

        ResolveAuthoredResidentOrigin(showcase, out int originChunkX, out int originChunkZ);
        var distinctCoords = new NavBakeTileCoord[expectedResidentTiles];
        int distinctIndex = 0;
        foreach (var kv in targetCounts)
        {
            distinctCoords[distinctIndex++] = new NavBakeTileCoord(kv.Key.X, kv.Key.Y);
        }

        AssertResidentWindowBounds(
            distinctCoords,
            originChunkX,
            originChunkZ,
            showcase.ResidentWidthChunks,
            showcase.ResidentHeightChunks,
            "ordered bake result tile targets");
    }

    private static NavBakeResultEntry CreateOrderKeyEntry(int chunkX, int chunkY, int layer, string profileId)
        => new(
            new NavBakeTileCoord(chunkX, chunkY),
            profileId,
            layer,
            success: true,
            tile: null!,
            detourTileBytes: Array.Empty<byte>(),
            artifact: default);

    private static void AssertDistinctTileCoords(NavBakeTileCoord[] coords, string owner)
    {
        var seen = new HashSet<(int X, int Y)>(coords.Length);
        for (int i = 0; i < coords.Length; i++)
        {
            if (!seen.Add((coords[i].ChunkX, coords[i].ChunkY)))
            {
                throw new InvalidOperationException(
                    $"{owner} contains duplicate tile ({coords[i].ChunkX},{coords[i].ChunkY}).");
            }
        }

        Assert.That(seen.Count, Is.EqualTo(coords.Length), $"{owner} must contain only distinct tiles.");
    }

    private static void AssertResidentWindowBounds(
        NavBakeTileCoord[] coords,
        int originChunkX,
        int originChunkZ,
        int widthChunks,
        int heightChunks,
        string owner)
    {
        int maxX = checked(originChunkX + widthChunks - 1);
        int maxZ = checked(originChunkZ + heightChunks - 1);
        int minSeenX = int.MaxValue;
        int minSeenZ = int.MaxValue;
        int maxSeenX = int.MinValue;
        int maxSeenZ = int.MinValue;
        for (int i = 0; i < coords.Length; i++)
        {
            NavBakeTileCoord c = coords[i];
            Assert.That(c.ChunkX, Is.GreaterThanOrEqualTo(originChunkX), $"{owner} ChunkX below resident origin.");
            Assert.That(c.ChunkY, Is.GreaterThanOrEqualTo(originChunkZ), $"{owner} ChunkY below resident origin.");
            Assert.That(c.ChunkX, Is.LessThanOrEqualTo(maxX), $"{owner} ChunkX above resident max.");
            Assert.That(c.ChunkY, Is.LessThanOrEqualTo(maxZ), $"{owner} ChunkY above resident max.");
            minSeenX = Math.Min(minSeenX, c.ChunkX);
            minSeenZ = Math.Min(minSeenZ, c.ChunkY);
            maxSeenX = Math.Max(maxSeenX, c.ChunkX);
            maxSeenZ = Math.Max(maxSeenZ, c.ChunkY);
        }

        Assert.That(minSeenX, Is.EqualTo(originChunkX), $"{owner} must cover resident min X.");
        Assert.That(minSeenZ, Is.EqualTo(originChunkZ), $"{owner} must cover resident min Y.");
        Assert.That(maxSeenX, Is.EqualTo(maxX), $"{owner} must cover resident max X.");
        Assert.That(maxSeenZ, Is.EqualTo(maxZ), $"{owner} must cover resident max Y.");
        Assert.That(coords.Length, Is.EqualTo(checked(widthChunks * heightChunks)),
            $"{owner} count must equal resident width*height.");
    }

    /// <summary>
    /// Runs normal full garbage collections so consecutive scenes start with comparable
    /// generational heap pressure. Does not compact the large object heap.
    /// </summary>
    private static void StabilizeManagedHeapPrecondition()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static INavBakeAlgorithm CreateAlgorithm(NavBakeAlgorithmKind algorithm, NavMeshBakeConfig config, int workers)
    {
        return algorithm switch
        {
            NavBakeAlgorithmKind.LayeredSpan => new LayeredSpanNavBakeAlgorithm(
                new LayeredSpanScratchPool(CloneLayered(config.LayeredSpan, Math.Max(config.LayeredSpan.ScratchSlotCount, workers)))),
            NavBakeAlgorithmKind.Cdt => new CdtNavBakeAlgorithm(),
            NavBakeAlgorithmKind.Recast => new RecastNavBakeAlgorithm(),
            _ => throw new InvalidOperationException($"Unsupported algorithm '{algorithm}'.")
        };
    }

    private static NavMeshBakeConfig CloneBakeConfigForOffline(NavMeshBakeConfig source, NavBakeAlgorithmKind algorithm)
    {
        return new NavMeshBakeConfig
        {
            Mode = NavBakeNames.ModeOffline,
            Algorithm = NavBakeNames.FormatAlgorithm(algorithm),
            Profiles = source.Profiles,
            Layers = source.Layers,
            Areas = source.Areas,
            RuntimeIncremental = source.RuntimeIncremental,
            LayeredSpan = source.LayeredSpan,
            TriangleSurface = source.TriangleSurface,
            Recast = source.Recast
        };
    }

    private static NavLayeredSpanConfig CloneLayered(NavLayeredSpanConfig source, int scratchSlotCount)
    {
        return new NavLayeredSpanConfig
        {
            ScratchSlotCount = scratchSlotCount,
            RasterCellSizeCm = source.RasterCellSizeCm,
            RasterHaloCells = source.RasterHaloCells,
            SameSurfaceToleranceCm = source.SameSurfaceToleranceCm,
            MaxSimplificationErrorCm = source.MaxSimplificationErrorCm,
            HeightRounding = source.HeightRounding,
            MaxLawsonFlipCount = source.MaxLawsonFlipCount,
            ColumnCapacity = source.ColumnCapacity,
            SpanCapacity = source.SpanCapacity,
            ClassifiedSpanCapacity = source.ClassifiedSpanCapacity,
            WalkableSpanCapacity = source.WalkableSpanCapacity,
            LinkCapacity = source.LinkCapacity,
            SheetCapacity = source.SheetCapacity,
            PortalIntervalCapacity = source.PortalIntervalCapacity,
            RegionCapacity = source.RegionCapacity,
            ChartCapacity = source.ChartCapacity,
            RingCapacity = source.RingCapacity,
            ContourVertexCapacity = source.ContourVertexCapacity,
            ContourEdgeCapacity = source.ContourEdgeCapacity,
            SeamCapacity = source.SeamCapacity,
            CanonicalLinkCapacity = source.CanonicalLinkCapacity,
            SplitPointCapacity = source.SplitPointCapacity,
            TriangulationVertexCapacity = source.TriangulationVertexCapacity,
            TriangulationTriangleCapacity = source.TriangulationTriangleCapacity,
            ConstrainedEdgeCapacity = source.ConstrainedEdgeCapacity,
            BorderPortalCapacity = source.BorderPortalCapacity,
            PolygonVertexCapacity = source.PolygonVertexCapacity,
            AdjacencyEdgeCapacity = source.AdjacencyEdgeCapacity,
            BridgeCandidateCapacity = source.BridgeCandidateCapacity,
            RingWorkCapacity = source.RingWorkCapacity,
            TemporaryConstraintFlagCapacity = source.TemporaryConstraintFlagCapacity
        };
    }

    private static void AssertBenchmarkParity(
        DynamicNavBakeShowcaseBenchmarkConfig left,
        DynamicNavBakeShowcaseBenchmarkConfig right)
    {
        Assert.That(left.SampleWindowCount, Is.EqualTo(right.SampleWindowCount));
        Assert.That(left.WarmupSampleCount, Is.EqualTo(right.WarmupSampleCount));
        Assert.That(left.DirtyPublishP95RatioMax, Is.EqualTo(right.DirtyPublishP95RatioMax));
        Assert.That(left.DirtyPublishP95FixedNoiseMs, Is.EqualTo(right.DirtyPublishP95FixedNoiseMs));
        Assert.That(left.SteadyStateThroughputRatioMin, Is.EqualTo(right.SteadyStateThroughputRatioMin));
        Assert.That(left.CollectP95BudgetMs, Is.EqualTo(right.CollectP95BudgetMs));
        Assert.That(left.CommitP95BudgetMs, Is.EqualTo(right.CommitP95BudgetMs));
        Assert.That(left.FixedStepBudgetMs, Is.EqualTo(right.FixedStepBudgetMs));
        Assert.That(
            left.DirtyComparisonBoundaryMarginChunks,
            Is.EqualTo(right.DirtyComparisonBoundaryMarginChunks));
    }

    private static void AssertInvariantCounters(ScenePerfEvidence evidence)
    {
        Assert.That(evidence.DroppedDirtyCommandCount, Is.EqualTo(0));
        Assert.That(evidence.CapacityGrowthCount, Is.EqualTo(0));
        Assert.That(evidence.FallbackCount, Is.EqualTo(0));
        Assert.That(evidence.FailedBatchCount, Is.EqualTo(0));
        Assert.That(evidence.MixedGenerationCount, Is.EqualTo(0));
        Assert.That(evidence.DroppedSampleCount, Is.EqualTo(0));
    }

    private static void WriteEvidence(ScenePerfEvidence evidence)
    {
        TestContext.WriteLine(
            $"PERF algorithm={evidence.Algorithm} scene={evidence.SceneKind} map={evidence.MapId} " +
            $"sceneHash={evidence.SceneHash} configHash={evidence.ConfigHash} inputHash={evidence.InputHash} " +
            $"workers={evidence.WorkerCount} budgetMs={evidence.FixedStepBudgetMs} tileBudget={evidence.TileBudget} " +
            $"worldTiles={evidence.WorldTileCount} " +
            $"fullBootstrapThroughput={evidence.BootstrapTilesPerSecond:F2}/s " +
            $"fullBootstrapSamples={evidence.BootstrapSampleCount} fullBootstrapTiles={evidence.BootstrapTotalProcessedTiles} " +
            $"fullBootstrapTriangleRefs={evidence.BootstrapTriangleReferenceCount} " +
            $"matchedInteriorThroughput={evidence.MatchedInteriorTilesPerSecond:F2}/s " +
            $"matchedInteriorSamples={evidence.MatchedInteriorSampleCount} " +
            $"matchedInteriorTiles={evidence.MatchedInteriorProcessedTiles} " +
            $"matchedInteriorTriangleRefs={evidence.MatchedInteriorTriangleReferenceCount} " +
            $"dirtyTilesProcessed={evidence.TotalProcessedTiles} dirtyThroughput={evidence.SteadyStateTilesPerSecond:F2}/s " +
            $"perGenTiles=[{string.Join(',', evidence.ProcessedTileCounts)}] lastRebuiltTiles={evidence.LastRebuiltTileCount} " +
            $"collectP50={evidence.CollectMsP50:F3} collectP95={evidence.CollectMsP95:F3} " +
            $"bakeP50={evidence.BakeMsP50:F3} bakeP95={evidence.BakeMsP95:F3} " +
            $"commitP50={evidence.CommitMsP50:F3} commitP95={evidence.CommitMsP95:F3} " +
            $"dirtyP50={evidence.DirtyPublishMsP50:F3} dirtyP95={evidence.DirtyPublishMsP95:F3} " +
            $"allocP95={evidence.AllocatedBytesP95} peakScratch={evidence.PeakWorkerScratchBytes} " +
            $"peakResidentBytes={evidence.PeakResidentBytes} peakResidentTiles={evidence.PeakResidentTileCount} " +
            $"dirtyCandidates={evidence.LastDirtyVisitedCandidateCount} " +
            $"droppedDirty={evidence.DroppedDirtyCommandCount} capacityGrowth={evidence.CapacityGrowthCount} " +
            $"fallback={evidence.FallbackCount} failedBatch={evidence.FailedBatchCount} " +
            $"mixedGeneration={evidence.MixedGenerationCount} droppedSamples={evidence.DroppedSampleCount} " +
            $"genChecksums=[{string.Join(',', Array.ConvertAll(evidence.GenerationChecksums, c => c.ToString("X16")))}]");
    }

    private static DynamicNavBakeShowcaseConfig LoadShowcaseConfig(string mapId)
    {
        string relative = mapId switch
        {
            DynamicNavBakeShowcaseIds.RtsMapId =>
                "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json",
            DynamicNavBakeShowcaseIds.OpenWorldMapId =>
                "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json",
            _ => throw new InvalidOperationException($"Unknown showcase map '{mapId}'.")
        };
        return DynamicNavBakeShowcaseConfig.Load(ReadJsonObject(Path.Combine(DynamicNavBakeShowcaseAcceptanceHarness.FindRepoRoot(), relative)));
    }

    private static JsonObject ReadJsonObject(string path)
    {
        string json = File.ReadAllText(path);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException($"Expected JSON object at '{path}'.");
    }

    private readonly struct OrderedBakeEvidence
    {
        public OrderedBakeEvidence(
            int[] chunkXs,
            int[] chunkYs,
            int[] layers,
            string[] profileIds,
            ulong[] tileChecksums,
            ulong generationChecksum)
        {
            ChunkXs = chunkXs;
            ChunkYs = chunkYs;
            Layers = layers;
            ProfileIds = profileIds;
            TileChecksums = tileChecksums;
            GenerationChecksum = generationChecksum;
        }

        public int[] ChunkXs { get; }
        public int[] ChunkYs { get; }
        public int[] Layers { get; }
        public string[] ProfileIds { get; }
        public ulong[] TileChecksums { get; }
        public ulong GenerationChecksum { get; }
    }

    private readonly struct ScenePerfEvidence
    {
        public ScenePerfEvidence(
            string mapId,
            string sceneKind,
            string algorithm,
            string sceneHash,
            string configHash,
            string inputHash,
            int workerCount,
            double fixedStepBudgetMs,
            int tileBudget,
            RuntimeNavMeshTelemetrySnapshot snap,
            ulong[] generationChecksums,
            int[] processedTileCounts,
            int lastDirtyVisitedCandidateCount,
            int droppedDirtyCommandCount,
            int capacityGrowthCount,
            double bootstrapTilesPerSecond,
            long bootstrapTotalProcessedTiles,
            int bootstrapSampleCount,
            long bootstrapTriangleReferenceCount,
            double matchedInteriorTilesPerSecond,
            int matchedInteriorSampleCount,
            long matchedInteriorProcessedTiles,
            long matchedInteriorTriangleReferenceCount,
            int worldTileCount)
        {
            MapId = mapId;
            SceneKind = sceneKind;
            Algorithm = algorithm;
            SceneHash = sceneHash;
            ConfigHash = configHash;
            InputHash = inputHash;
            WorkerCount = workerCount;
            FixedStepBudgetMs = fixedStepBudgetMs;
            TileBudget = tileBudget;
            CollectMsP50 = snap.CollectMsP50;
            CollectMsP95 = snap.CollectMsP95;
            BakeMsP50 = snap.BakeMsP50;
            BakeMsP95 = snap.BakeMsP95;
            CommitMsP50 = snap.CommitMsP50;
            CommitMsP95 = snap.CommitMsP95;
            DirtyPublishMsP50 = snap.DirtyPublishMsP50;
            DirtyPublishMsP95 = snap.DirtyPublishMsP95;
            AllocatedBytesP95 = snap.AllocatedBytesP95;
            SteadyStateTilesPerSecond = snap.SteadyStateTilesPerSecond;
            TotalProcessedTiles = snap.TotalProcessedTiles;
            PeakWorkerScratchBytes = snap.PeakWorkerScratchBytes;
            PeakResidentBytes = snap.PeakResidentBytes;
            PeakResidentTileCount = snap.PeakResidentTileCount;
            SampleCount = snap.SampleCount;
            GenerationChecksums = generationChecksums;
            ProcessedTileCounts = processedTileCounts;
            LastRebuiltTileCount = snap.LastRebuiltTileCount;
            LastDirtyVisitedCandidateCount = lastDirtyVisitedCandidateCount;
            DroppedDirtyCommandCount = droppedDirtyCommandCount;
            CapacityGrowthCount = capacityGrowthCount;
            FallbackCount = snap.FallbackCount;
            FailedBatchCount = snap.FailedBatchCount;
            MixedGenerationCount = snap.MixedGenerationCount;
            DroppedSampleCount = snap.DroppedSampleCount;
            BootstrapTilesPerSecond = bootstrapTilesPerSecond;
            BootstrapTotalProcessedTiles = bootstrapTotalProcessedTiles;
            BootstrapSampleCount = bootstrapSampleCount;
            BootstrapTriangleReferenceCount = bootstrapTriangleReferenceCount;
            MatchedInteriorTilesPerSecond = matchedInteriorTilesPerSecond;
            MatchedInteriorSampleCount = matchedInteriorSampleCount;
            MatchedInteriorProcessedTiles = matchedInteriorProcessedTiles;
            MatchedInteriorTriangleReferenceCount = matchedInteriorTriangleReferenceCount;
            WorldTileCount = worldTileCount;
        }

        public string MapId { get; }
        public string SceneKind { get; }
        public string Algorithm { get; }
        public string SceneHash { get; }
        public string ConfigHash { get; }
        public string InputHash { get; }
        public int WorkerCount { get; }
        public double FixedStepBudgetMs { get; }
        public int TileBudget { get; }
        public double CollectMsP50 { get; }
        public double CollectMsP95 { get; }
        public double BakeMsP50 { get; }
        public double BakeMsP95 { get; }
        public double CommitMsP50 { get; }
        public double CommitMsP95 { get; }
        public double DirtyPublishMsP50 { get; }
        public double DirtyPublishMsP95 { get; }
        public long AllocatedBytesP95 { get; }
        public double SteadyStateTilesPerSecond { get; }
        public long TotalProcessedTiles { get; }
        public long PeakWorkerScratchBytes { get; }
        public long PeakResidentBytes { get; }
        public int PeakResidentTileCount { get; }
        public int SampleCount { get; }
        public ulong[] GenerationChecksums { get; }
        public int[] ProcessedTileCounts { get; }
        public int LastRebuiltTileCount { get; }
        public int LastDirtyVisitedCandidateCount { get; }
        public int DroppedDirtyCommandCount { get; }
        public int CapacityGrowthCount { get; }
        public int FallbackCount { get; }
        public int FailedBatchCount { get; }
        public int MixedGenerationCount { get; }
        public int DroppedSampleCount { get; }
        public double BootstrapTilesPerSecond { get; }
        public long BootstrapTotalProcessedTiles { get; }
        public int BootstrapSampleCount { get; }
        public long BootstrapTriangleReferenceCount { get; }
        public double MatchedInteriorTilesPerSecond { get; }
        public int MatchedInteriorSampleCount { get; }
        public long MatchedInteriorProcessedTiles { get; }
        public long MatchedInteriorTriangleReferenceCount { get; }
        public int WorldTileCount { get; }
    }
}
