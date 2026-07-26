using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Runtime;

public sealed class DynamicNavBakeShowcaseEvidence
{
    public string MapId { get; init; } = string.Empty;
    public string SceneKind { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string SceneHash { get; init; } = string.Empty;
    public string ConfigHash { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public int TriangleSnapshotCount { get; init; }
    public int LastNavBootstrapUriResolveCount { get; init; }
    public int ResidentWindowCount { get; init; }
    public int CommittedResidentWindowCount { get; init; }
    public int PendingTileCount { get; init; }
    public int LastDirtyVisitedCandidateCount { get; init; }
    public int WorkerCount { get; init; }
    public double FixedStepBudgetMs { get; init; }
    public int TileBudgetPerFixedTick { get; init; }
    public int SampleWindowCount { get; init; }
    public int WarmupSampleCount { get; init; }
    public ulong LastGeneration { get; init; }
    public ulong LastGenerationChecksum { get; init; }
    public int LastPublishedCount { get; init; }
    public int LastRebuiltTileCount { get; init; }
    public double LastDurationMs { get; init; }
    public double LastCollectMs { get; init; }
    public double LastBakeMs { get; init; }
    public double LastCommitMs { get; init; }
    public long LastAllocatedBytes { get; init; }
    public double CollectMsP50 { get; init; }
    public double CollectMsP95 { get; init; }
    public double BakeMsP50 { get; init; }
    public double BakeMsP95 { get; init; }
    public double CommitMsP50 { get; init; }
    public double CommitMsP95 { get; init; }
    public double DirtyPublishMsP50 { get; init; }
    public double DirtyPublishMsP95 { get; init; }
    public double DurationMsP50 { get; init; }
    public double DurationMsP95 { get; init; }
    public long AllocatedBytesP50 { get; init; }
    public long AllocatedBytesP95 { get; init; }
    public double SteadyStateTilesPerSecond { get; init; }
    public long TotalProcessedTiles { get; init; }
    public long PeakWorkerScratchBytes { get; init; }
    public long PeakResidentBytes { get; init; }
    public int PeakResidentTileCount { get; init; }
    public ulong[] GenerationChecksumSequence { get; init; } = Array.Empty<ulong>();
    public int DroppedSampleCount { get; init; }
    public int FailedBatchCount { get; init; }
    public int DroppedDirtyCommandCount { get; init; }
    public int CapacityGrowthCount { get; init; }
    public int FallbackCount { get; init; }
    public int MixedGenerationCount { get; init; }
    public string PathStatus { get; init; } = string.Empty;
    public string PathOrchestrationState { get; init; } = string.Empty;
    public int PathPointCount { get; init; }
    public int CoarseCorridorNodeCount { get; init; }
    public int WallDeployedCount { get; init; }
    public bool SquadDeployed { get; init; }

    /// <summary>
    /// Count of bound squad entities that currently have an active MassNavigation RouteState.
    /// May be zero before OrderQueue / route sync has processed a move; never manufactured.
    /// </summary>
    public int FormalRouteAgentCount { get; init; }

    /// <summary>
    /// Agreed <see cref="PathDomain"/> across all aggregated formal routes.
    /// <see cref="PathDomain.None"/> when <see cref="FormalRouteAgentCount"/> is zero.
    /// Domain disagreement among found squad routes is an explicit failure at capture time.
    /// </summary>
    public PathDomain FormalRouteDomain { get; init; }

    /// <summary>
    /// Minimum WaypointCount (RouteState.PointCount) among aggregated formal routes.
    /// Zero when no formal routes were found.
    /// </summary>
    public int FormalRouteMinWaypointCount { get; init; }

    public ulong FormalRouteGeometrySignature { get; init; }

    /// <summary>
    /// Deterministic player route/path signature over showcase path points + formal route evidence.
    /// Used by auto-capture gates and UAT to prove initial/dynamic/final routes are not stale.
    /// </summary>
    public ulong PlayerRouteSignature { get; init; }
}

/// <summary>
/// Allocation-free formal player-route observation for host-frame readiness / screenshot gates.
/// Owned by the DynamicNavBake showcase; aggregates preallocated squad entities through the
/// existing MassNavigation route sink evidence API and reuses <see cref="DynamicNavBakeShowcaseEvidenceCapture.ComputePlayerRouteSignature"/>.
/// Full <see cref="DynamicNavBakeShowcaseEvidence"/> capture remains the cold-path reporting surface.
/// </summary>
public readonly struct DynamicNavBakeShowcaseFormalPlayerRouteSnapshot
{
    public DynamicNavBakeShowcaseFormalPlayerRouteSnapshot(
        int formalReadyAgentCount,
        PathDomain agreedPathDomain,
        int minWaypointCount,
        ulong formalRouteGeometrySignature,
        ulong playerRouteSignature,
        ulong committedGeneration)
    {
        FormalReadyAgentCount = formalReadyAgentCount;
        AgreedPathDomain = agreedPathDomain;
        MinWaypointCount = minWaypointCount;
        FormalRouteGeometrySignature = formalRouteGeometrySignature;
        PlayerRouteSignature = playerRouteSignature;
        CommittedGeneration = committedGeneration;
    }

    public int FormalReadyAgentCount { get; }
    public PathDomain AgreedPathDomain { get; }
    public int MinWaypointCount { get; }
    public ulong FormalRouteGeometrySignature { get; }
    public ulong PlayerRouteSignature { get; }
    public ulong CommittedGeneration { get; }
}

/// <summary>
/// Allocation-free read-only observation of authored squad members against Goal + formation slots.
/// Used by arrival-mode final capture; never treats vanished orders alone as arrival.
/// </summary>
public readonly struct DynamicNavBakeShowcaseSquadArrivalSnapshot
{
    public DynamicNavBakeShowcaseSquadArrivalSnapshot(
        int squadCount,
        int idleInToleranceCount,
        int activeMoveOrderCount,
        int outsideToleranceWithoutMoveCount,
        int firstOutsideSlotIndex,
        int firstOutsideXCm,
        int firstOutsideZCm,
        int firstExpectedXCm,
        int firstExpectedZCm,
        long farthestDistanceSquaredCm,
        int farthestSlotIndex,
        int farthestXCm,
        int farthestZCm,
        int farthestExpectedXCm,
        int farthestExpectedZCm)
    {
        SquadCount = squadCount;
        IdleInToleranceCount = idleInToleranceCount;
        ActiveMoveOrderCount = activeMoveOrderCount;
        OutsideToleranceWithoutMoveCount = outsideToleranceWithoutMoveCount;
        FirstOutsideSlotIndex = firstOutsideSlotIndex;
        FirstOutsideXCm = firstOutsideXCm;
        FirstOutsideZCm = firstOutsideZCm;
        FirstExpectedXCm = firstExpectedXCm;
        FirstExpectedZCm = firstExpectedZCm;
        FarthestDistanceSquaredCm = farthestDistanceSquaredCm;
        FarthestSlotIndex = farthestSlotIndex;
        FarthestXCm = farthestXCm;
        FarthestZCm = farthestZCm;
        FarthestExpectedXCm = farthestExpectedXCm;
        FarthestExpectedZCm = farthestExpectedZCm;
    }

    public int SquadCount { get; }
    public int IdleInToleranceCount { get; }
    public int ActiveMoveOrderCount { get; }
    public int OutsideToleranceWithoutMoveCount { get; }
    public int FirstOutsideSlotIndex { get; }
    public int FirstOutsideXCm { get; }
    public int FirstOutsideZCm { get; }
    public int FirstExpectedXCm { get; }
    public int FirstExpectedZCm { get; }
    public long FarthestDistanceSquaredCm { get; }
    public int FarthestSlotIndex { get; }
    public int FarthestXCm { get; }
    public int FarthestZCm { get; }
    public int FarthestExpectedXCm { get; }
    public int FarthestExpectedZCm { get; }

    public bool AllIdleInTolerance =>
        SquadCount > 0 &&
        IdleInToleranceCount == SquadCount &&
        ActiveMoveOrderCount == 0 &&
        OutsideToleranceWithoutMoveCount == 0;
}

public static class DynamicNavBakeShowcaseEvidenceCapture
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static DynamicNavBakeShowcaseEvidence Capture(
        DynamicNavBakeShowcaseConfig config,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        RuntimeNavMeshTelemetryService telemetry,
        NavTriangleSurfaceTileIndex triangleSurface,
        NavMeshBakeConfig bakeConfig,
        NavPathStatus pathStatus,
        int pathPointCount,
        int coarseCorridorNodeCount,
        int wallDeployedCount,
        bool squadDeployed,
        int lastNavBootstrapUriResolveCount,
        DynamicNavBakePathOrchestrationState pathOrchestrationState,
        MassNavigationRouteExecutionSink? routeExecutionSink,
        ReadOnlySpan<Entity> squadEntities,
        int tileBudgetPerFixedTick,
        ReadOnlySpan<int> pathXcm,
        ReadOnlySpan<int> pathZcm)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (queue == null)
        {
            throw new ArgumentNullException(nameof(queue));
        }

        if (telemetry == null)
        {
            throw new ArgumentNullException(nameof(telemetry), "Telemetry is required for formal DynamicNavBake evidence.");
        }

        if (triangleSurface == null)
        {
            throw new ArgumentNullException(nameof(triangleSurface), "Triangle surface is required for formal DynamicNavBake evidence.");
        }

        if (bakeConfig == null)
        {
            throw new ArgumentNullException(nameof(bakeConfig), "NavMeshBakeConfig is required for formal DynamicNavBake evidence.");
        }

        if (config.Benchmark == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBake evidence requires DynamicNavBakeShowcaseConfig.benchmark.");
        }

        if (tileBudgetPerFixedTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileBudgetPerFixedTick));
        }

        RuntimeNavMeshTelemetrySnapshot snapshot = telemetry.CaptureSnapshot();
        if (snapshot.SampleCount <= 0 && snapshot.LastGeneration == 0 && snapshot.LastPublishedCount == 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBake evidence requires at least one telemetry sample or a committed generation; empty telemetry is an explicit failure.");
        }

        var checksums = new ulong[snapshot.SampleCount];
        telemetry.CopyGenerationChecksumSequence(checksums);

        AggregateFormalRoutes(
            routeExecutionSink,
            squadEntities,
            out int formalRouteAgentCount,
            out PathDomain formalRouteDomain,
            out int formalRouteMinWaypointCount,
            out ulong formalRouteGeometrySignature);

        ulong playerRouteSignature = ComputePlayerRouteSignature(
            pathStatus,
            pathXcm,
            pathZcm,
            formalRouteAgentCount,
            formalRouteDomain,
            formalRouteMinWaypointCount,
            formalRouteGeometrySignature);

        // Full evidence remains the cold-path reporting surface (checksum sequence allocates).
        return new DynamicNavBakeShowcaseEvidence
        {
            MapId = config.MapId,
            SceneKind = config.SceneKind,
            Algorithm = NavBakeNames.FormatAlgorithm(queue.CurrentAlgorithm),
            SceneHash = ComputeSceneHash(config),
            ConfigHash = ComputeConfigHash(bakeConfig),
            InputHash = ComputeInputHash(triangleSurface),
            TriangleSnapshotCount = triangleSurface.Surface.TriangleCount,
            LastNavBootstrapUriResolveCount = lastNavBootstrapUriResolveCount,
            ResidentWindowCount = queue.ResidentWindowCount,
            CommittedResidentWindowCount = queue.CommittedResidentWindowCount,
            PendingTileCount = queue.PendingTileCount,
            LastDirtyVisitedCandidateCount = queue.LastDirtyVisitedCandidateCount,
            WorkerCount = queue.WorkerCount,
            FixedStepBudgetMs = config.Benchmark.FixedStepBudgetMs,
            TileBudgetPerFixedTick = tileBudgetPerFixedTick,
            SampleWindowCount = config.Benchmark.SampleWindowCount,
            WarmupSampleCount = config.Benchmark.WarmupSampleCount,
            LastGeneration = snapshot.LastGeneration,
            LastGenerationChecksum = snapshot.LastGenerationChecksum,
            LastPublishedCount = snapshot.LastPublishedCount,
            LastRebuiltTileCount = snapshot.LastRebuiltTileCount,
            LastDurationMs = snapshot.LastDurationMs,
            LastCollectMs = TicksToMs(snapshot.LastCollectTicks, snapshot.StopwatchFrequency),
            LastBakeMs = TicksToMs(snapshot.LastBakeTicks, snapshot.StopwatchFrequency),
            LastCommitMs = TicksToMs(snapshot.LastCommitTicks, snapshot.StopwatchFrequency),
            LastAllocatedBytes = snapshot.LastAllocatedBytes,
            CollectMsP50 = snapshot.CollectMsP50,
            CollectMsP95 = snapshot.CollectMsP95,
            BakeMsP50 = snapshot.BakeMsP50,
            BakeMsP95 = snapshot.BakeMsP95,
            CommitMsP50 = snapshot.CommitMsP50,
            CommitMsP95 = snapshot.CommitMsP95,
            DirtyPublishMsP50 = snapshot.DirtyPublishMsP50,
            DirtyPublishMsP95 = snapshot.DirtyPublishMsP95,
            DurationMsP50 = snapshot.DurationMsP50,
            DurationMsP95 = snapshot.DurationMsP95,
            AllocatedBytesP50 = snapshot.AllocatedBytesP50,
            AllocatedBytesP95 = snapshot.AllocatedBytesP95,
            SteadyStateTilesPerSecond = snapshot.SteadyStateTilesPerSecond,
            TotalProcessedTiles = snapshot.TotalProcessedTiles,
            PeakWorkerScratchBytes = snapshot.PeakWorkerScratchBytes,
            PeakResidentBytes = snapshot.PeakResidentBytes,
            PeakResidentTileCount = snapshot.PeakResidentTileCount,
            GenerationChecksumSequence = checksums,
            DroppedSampleCount = snapshot.DroppedSampleCount,
            FailedBatchCount = snapshot.FailedBatchCount,
            DroppedDirtyCommandCount = snapshot.DroppedDirtyCommandCount,
            CapacityGrowthCount = snapshot.CapacityGrowthCount,
            FallbackCount = snapshot.FallbackCount,
            MixedGenerationCount = snapshot.MixedGenerationCount,
            PathStatus = pathStatus.ToString(),
            PathOrchestrationState = pathOrchestrationState.ToString(),
            PathPointCount = pathPointCount,
            CoarseCorridorNodeCount = coarseCorridorNodeCount,
            WallDeployedCount = wallDeployedCount,
            SquadDeployed = squadDeployed,
            FormalRouteAgentCount = formalRouteAgentCount,
            FormalRouteDomain = formalRouteDomain,
            FormalRouteMinWaypointCount = formalRouteMinWaypointCount,
            FormalRouteGeometrySignature = formalRouteGeometrySignature,
            PlayerRouteSignature = playerRouteSignature,
        };
    }

    /// <summary>
    /// Allocation-free formal player-route snapshot for host-frame readiness polling.
    /// Reuses <see cref="AggregateFormalRoutes"/> and <see cref="ComputePlayerRouteSignature"/>;
    /// never allocates checksum sequences or evidence DTOs.
    /// </summary>
    public static DynamicNavBakeShowcaseFormalPlayerRouteSnapshot CaptureFormalPlayerRoute(
        MassNavigationRouteExecutionSink? routeExecutionSink,
        ReadOnlySpan<Entity> squadEntities,
        NavPathStatus pathStatus,
        ReadOnlySpan<int> pathXcm,
        ReadOnlySpan<int> pathZcm,
        ulong committedGeneration)
    {
        AggregateFormalRoutes(
            routeExecutionSink,
            squadEntities,
            out int formalRouteAgentCount,
            out PathDomain formalRouteDomain,
            out int formalRouteMinWaypointCount,
            out ulong formalRouteGeometrySignature);

        ulong playerRouteSignature = ComputePlayerRouteSignature(
            pathStatus,
            pathXcm,
            pathZcm,
            formalRouteAgentCount,
            formalRouteDomain,
            formalRouteMinWaypointCount,
            formalRouteGeometrySignature);

        return new DynamicNavBakeShowcaseFormalPlayerRouteSnapshot(
            formalRouteAgentCount,
            formalRouteDomain,
            formalRouteMinWaypointCount,
            formalRouteGeometrySignature,
            playerRouteSignature,
            committedGeneration);
    }

    /// <summary>
    /// Deterministic FNV-1a signature over showcase path geometry + formal MassNavigation route evidence.
    /// Owned by showcase evidence (not a Core API): path points come from showcase orchestration,
    /// formal fields from the existing route sink evidence surface.
    /// </summary>
    public static ulong ComputePlayerRouteSignature(
        NavPathStatus pathStatus,
        ReadOnlySpan<int> pathXcm,
        ReadOnlySpan<int> pathZcm,
        int formalRouteAgentCount,
        PathDomain formalRouteDomain,
        int formalRouteMinWaypointCount,
        ulong formalRouteGeometrySignature)
    {
        if (pathXcm.Length != pathZcm.Length)
        {
            throw new InvalidOperationException(
                $"DynamicNavBake route signature requires equal path X/Z lengths; got X={pathXcm.Length}, Z={pathZcm.Length}.");
        }

        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        hash = MixFnv(hash, (ulong)(uint)pathStatus, prime);
        hash = MixFnv(hash, (ulong)(uint)pathXcm.Length, prime);
        for (int i = 0; i < pathXcm.Length; i++)
        {
            hash = MixFnv(hash, unchecked((ulong)(uint)pathXcm[i]), prime);
            hash = MixFnv(hash, unchecked((ulong)(uint)pathZcm[i]), prime);
        }

        hash = MixFnv(hash, (ulong)(uint)formalRouteAgentCount, prime);
        hash = MixFnv(hash, (ulong)(uint)formalRouteDomain, prime);
        hash = MixFnv(hash, (ulong)(uint)formalRouteMinWaypointCount, prime);
        hash = MixFnv(hash, formalRouteGeometrySignature, prime);
        return hash;
    }

    private static ulong MixFnv(ulong current, ulong value, ulong prime)
    {
        current ^= value;
        return unchecked(current * prime);
    }

    /// <summary>
    /// Canonical SHA-256 over the complete showcase config via culture-invariant camelCase JSON.
    /// </summary>
    public static string ComputeSceneHash(DynamicNavBakeShowcaseConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        string json = JsonSerializer.Serialize(config, CanonicalJsonOptions);
        return Sha256Hex(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Canonical SHA-256 over the complete NavMeshBakeConfig via little-endian binary encoding
    /// (no culture-sensitive number formatting).
    /// </summary>
    public static string ComputeConfigHash(NavMeshBakeConfig bakeConfig)
    {
        if (bakeConfig == null)
        {
            throw new ArgumentNullException(nameof(bakeConfig));
        }

        using var ms = new MemoryStream(4096);
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            WriteUtf8(writer, bakeConfig.Mode);
            WriteUtf8(writer, bakeConfig.Algorithm);
            WriteProfiles(writer, bakeConfig.Profiles);
            WriteLayers(writer, bakeConfig.Layers);
            WriteAreas(writer, bakeConfig.Areas);
            WriteRuntime(writer, bakeConfig.RuntimeIncremental);
            WriteLayered(writer, bakeConfig.LayeredSpan);
            WriteTriangle(writer, bakeConfig.TriangleSurface);
            WriteRecast(writer, bakeConfig.Recast);
        }

        return Sha256Hex(ms.ToArray());
    }

    /// <summary>
    /// Canonical SHA-256 over triangle grid metadata and every triangle SoA channel (little-endian).
    /// </summary>
    public static string ComputeInputHash(NavTriangleSurfaceTileIndex triangleSurface)
    {
        if (triangleSurface == null)
        {
            throw new ArgumentNullException(nameof(triangleSurface));
        }

        NavTriangleSurfaceSnapshot surface = triangleSurface.Surface;
        NavTriangleSurfaceTileGrid grid = triangleSurface.Grid;
        using var ms = new MemoryStream(checked(1024 + (surface.VertexCount * 12) + (surface.TriangleCount * 17)));
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(grid.OriginXcm);
            writer.Write(grid.OriginZcm);
            writer.Write(grid.TileWidthCm);
            writer.Write(grid.TileHeightCm);
            writer.Write(grid.TileCountX);
            writer.Write(grid.TileCountZ);
            writer.Write(grid.HaloPaddingCm);
            writer.Write(surface.VertexCount);
            writer.Write(surface.TriangleCount);

            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            for (int i = 0; i < surface.VertexCount; i++)
            {
                writer.Write(vx[i]);
                writer.Write(vy[i]);
                writer.Write(vz[i]);
            }

            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;
            ReadOnlySpan<byte> areas = surface.TriAreaIds;
            ReadOnlySpan<int> stables = surface.TriStableIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = surface.TriFlags;
            for (int i = 0; i < surface.TriangleCount; i++)
            {
                writer.Write(ta[i]);
                writer.Write(tb[i]);
                writer.Write(tc[i]);
                writer.Write(areas[i]);
                writer.Write(stables[i]);
                writer.Write((byte)flags[i]);
            }
        }

        return Sha256Hex(ms.ToArray());
    }

    private static void WriteUtf8(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteProfiles(BinaryWriter writer, System.Collections.Generic.List<NavMeshAgentProfileConfig> profiles)
    {
        writer.Write(profiles?.Count ?? 0);
        if (profiles == null)
        {
            return;
        }

        for (int i = 0; i < profiles.Count; i++)
        {
            NavMeshAgentProfileConfig p = profiles[i];
            WriteUtf8(writer, p.Id);
            writer.Write(p.MaxClimbCm);
            WriteFloatLe(writer, p.MaxSlopeDeg);
        }
    }

    private static void WriteLayers(BinaryWriter writer, System.Collections.Generic.List<NavLayerConfig> layers)
    {
        writer.Write(layers?.Count ?? 0);
        if (layers == null)
        {
            return;
        }

        for (int i = 0; i < layers.Count; i++)
        {
            WriteUtf8(writer, layers[i].Id);
            writer.Write(layers[i].Layer);
        }
    }

    private static void WriteAreas(BinaryWriter writer, System.Collections.Generic.List<NavAreaCostConfig> areas)
    {
        writer.Write(areas?.Count ?? 0);
        if (areas == null)
        {
            return;
        }

        for (int i = 0; i < areas.Count; i++)
        {
            WriteUtf8(writer, areas[i].Id);
            writer.Write(areas[i].AreaId);
            WriteFloatLe(writer, areas[i].Cost);
        }
    }

    private static void WriteRuntime(BinaryWriter writer, NavRuntimeIncrementalConfig runtime)
    {
        if (runtime == null)
        {
            throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental is required for config hash.");
        }

        writer.Write(runtime.TileBudgetPerFixedTick);
        writer.Write(runtime.IncludeNeighborTiles);
        WriteFloatLe(writer, runtime.HeightScaleMeters);
        WriteFloatLe(writer, runtime.MinWalkableUpDot);
        writer.Write(runtime.CliffHeightThreshold);
        writer.Write(runtime.TrackedStructuralEntityCapacity);
        writer.Write(runtime.ObstaclePrimitiveCapacity);
        writer.Write(runtime.PolygonVertexCapacity);
        writer.Write(runtime.DirtyTileCapacity);
        writer.Write(runtime.StagedEntryCapacity);
        writer.Write(runtime.PublishedTileCapacity);
        writer.Write(runtime.StoreGroupCapacity);
        writer.Write(runtime.ResidentTileCapacity);
        writer.Write(runtime.OutputVertexCapacity);
        writer.Write(runtime.OutputTriangleCapacity);
        writer.Write(runtime.OutputPortalCapacity);
        writer.Write(runtime.InitialResidentChunkX);
        writer.Write(runtime.InitialResidentChunkZ);
        writer.Write(runtime.InitialResidentWidthChunks);
        writer.Write(runtime.InitialResidentHeightChunks);
    }

    private static void WriteLayered(BinaryWriter writer, NavLayeredSpanConfig layered)
    {
        if (layered == null)
        {
            throw new InvalidOperationException("NavMeshBakeConfig.layeredSpan is required for config hash.");
        }

        writer.Write(layered.ScratchSlotCount);
        writer.Write(layered.RasterCellSizeCm);
        writer.Write(layered.RasterHaloCells);
        writer.Write(layered.SameSurfaceToleranceCm);
        writer.Write(layered.MaxSimplificationErrorCm);
        WriteUtf8(writer, layered.HeightRounding);
        writer.Write(layered.MaxLawsonFlipCount);
        writer.Write(layered.ColumnCapacity);
        writer.Write(layered.SpanCapacity);
        writer.Write(layered.ClassifiedSpanCapacity);
        writer.Write(layered.WalkableSpanCapacity);
        writer.Write(layered.LinkCapacity);
        writer.Write(layered.SheetCapacity);
        writer.Write(layered.PortalIntervalCapacity);
        writer.Write(layered.RegionCapacity);
        writer.Write(layered.ChartCapacity);
        writer.Write(layered.RingCapacity);
        writer.Write(layered.ContourVertexCapacity);
        writer.Write(layered.ContourEdgeCapacity);
        writer.Write(layered.SeamCapacity);
        writer.Write(layered.CanonicalLinkCapacity);
        writer.Write(layered.SplitPointCapacity);
        writer.Write(layered.TriangulationVertexCapacity);
        writer.Write(layered.TriangulationTriangleCapacity);
        writer.Write(layered.ConstrainedEdgeCapacity);
        writer.Write(layered.BorderPortalCapacity);
        writer.Write(layered.PolygonVertexCapacity);
        writer.Write(layered.AdjacencyEdgeCapacity);
        writer.Write(layered.BridgeCandidateCapacity);
        writer.Write(layered.RingWorkCapacity);
        writer.Write(layered.TemporaryConstraintFlagCapacity);
    }

    private static void WriteTriangle(BinaryWriter writer, NavTriangleSurfaceConfig triangle)
    {
        if (triangle == null)
        {
            throw new InvalidOperationException("NavMeshBakeConfig.triangleSurface is required for config hash.");
        }

        writer.Write(triangle.HaloPaddingCm);
    }

    private static void WriteRecast(BinaryWriter writer, NavRecastConfig recast)
    {
        if (recast == null)
        {
            throw new InvalidOperationException("NavMeshBakeConfig.recast is required for config hash.");
        }

        writer.Write(recast.RasterCellSizeCm);
        writer.Write(recast.RasterCellHeightCm);
    }

    private static void WriteFloatLe(BinaryWriter writer, float value)
    {
        // BinaryWriter emits IEEE-754 little-endian on all supported runtimes.
        writer.Write(value);
    }

    private static string Sha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static double TicksToMs(long ticks, long frequency)
        => frequency <= 0 ? 0d : ticks * 1000d / frequency;

    private static void AggregateFormalRoutes(
        MassNavigationRouteExecutionSink? routeExecutionSink,
        ReadOnlySpan<Entity> squadEntities,
        out int formalRouteAgentCount,
        out PathDomain formalRouteDomain,
        out int formalRouteMinWaypointCount,
        out ulong formalRouteGeometrySignature)
    {
        formalRouteAgentCount = 0;
        formalRouteDomain = PathDomain.None;
        formalRouteMinWaypointCount = 0;
        formalRouteGeometrySignature = 0;

        if (routeExecutionSink == null || squadEntities.Length == 0)
        {
            return;
        }

        PathDomain? agreedDomain = null;
        int minWaypoints = int.MaxValue;
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong geometryHash = offset;
        for (int i = 0; i < squadEntities.Length; i++)
        {
            Entity agent = squadEntities[i];
            if (!routeExecutionSink.TryGetActiveRouteEvidence(agent, out MassNavigationRouteEvidence route))
            {
                continue;
            }

            // PathDomain.None means the sink still owns the agent but PreferMesh has not resolved yet.
            // RouteReady=false means the formal route is not yet player-ready.
            // Counting either as ready falsely passes screenshot / UAT gates.
            if (route.ResolvedDomain == PathDomain.None || !route.RouteReady)
            {
                continue;
            }

            if (agreedDomain == null)
            {
                agreedDomain = route.ResolvedDomain;
            }
            else if (agreedDomain.Value != route.ResolvedDomain)
            {
                throw new InvalidOperationException(
                    $"DynamicNavBake formal route domain disagreement among squad routes: '{agreedDomain.Value}' vs '{route.ResolvedDomain}'.");
            }

            if (route.WaypointCount < minWaypoints)
            {
                minWaypoints = route.WaypointCount;
            }

            geometryHash = MixFnv(geometryHash, route.WaypointGeometrySignature, prime);
            formalRouteAgentCount++;
        }

        if (formalRouteAgentCount <= 0)
        {
            return;
        }

        formalRouteDomain = agreedDomain!.Value;
        formalRouteMinWaypointCount = minWaypoints;
        formalRouteGeometrySignature = geometryHash;
    }
}
