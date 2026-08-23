using System;
using System.Diagnostics;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Scripting;
using Ludots.Core.Navigation.Terrain;

namespace NavDomainShowcaseMod.Runtime;

internal sealed class NavBakeSessionOutcome
{
    public int OkCount { get; init; }

    public int EmptyCount { get; init; }

    public int FailCount { get; init; }

    public int TriangleCount { get; init; }

    public double ElapsedMs { get; init; }

    public string EstimateHash { get; init; } = string.Empty;

    public string FailMessage { get; init; } = string.Empty;
}

internal sealed class NavBakeSession
{
    private const bool IncludeNeighborTiles = true;
    private const int NavTileLayer = 0;

    private readonly LogicTerrainDocument _document;
    private readonly NavBakeService _bakeService;
    private readonly NavTileStore _tileStore;
    private uint _tileVersion;

    public NavBakeSession(LogicTerrainDocument document)
    {
        _document = document;
        _bakeService = new NavBakeService(new CdtNavBakeAlgorithm());
        _tileStore = new NavTileStore(_ => throw new NotSupportedException(
            "NavDomainShowcaseMod preview tiles are in-memory only; baked tiles are published through NavTileStore.Replace."));
    }

    public NavTileStore TileStore => _tileStore;

    public NavBakeEstimateReport? LastEstimate { get; private set; }

    public NavBakeSessionOutcome? LastOutcome { get; private set; }

    public NavBakeEstimateReport EstimateDirty(GameEngine engine)
    {
        NavBakeContext context = BuildContext(engine, dirtyOnly: true);
        LastEstimate = NavBakeEstimator.Estimate(context);
        return LastEstimate;
    }

    public NavBakeSessionOutcome BakeDirty(GameEngine engine)
    {
        return Bake(engine, dirtyOnly: true);
    }

    public NavBakeSessionOutcome BakeAll(GameEngine engine)
    {
        return Bake(engine, dirtyOnly: false);
    }

    public bool TryGetTile(int chunkX, int chunkY, out NavTile tile)
    {
        return _tileStore.TryGet(new NavTileId(chunkX, chunkY, NavTileLayer), out tile!);
    }

    private NavBakeSessionOutcome Bake(GameEngine engine, bool dirtyOnly)
    {
        var stopwatch = Stopwatch.StartNew();
        NavBakeContext context = BuildContext(engine, dirtyOnly);
        if (context.Targets.Count == 0)
        {
            return new NavBakeSessionOutcome
            {
                FailMessage = "No dirty terrain chunks to bake."
            };
        }

        NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);
        NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved: false, acceptedEstimateHash: null);
        _tileVersion = _tileVersion == uint.MaxValue ? 1u : _tileVersion + 1u;

        NavBakeResult result = _bakeService.Bake(context);
        int ok = 0;
        int empty = 0;
        int fail = 0;
        int triangles = 0;
        string failMessage = string.Empty;
        for (int i = 0; i < result.Entries.Count; i++)
        {
            NavBakeResultEntry entry = result.Entries[i];
            if (entry.Success)
            {
                ok++;
                triangles += entry.Tile.TriangleCount;
                _tileStore.Replace(entry.Tile);
                continue;
            }

            if (entry.Artifact.ErrorCode == NavBakeErrorCode.NoWalkableDomain)
            {
                empty++;
                _tileStore.Unload(new NavTileId(entry.Target.ChunkX, entry.Target.ChunkY, NavTileLayer));
                continue;
            }

            fail++;
            if (string.IsNullOrEmpty(failMessage))
            {
                failMessage = $"tile {entry.Target.ChunkX},{entry.Target.ChunkY}: {entry.Artifact.ErrorCode} {entry.Artifact.Message}";
            }
        }

        if (fail == 0)
        {
            _document.ClearDirty();
        }

        stopwatch.Stop();
        LastEstimate = estimate;
        LastOutcome = new NavBakeSessionOutcome
        {
            OkCount = ok,
            EmptyCount = empty,
            FailCount = fail,
            TriangleCount = triangles,
            ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
            EstimateHash = estimate.EstimateHash,
            FailMessage = failMessage
        };
        return LastOutcome;
    }

    private NavBakeContext BuildContext(GameEngine engine, bool dirtyOnly)
    {
        if (engine.GetService(CoreServiceKeys.NavMeshBakeConfig) is not NavMeshBakeConfig config)
        {
            throw new InvalidOperationException("NavDomainShowcaseMod requires the engine NavMeshBakeConfig service.");
        }

        if (engine.GetService(CoreServiceKeys.AgentProfiles) is not Ludots.Core.Navigation.AgentProfiles.AgentProfileRegistry agentProfiles)
        {
            throw new InvalidOperationException("NavDomainShowcaseMod requires the engine AgentProfiles service.");
        }

        var runtimeIncremental = config.RuntimeIncremental;
        var buildConfig = new NavBuildConfig(
            runtimeIncremental.HeightScaleMeters,
            runtimeIncremental.MinWalkableUpDot,
            runtimeIncremental.CliffHeightThreshold);

        string dirtyJson = _document.BuildDirtyJson();
        IReadOnlyList<NavBakeTileCoord> targets = NavBakeTileSelection.Resolve(
            _document.Field,
            dirtyJson,
            IncludeNeighborTiles,
            dirtyOnly);

        return new NavBakeContext
        {
            MapId = NavDomainShowcaseIds.MapId,
            ModId = "NavDomainShowcaseMod",
            SourceUri = NavDomainShowcaseIds.SourceUri,
            Terrain = _document.Field,
            Obstacles = new NavObstacleSet(),
            Config = config,
            AgentProfiles = agentProfiles,
            Targets = targets,
            BuildConfig = buildConfig,
            TileVersion = _tileVersion + 1u,
            Mode = NavBakeMode.Offline,
            Algorithm = NavBakeAlgorithmKind.Cdt,
            Execution = new NavBakeExecutionOptions
            {
                Parallel = false,
                MaxDegreeOfParallelism = 1
            }
        };
    }
}
