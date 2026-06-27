using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map.Fields;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.NavBake.Recast;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
public sealed class TerrainNavClosedLoopShowcaseAcceptanceTests
{
    private const string ModId = "TerrainNavClosedLoopShowcaseMod";
    private const string MapId = "terrain_nav_closed_loop";
    private const string ProfileId = "Small";
    private const int Layer = 0;
    private const byte BlockedAreaId = 1;
    private const byte RidgeAreaId = 2;
    private const int CellSizeCm = 100;
    private const int ChunkSizeCells = 64;
    private const float AgentRadiusCm = 30f;
    private const float AgentSpeedCmPerSecond = 700f;
    private const float WaypointSpacingCm = 120f;
    private const float WaypointArriveCm = 55f;
    private const float GoalToleranceCm = 130f;
    private const float TickSeconds = 0.05f;

    [Test]
    public void Showcase_RunsDataBakeQueryMovementAndGroundingClosedLoop()
    {
        string repoRoot = CapabilityStandardShowcaseTestHarness.FindRepoRoot();
        ShowcaseAssets assets = LoadShowcaseAssets(repoRoot);
        TerrainFacts facts = AssertTerrainDataProducts(assets);
        NavigationEndpoints endpoints = ResolveEndpoints(facts.BlockedWorldAabb, assets.Terrain);

        NavBakeResult bake = BakeTargetTile(repoRoot, assets.Terrain, endpoints.Start);
        AssertBakeCarriesAreaClassifications(bake);
        RequireCommittedNavTiles(repoRoot, assets.Terrain);

        IReadOnlyList<byte[]> detourPayloads = RequireDetourPayloads(bake);
        NavAreaCostTable areaCosts = BuildAreaCostTable(assets.NavConfig);
        NavPathResult detourPath = DetourNavQueryEngine.FindPathFromDetourTileBytes(
            detourPayloads,
            Layer,
            areaCosts,
            (int)MathF.Round(endpoints.Start.X),
            (int)MathF.Round(endpoints.Start.Y),
            (int)MathF.Round(endpoints.Goal.X),
            (int)MathF.Round(endpoints.Goal.Y),
            maxPortals: 256);

        AssertPathOk(detourPath, "DetourNavQueryEngine");
        AssertPathAvoidsBlockedArea(detourPath, assets.Terrain, facts.BlockedWorldAabb);

        NavPathResult servicePath = QueryPathThroughNavQueryService(bake, areaCosts, endpoints);
        AssertPathOk(servicePath, "NavQueryService");
        AssertPathAvoidsBlockedArea(servicePath, assets.Terrain, facts.BlockedWorldAabb);

        AgentRunResult run = RunAgentAlongPath(
            assets.Heightmap,
            servicePath,
            facts.BlockedWorldAabb,
            endpoints);

        Assert.That(
            run.ReachedGoal,
            Is.True,
            $"agent final distance was {run.FinalDistanceCm:0.###} cm at {run.FinalPositionCm}; " +
            $"waypoint={run.WaypointIndex}/{run.WaypointCount} target={run.TargetWaypointCm}; " +
            $"minBlockedDistance={run.MinDistanceToBlockedAabbCm:0.###} cm");
        Assert.That(run.MinDistanceToBlockedAabbCm, Is.GreaterThan(AgentRadiusCm));
        Assert.That(run.MaxSampledTerrainHeightCm - run.MinSampledTerrainHeightCm, Is.GreaterThan(4f));
        Assert.That(run.GroundingSampleCount, Is.GreaterThan(8));

        AssertMissingSourcesFailFast(assets, bake);
    }

    private static ShowcaseAssets LoadShowcaseAssets(string repoRoot)
    {
        string modRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "terrain_nav_closed_loop",
            "TerrainNavClosedLoopShowcaseMod");
        string terrainRoot = Path.Combine(modRoot, "assets", "terrain");
        string heightmapPath = Path.Combine(terrainRoot, "terrain_nav_closed_loop.vhtm");
        string logicPath = Path.Combine(terrainRoot, "terrain_nav_closed_loop.ltrn");

        VisualHeightmapRuntime heightmap = LoadVisualHeightmap(heightmapPath);
        SparseGridLogicTerrainField terrain = LoadLogicTerrain(logicPath);
        NavMeshBakeConfigContext navContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, ModId);

        return new ShowcaseAssets(modRoot, heightmapPath, logicPath, heightmap, terrain, navContext.Config, navContext.AgentProfiles);
    }

    private static VisualHeightmapRuntime LoadVisualHeightmap(string path)
    {
        RequireFile(path);
        using FileStream stream = File.OpenRead(path);
        return new VisualHeightmapRuntime(VisualHeightmapBinary.Read(stream));
    }

    private static SparseGridLogicTerrainField LoadLogicTerrain(string path)
    {
        RequireFile(path);
        using FileStream stream = File.OpenRead(path);
        LogicTerrainField terrain = LogicTerrainBinary.Read(stream);
        return terrain as SparseGridLogicTerrainField
            ?? throw new InvalidDataException("Showcase LogicTerrain must use sparse grid binary data.");
    }

    private static TerrainFacts AssertTerrainDataProducts(ShowcaseAssets assets)
    {
        FileInfo heightInfo = new(assets.HeightmapPath);
        FileInfo logicInfo = new(assets.LogicTerrainPath);
        Assert.That(heightInfo.Length, Is.GreaterThan(0));
        Assert.That(logicInfo.Length, Is.GreaterThan(0));

        VisualHeightmapAsset heightAsset = assets.Heightmap.Asset;
        Assert.That(VisualHeightmapBinary.TryGetFlatHeightCm(heightAsset, out _), Is.False);
        SampleHeightRange(assets.Heightmap, out float minHeight, out float maxHeight);
        Assert.That(maxHeight - minHeight, Is.GreaterThan(8f));

        using FileStream metadataStream = File.OpenRead(assets.LogicTerrainPath);
        LogicTerrainBinaryMetadata metadata = LogicTerrainBinary.ReadMetadata(metadataStream);
        int totalChunks = ((metadata.WidthCells + metadata.ChunkSizeCells - 1) / metadata.ChunkSizeCells) *
            ((metadata.HeightCells + metadata.ChunkSizeCells - 1) / metadata.ChunkSizeCells);
        Assert.That(metadata.ChunkCount, Is.GreaterThan(0));
        Assert.That(metadata.ChunkCount, Is.LessThan(totalChunks), "LogicTerrain showcase data must be sparse, not dense-equivalent.");

        ScanTerrainClassifications(
            assets.Terrain,
            out bool hasBlocked,
            out bool hasRidge,
            out int minBlockedCol,
            out int maxBlockedCol,
            out int minBlockedRow,
            out int maxBlockedRow);

        Assert.That(hasBlocked, Is.True, "Showcase LogicTerrain must contain areaId=Blocked cells.");
        Assert.That(hasRidge, Is.True, "Showcase LogicTerrain must contain non-default walkable area classification.");

        var blockedAabb = new WorldAabbCm(
            minBlockedCol * assets.Terrain.CellSizeCm,
            minBlockedRow * assets.Terrain.CellSizeCm,
            (maxBlockedCol - minBlockedCol + 1) * assets.Terrain.CellSizeCm,
            (maxBlockedRow - minBlockedRow + 1) * assets.Terrain.CellSizeCm);

        return new TerrainFacts(blockedAabb, minHeight, maxHeight);
    }

    private static NavigationEndpoints ResolveEndpoints(WorldAabbCm blockedAabb, SparseGridLogicTerrainField terrain)
    {
        float centerY = blockedAabb.Top + (blockedAabb.Height * 0.5f);
        float startX = MathF.Max(terrain.CellSizeCm * 4f, blockedAabb.Left - 1700f);
        float goalX = MathF.Min((terrain.WidthCells * terrain.CellSizeCm) - (terrain.CellSizeCm * 4f), blockedAabb.Right + 1700f);
        var start = new Vector2(startX, centerY);
        var goal = new Vector2(goalX, centerY);

        Assert.That(SegmentIntersectsAabb(start, goal, blockedAabb), Is.True, "Start->goal baseline must cross the Blocked area.");
        AssertWalkableCell(terrain, start);
        AssertWalkableCell(terrain, goal);
        return new NavigationEndpoints(start, goal);
    }

    private static NavBakeResult BakeTargetTile(string repoRoot, SparseGridLogicTerrainField terrain, Vector2 start)
    {
        NavMeshBakeConfigContext navContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, ModId);
        int tileWidthCm = terrain.ChunkSizeCells * terrain.CellSizeCm;
        int tileX = Math.Clamp((int)MathF.Floor(start.X / tileWidthCm), 0, terrain.WidthChunks - 1);
        int tileY = Math.Clamp((int)MathF.Floor(start.Y / tileWidthCm), 0, terrain.HeightChunks - 1);
        var context = new NavBakeContext
        {
            MapId = MapId,
            ModId = ModId,
            SourceUri = $"{ModId}:assets/terrain/terrain_nav_closed_loop.ltrn",
            Terrain = terrain,
            Obstacles = new NavObstacleSet(),
            Config = navContext.Config,
            AgentProfiles = navContext.AgentProfiles,
            Targets = new[] { new NavBakeTileCoord(tileX, tileY) },
            BuildConfig = new NavBuildConfig(
                navContext.Config.RuntimeIncremental.HeightScaleMeters,
                navContext.Config.RuntimeIncremental.MinWalkableUpDot,
                navContext.Config.RuntimeIncremental.CliffHeightThreshold),
            TileVersion = NavTileBinary.FormatVersion,
            Mode = navContext.Config.ParsedMode,
            Algorithm = navContext.Config.ParsedAlgorithm,
            Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
        };

        NavBakeResult result = new NavBakeService(new RecastNavBakeAlgorithm(), new CdtNavBakeAlgorithm()).Bake(context);
        Assert.That(result.FailureCount, Is.EqualTo(0), FormatBakeFailures(result));
        Assert.That(result.SuccessCount, Is.GreaterThan(0));
        return result;
    }

    private static void AssertBakeCarriesAreaClassifications(NavBakeResult bake)
    {
        bool anyNonDefault = false;
        bool anyRidge = false;
        for (int i = 0; i < bake.Entries.Count; i++)
        {
            NavBakeResultEntry entry = bake.Entries[i];
            Assert.That(entry.Success, Is.True, entry.Artifact.Message);
            Assert.That(entry.Tile, Is.Not.Null);
            Assert.That(entry.DetourTileBytes, Is.Not.Empty);
            for (int tri = 0; tri < entry.Tile.TriAreaIds.Length; tri++)
            {
                byte areaId = entry.Tile.TriAreaIds[tri];
                anyNonDefault |= areaId != 0;
                anyRidge |= areaId == RidgeAreaId;
                Assert.That(areaId, Is.Not.EqualTo(BlockedAreaId), "Blocked area must be removed from walkable navmesh, not cost-mapped as walkable.");
            }
        }

        Assert.That(anyNonDefault, Is.True, "TriAreaIds must not collapse to all default area.");
        Assert.That(anyRidge, Is.True, "Walkable Ridge area classification must reach baked TriAreaIds.");
    }

    private static NavPathResult QueryPathThroughNavQueryService(NavBakeResult bake, NavAreaCostTable areaCosts, NavigationEndpoints endpoints)
    {
        var store = new NavTileStore(
            id => throw new FileNotFoundException($"Test NavTileStore has no committed fallback loader for tile {id}."),
            KnownTileIds(bake));
        for (int i = 0; i < bake.Entries.Count; i++)
        {
            store.Replace(bake.Entries[i].Tile);
        }

        int tileWidthCm = ChunkSizeCells * CellSizeCm;
        var query = new NavQueryService(store, Layer, areaCosts, tileWidthCm, tileWidthCm);
        return query.TryFindPath(
            (int)MathF.Round(endpoints.Start.X),
            (int)MathF.Round(endpoints.Start.Y),
            (int)MathF.Round(endpoints.Goal.X),
            (int)MathF.Round(endpoints.Goal.Y),
            maxPortals: 256);
    }

    private static AgentRunResult RunAgentAlongPath(
        IVisualHeightmap heightmap,
        NavPathResult path,
        WorldAabbCm blockedAabb,
        NavigationEndpoints endpoints)
    {
        List<Vector2> waypoints = DensifyPath(path, WaypointSpacingCm);
        Assert.That(waypoints.Count, Is.GreaterThan(2));

        using World world = World.Create();
        Vector2 start = endpoints.Start;
        WorldPositionCm startPosition = WorldPositionCm.FromCmFloat(start.X, start.Y);
        Entity agent = world.Create(
            new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register(ProfileId) },
            startPosition,
            new PreviousWorldPositionCm { Value = startPosition.Value },
            VisualTransform.Default,
            new VisualHeightmapSampleState());

        MassNavigationSimulationRuntime runtime = CreateMassNavigationRuntime(world, agent, start, blockedAabb);
        runtime.SyncAgentEntitiesNow(world);

        var globals = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [CoreServiceKeys.VisualHeightmap.Name] = heightmap
        };
        using var heightSync = new TerrainHeightSyncSystem(world, globals);

        int waypointIndex = 1;
        int previousTargetWaypointIndex = -1;
        float minDistanceToBlocked = float.PositiveInfinity;
        float minHeightCm = float.PositiveInfinity;
        float maxHeightCm = float.NegativeInfinity;
        int groundingSamples = 0;
        Vector2 finalPosition = start;

        for (int tick = 0; tick < 2600; tick++)
        {
            Vector2 current = runtime.GetAgentWorldPositionCm(0);
            while (waypointIndex < waypoints.Count - 1 &&
                   Vector2.Distance(current, waypoints[waypointIndex]) <= WaypointArriveCm)
            {
                waypointIndex++;
            }

            Vector2 target = waypoints[waypointIndex];
            bool targetChanged = waypointIndex != previousTargetWaypointIndex;
            _ = runtime.SetAgentNavigationTargetWorldCm(0, target.X, target.Y, WaypointArriveCm, resetRecovery: targetChanged);
            previousTargetWaypointIndex = waypointIndex;
            runtime.StepNavigationForTests(world, TickSeconds, runHardResolve: true);
            runtime.SyncAgentEntitiesNow(world);
            heightSync.Update(TickSeconds);

            ref WorldPositionCm worldPosition = ref world.Get<WorldPositionCm>(agent);
            Vector2 sampledCm = worldPosition.Value.ToVector2();
            finalPosition = sampledCm;
            float distanceToBlocked = DistanceToAabb(sampledCm, blockedAabb);
            minDistanceToBlocked = MathF.Min(minDistanceToBlocked, distanceToBlocked);

            Assert.That(heightmap.TrySampleHeightCm(sampledCm.X, sampledCm.Y, out float expectedHeightCm), Is.True);
            ref VisualTransform visual = ref world.Get<VisualTransform>(agent);
            Assert.That(visual.Position.Y, Is.EqualTo(WorldUnits.CmToM(expectedHeightCm)).Within(0.025f));
            minHeightCm = MathF.Min(minHeightCm, expectedHeightCm);
            maxHeightCm = MathF.Max(maxHeightCm, expectedHeightCm);
            groundingSamples++;

            if (Vector2.Distance(sampledCm, endpoints.Goal) <= GoalToleranceCm)
            {
                return new AgentRunResult(
                    true,
                    Vector2.Distance(sampledCm, endpoints.Goal),
                    sampledCm,
                    waypointIndex,
                    waypoints.Count,
                    target,
                    minDistanceToBlocked,
                    minHeightCm,
                    maxHeightCm,
                    groundingSamples);
            }
        }

        return new AgentRunResult(
            false,
            Vector2.Distance(finalPosition, endpoints.Goal),
            finalPosition,
            waypointIndex,
            waypoints.Count,
            waypoints[Math.Min(waypointIndex, waypoints.Count - 1)],
            minDistanceToBlocked,
            minHeightCm,
            maxHeightCm,
            groundingSamples);
    }

    private static MassNavigationSimulationRuntime CreateMassNavigationRuntime(
        World world,
        Entity agent,
        Vector2 start,
        WorldAabbCm blockedAabb)
    {
        MassNavigationConfig config = CreateMassNavigationConfig();
        var runtime = new MassNavigationSimulationRuntime(config);
        runtime.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_600, 25_600), CellSizeCm));
        runtime.SetWorldOperationsReady(true);

        var layer = new MassNavigationAgentLayer(1u, 1u);
        Vector2 localStart = runtime.ToLocalCm(start);
        runtime.RebuildFromAuthoredAgents(
            world,
            new[] { agent },
            new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: localStart.X,
                    localPositionYCm: localStart.Y,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: AgentRadiusCm,
                    speedCmPerSecond: AgentSpeedCmPerSecond,
                    layer)
            },
            new[] { true });

        runtime.RebuildRuntimeObstacles(CreateRuntimeObstacles(blockedAabb).ToArray());
        Assert.That(runtime.NavigationObstacleCount, Is.GreaterThan(0));
        return runtime;
    }

    private static MassNavigationConfig CreateMassNavigationConfig()
    {
        var solver = new MassNavigationFlowSolverConfig
        {
            FieldWidthCm = 6400,
            FieldHeightCm = 6400,
            FlowCellSizeCm = 100,
            MaxObstacleCount = 128,
            ParallelWorkerCount = 1,
            SeparationHashCellSizeCm = 100,
            SeparationHashMinSearchRadiusCells = 2,
            HardResolveHashCellSizeCm = 50,
            HardResolveHashMinSearchRadiusCells = 1,
            PlayAreaMinXCm = 50f,
            PlayAreaMaxXCm = 6350f,
            PlayAreaMinYCm = 50f,
            PlayAreaMaxYCm = 6350f,
        };

        var config = new MassNavigationConfig
        {
            MapId = MapId,
            Solver = solver,
            World = new MassNavigationWorldConfig
            {
                SolverWindowWidthCm = solver.FieldWidthCm,
                SolverWindowHeightCm = solver.FieldHeightCm,
                StreamingChunkSizeCm = 500,
                StreamingRadiusCm = 1000,
                CommandFocusHoldTicks = 3,
                WorkAreaPaddingCm = 100,
                WorkAreaMaxWidthCm = solver.FieldWidthCm,
                WorkAreaMaxHeightCm = solver.FieldHeightCm,
                ActiveHotZoneId = "closed-loop",
                HotZones = new[]
                {
                    new MassNavigationHotZoneConfig
                    {
                        Id = "closed-loop",
                        Label = "Closed Loop",
                        CenterXCm = 3200,
                        CenterYCm = 3200,
                        WidthCm = 1600,
                        HeightCm = 1600,
                    },
                },
            },
            Streaming = new MassNavigationStreamingConfig
            {
                RetainSeconds = 6f,
                RadiusCm = 1000,
            },
            Scenario = new MassNavigationScenarioConfig
            {
                AgentsPerTeam = 1,
                InitialSelectedTeamId = 1,
                Teams = new[] { new MassNavigationScenarioTeamConfig { Id = 1, Name = "Team 1" } },
                SpawnLayout = new MassNavigationScenarioSpawnLayoutConfig
                {
                    Kind = "OrbitOpposedTargets",
                    OrbitRadiusCm = 3000f,
                    RandomSeed = 39916,
                },
            },
            ScenarioRuntime = new MassNavigationScenarioRuntimeConfig
            {
                AutoSpawnConfiguredScenario = false,
                InitialSelectionScratchCapacity = 4,
                InitialSelectedEntityCapacity = 4,
                RuntimeCapacity = new MassNavigationRuntimeCapacityConfig
                {
                    NavigationGroupCapacity = 4,
                    GroupMembershipAgentCapacity = 8,
                    SelectionMemberScratchCapacity = 4,
                    GroupMemberCapacity = 4,
                    OrderIngestionTokenCapacity = 4,
                    OrderIngestionMemberCapacity = 4,
                    LoadedChunkCapacity = 32,
                    MetadataTeamCapacity = 1,
                },
            },
            Cadence = new MassNavigationCadenceConfig
            {
                SimulationHz = 20,
                TargetUpdateHz = 20,
                FlowStepHz = 20,
                FlowCrowdStampHz = 20,
                FlowObstacleStampHz = 20,
                HardResolveHz = 20,
                EntitySyncHz = 20,
                MaxStepsPerFixedTick = 1,
                HardResolveCandidateThresholdAgents = 1,
                OrderIdleScanIntervalFrames = 1,
            },
            Presentation = new MassNavigationPresentationConfig
            {
                RequiredMeshAssetIds = new[] { "mass_navigation_test_agent" },
                Teams = Array.Empty<MassNavigationTeamPresentationConfig>(),
            },
            AgentProfiles = new MassNavigationAgentProfileSetConfig
            {
                DefaultProfileId = ProfileId,
                Profiles = new[]
                {
                    new MassNavigationAgentProfileConfig
                    {
                        Id = ProfileId,
                        Heavy = false,
                        VisualScale = 1f,
                        SpeedCmPerSecond = AgentSpeedCmPerSecond,
                        EveryNth = 0,
                        NthOffset = 0,
                    },
                },
            },
            Flow = new MassNavigationFlowTuning
            {
                Enabled = true,
                IterationsPerStep = 128,
                MaxIterationsPerStep = 128,
                StepIntervalTicks = 1,
                CrowdStampIntervalTicks = 1,
                ObstacleStampIntervalTicks = 1,
                ForceRefreshFlow = true,
                ForceRefreshCrowd = true,
                ForceRefreshObstacles = true,
            },
            Arrival = CreateArrivalTuning(enabled: false),
            Avoidance = CreateAvoidanceTuning(),
            Semantics = CreateCrowdSemantics(),
        };

        config.Solver.Validate();
        config.World.Validate(config.Solver);
        config.Streaming.Validate();
        config.ScenarioRuntime.Validate();
        config.Scenario.Validate(config.ScenarioRuntime);
        config.Presentation.Validate(config.Scenario, config.ScenarioRuntime, config.World);
        config.Cadence.Validate();
        config.Flow.Validate();
        config.Arrival.Validate();
        config.Avoidance.Validate();
        config.Semantics.Validate();
        config.AgentProfiles.Validate();
        config.AgentProfiles.BindAgentProfiles(new AgentProfileRegistry(new[]
        {
            new AgentProfileConfig
            {
                Id = ProfileId,
                RadiusCm = AgentRadiusCm,
                HeightCm = 180f,
                ClearanceCm = 40f,
                Mass = 1f,
                Layer = Layer,
            },
        }));
        return config;
    }

    private static MassNavigationFlowArrivalTuning CreateArrivalTuning(bool enabled)
        => new()
        {
            Enabled = enabled,
            TimeoutMs = 1500,
            TimeoutMinMs = 250,
            TimeoutMaxMs = 10000,
            ProgressDistanceCm = 60,
            ProgressDistanceMinCm = 10,
            ProgressDistanceMaxCm = 500,
            WakePushDistanceCm = 80,
            WakePushDistanceMinCm = 10,
            WakePushDistanceMaxCm = 500,
            MaxRetryCountMin = 0,
            MaxRetryCountMax = 16,
            MaxRetryCount = 2,
        };

    private static MassNavigationFlowAvoidanceTuning CreateAvoidanceTuning()
        => new()
        {
            Mode = "Sonar",
            Orca = new MassNavigationFlowOrcaAvoidanceConfig
            {
                TimeHorizonSeconds = 0.85f,
                MaxNeighbors = 16,
            },
            Sonar = new MassNavigationFlowSonarAvoidanceConfig
            {
                MaxSteerAngleDeg = 280,
                BackwardPenaltyAngleDeg = 230,
                PredictionTimeScale = 0.9f,
                IgnoreBehindMovingAgents = true,
                BlockedStop = false,
                UsePreferredVelocityWhenBlocked = true,
                TimeHorizonSeconds = 0.85f,
                MaxNeighbors = 16,
            },
            DominantMassRatio = 2.25f,
            FriendlyResponseScale = 1.1f,
            FriendlyResponseMin = 0.35f,
            FriendlyResponseMax = 2.75f,
            NonFriendlyResponseScale = 1.25f,
            NonFriendlyResponseMin = 0.25f,
            NonFriendlyResponseMax = 3.25f,
            DominantPushResponseScale = 1.6f,
            DominantPushResponseMin = 0.15f,
            DominantPushResponseMax = 4.5f,
            FriendlyCorrectionShareMin = 0.18f,
            FriendlyCorrectionShareMax = 0.82f,
            DominantCorrectionOtherMassWeight = 1.8f,
            DominantCorrectionShareMin = 0.05f,
            DominantCorrectionShareMax = 0.95f,
            NonFriendlyCorrectionOtherMassWeight = 1.2f,
            NonFriendlyCorrectionShareMin = 0.08f,
            NonFriendlyCorrectionShareMax = 0.92f,
        };

    private static MassNavigationCrowdSemantics CreateCrowdSemantics()
        => new()
        {
            Obstacle = new MassNavigationObstacleSemantics
            {
                HardResolveCandidateDistanceCm = 100f,
                SoftPushPaddingCm = 60f,
                SoftPushForceScale = 3f,
            },
            TargetProjection = new MassNavigationTargetProjectionSemantics
            {
                TeamTargetClearanceCm = 60f,
                GroupCenterClearanceCm = 60f,
                TeamSlotClearanceCm = 45f,
                GroupSlotClearanceCm = 50f,
                LooseTargetClearanceCm = 50f,
            },
            Group = new MassNavigationGroupSemantics
            {
                SpawnSpacingCm = 46f,
                SpawnJitterCm = 12f,
                TeamSlotSpacingCm = 90f,
                FormationLineSpacingCm = 180f,
                FormationSquareSpacingCm = 80f,
                FormationCircleSpacingCm = 180f,
                FormationCircleMinRadiusCm = 200f,
                FormationWedgeSpacingCm = 180f,
                FormationRotationEpsilonRadians = 0.00001f,
                FormationRotationSpeedRadiansPerSecond = 2.5f,
                PullDeadZoneCm = 50f,
                PullClampCm = 2000f,
                ArrivedRadiusCm = 150f,
                FormationArriveThresholdCm = 200f,
                LooseArriveThresholdCm = 300f,
                UnitTargetStopThresholdCm = 50f,
                FormationFlowSlowRadiusCm = 400f,
                NearSlotBlend = 0.82f,
                FarSlotBlend = 0.38f,
                NearSlotBlendDistanceSq = 4_000_000f,
            },
            Steering = new MassNavigationSteeringSemantics
            {
                SeparationRadiusCm = 200f,
                GoalArrivalRadiusCm = 1200f,
                FlowObstacleAvoidanceScale = 0.45f,
                FormationSeparationScale = 2f,
                LooseSeparationScale = 4f,
                VelocityBlendPerSecond = 5f,
            },
            Solver = new MassNavigationSolverSemantics
            {
                MinNavMass = 0.001f,
                MinVisualScale = 0.01f,
                MaxStepDtSeconds = TickSeconds,
                ParallelStepMinAgents = 2048,
                DirectionEpsilonSq = 0.0001f,
                NormalizationEpsilonSq = 0.000001f,
                InverseSqrtMinValue = 1e-8f,
                EntitySyncPositionEpsilonSq = 0.25f,
                EntitySyncVelocityEpsilonSq = 0.01f,
                FacingVelocityEpsilonSq = 0.01f,
                FlowBlockedCellCost = 99999f,
                FlowBlockedCellThreshold = 9999f,
                FlowTargetStopDistanceSq = 1f,
                FlowObstacleNeighborRadiusCells = 2,
                FlowObstacleNeighborWeight = 1.5f,
                FlowObstacleAvoidanceWeight = 1.5f,
                CoincidentPairHashBucketCount = 1024,
                CoincidentPairHashPrimeA = 73856093,
                CoincidentPairHashPrimeB = 19349663,
            },
        };

    private static List<MassNavigationObstacleSnapshot> CreateRuntimeObstacles(WorldAabbCm blockedAabb)
    {
        const float spacingCm = 300f;
        const float radiusCm = 90f;
        var obstacles = new List<MassNavigationObstacleSnapshot>(96);
        for (float y = blockedAabb.Top + (spacingCm * 0.5f); y < blockedAabb.Bottom; y += spacingCm)
        {
            for (float x = blockedAabb.Left + (spacingCm * 0.5f); x < blockedAabb.Right; x += spacingCm)
            {
                obstacles.Add(new MassNavigationObstacleSnapshot(x, y, radiusCm));
            }
        }

        return obstacles;
    }

    private static IReadOnlyList<byte[]> RequireDetourPayloads(NavBakeResult bake)
    {
        var payloads = new List<byte[]>(bake.Entries.Count);
        for (int i = 0; i < bake.Entries.Count; i++)
        {
            NavBakeResultEntry entry = bake.Entries[i];
            if (!entry.Success || entry.DetourTileBytes == null || entry.DetourTileBytes.Length == 0)
            {
                throw new InvalidOperationException($"Missing Detour navmesh payload for tile {entry.Target}.");
            }

            payloads.Add(entry.DetourTileBytes);
        }

        if (payloads.Count == 0)
        {
            throw new InvalidOperationException("No Detour navmesh payloads were produced.");
        }

        return payloads;
    }

    private static void RequireCommittedNavTiles(string repoRoot, SparseGridLogicTerrainField terrain)
    {
        string navRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "terrain_nav_closed_loop",
            "TerrainNavClosedLoopShowcaseMod");
        for (int y = 0; y < terrain.HeightChunks; y++)
        {
            for (int x = 0; x < terrain.WidthChunks; x++)
            {
                string relative = NavAssetPaths.GetNavTileRelativePath(MapId, Layer, ProfileId, x, y);
                string path = Path.Combine(navRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                RequireFile(path);
                using FileStream stream = File.OpenRead(path);
                NavTile tile = NavTileBinary.Read(stream);
                Assert.That(tile.TileId, Is.EqualTo(new NavTileId(x, y, Layer)));
                Assert.That(tile.TriangleCount, Is.GreaterThan(0));
            }
        }
    }

    private static NavAreaCostTable BuildAreaCostTable(NavMeshBakeConfig config)
    {
        Fix64[] costs = new Fix64[256];
        for (int i = 0; i < costs.Length; i++)
        {
            costs[i] = Fix64.OneValue;
        }

        for (int i = 0; i < config.Areas.Count; i++)
        {
            NavAreaCostConfig area = config.Areas[i];
            costs[(byte)area.AreaId] = Fix64.FromFloat(area.Cost);
        }

        return new NavAreaCostTable(costs);
    }

    private static void AssertPathOk(NavPathResult path, string source)
    {
        Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), $"{source} did not produce a path.");
        Assert.That(path.PathXcm.Length, Is.EqualTo(path.PathZcm.Length));
        Assert.That(path.PathXcm.Length, Is.GreaterThan(1));
    }

    private static void AssertPathAvoidsBlockedArea(
        NavPathResult path,
        SparseGridLogicTerrainField terrain,
        WorldAabbCm blockedAabb)
    {
        for (int i = 0; i < path.PathXcm.Length; i++)
        {
            var point = new Vector2(path.PathXcm[i], path.PathZcm[i]);
            AssertWalkableCell(terrain, point);
            Assert.That(ContainsAabb(blockedAabb, point), Is.False, $"Path point {i} enters blocked AABB.");
        }

        for (int i = 1; i < path.PathXcm.Length; i++)
        {
            var a = new Vector2(path.PathXcm[i - 1], path.PathZcm[i - 1]);
            var b = new Vector2(path.PathXcm[i], path.PathZcm[i]);
            Assert.That(
                SegmentIntersectsAabb(a, b, blockedAabb),
                Is.False,
                $"Path segment {i - 1}->{i} intersects blocked AABB. a={a} b={b} blocked={blockedAabb}");
            AssertSegmentSamplesWalkable(terrain, a, b);
        }
    }

    private static void AssertWalkableCell(SparseGridLogicTerrainField terrain, Vector2 worldCm)
    {
        int col = Math.Clamp((int)MathF.Floor(worldCm.X / terrain.CellSizeCm), 0, terrain.WidthCells - 1);
        int row = Math.Clamp((int)MathF.Floor(worldCm.Y / terrain.CellSizeCm), 0, terrain.HeightCells - 1);
        LogicTerrainCell cell = terrain.GetCell(col, row);
        Assert.That(cell.IsBlocked, Is.False, $"Cell {col},{row} is blocked.");
        Assert.That(cell.AreaId, Is.Not.EqualTo(BlockedAreaId), $"Cell {col},{row} has Blocked area id.");
    }

    private static void AssertSegmentSamplesWalkable(SparseGridLogicTerrainField terrain, Vector2 a, Vector2 b)
    {
        float distance = Vector2.Distance(a, b);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / 25f));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            AssertWalkableCell(terrain, Vector2.Lerp(a, b, t));
        }
    }

    private static List<Vector2> DensifyPath(NavPathResult path, float spacingCm)
    {
        var points = new List<Vector2>(path.PathXcm.Length * 4)
        {
            new(path.PathXcm[0], path.PathZcm[0])
        };
        for (int i = 1; i < path.PathXcm.Length; i++)
        {
            Vector2 a = points[^1];
            Vector2 b = new(path.PathXcm[i], path.PathZcm[i]);
            float distance = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / spacingCm));
            for (int step = 1; step <= steps; step++)
            {
                points.Add(Vector2.Lerp(a, b, step / (float)steps));
            }
        }

        return points;
    }

    private static void AssertMissingSourcesFailFast(ShowcaseAssets assets, NavBakeResult bake)
    {
        Assert.Throws<FileNotFoundException>(() => LoadVisualHeightmap(assets.HeightmapPath + ".missing"));
        Assert.Throws<FileNotFoundException>(() => LoadLogicTerrain(assets.LogicTerrainPath + ".missing"));
        Assert.Throws<InvalidOperationException>(() => RequireDetourPayloads(new NavBakeResult(Array.Empty<NavBakeResultEntry>())));

        var emptyStore = new NavTileStore(id => throw new FileNotFoundException($"Missing committed nav tile {id}."));
        var query = new NavQueryService(emptyStore, Layer, BuildAreaCostTable(assets.NavConfig), ChunkSizeCells * CellSizeCm, ChunkSizeCells * CellSizeCm);
        NavPathResult missingNav = query.TryFindPath(900, 3200, 5500, 3200);
        Assert.That(missingNav.Status, Is.EqualTo(NavPathStatus.NotReady));
        Assert.That(bake.SuccessCount, Is.GreaterThan(0), "Fail-fast assertions must run after a real navmesh bake, not instead of it.");
    }

    private static IReadOnlyList<NavTileId> KnownTileIds(NavBakeResult bake)
    {
        var ids = new NavTileId[bake.Entries.Count];
        for (int i = 0; i < bake.Entries.Count; i++)
        {
            ids[i] = bake.Entries[i].Tile.TileId;
        }

        return ids;
    }

    private static void SampleHeightRange(VisualHeightmapRuntime heightmap, out float minHeight, out float maxHeight)
    {
        minHeight = float.PositiveInfinity;
        maxHeight = float.NegativeInfinity;
        WorldAabbCm bounds = heightmap.Bounds;
        for (int y = 0; y <= 8; y++)
        {
            for (int x = 0; x <= 8; x++)
            {
                float worldX = bounds.Left + (bounds.Width * (x / 8f));
                float worldY = bounds.Top + (bounds.Height * (y / 8f));
                Assert.That(heightmap.TrySampleHeightCm(worldX, worldY, out float h), Is.True);
                minHeight = MathF.Min(minHeight, h);
                maxHeight = MathF.Max(maxHeight, h);
            }
        }
    }

    private static void ScanTerrainClassifications(
        SparseGridLogicTerrainField terrain,
        out bool hasBlocked,
        out bool hasRidge,
        out int minBlockedCol,
        out int maxBlockedCol,
        out int minBlockedRow,
        out int maxBlockedRow)
    {
        hasBlocked = false;
        hasRidge = false;
        minBlockedCol = int.MaxValue;
        maxBlockedCol = int.MinValue;
        minBlockedRow = int.MaxValue;
        maxBlockedRow = int.MinValue;

        for (int row = 0; row < terrain.HeightCells; row++)
        {
            for (int col = 0; col < terrain.WidthCells; col++)
            {
                LogicTerrainCell cell = terrain.GetCell(col, row);
                if (cell.AreaId == BlockedAreaId && cell.IsBlocked)
                {
                    hasBlocked = true;
                    minBlockedCol = Math.Min(minBlockedCol, col);
                    maxBlockedCol = Math.Max(maxBlockedCol, col);
                    minBlockedRow = Math.Min(minBlockedRow, row);
                    maxBlockedRow = Math.Max(maxBlockedRow, row);
                }

                if (cell.AreaId == RidgeAreaId && !cell.IsBlocked)
                {
                    hasRidge = true;
                }
            }
        }
    }

    private static bool ContainsAabb(WorldAabbCm aabb, Vector2 point)
        => point.X >= aabb.Left && point.X <= aabb.Right && point.Y >= aabb.Top && point.Y <= aabb.Bottom;

    private static float DistanceToAabb(Vector2 point, WorldAabbCm aabb)
    {
        float dx = point.X < aabb.Left ? aabb.Left - point.X : point.X > aabb.Right ? point.X - aabb.Right : 0f;
        float dy = point.Y < aabb.Top ? aabb.Top - point.Y : point.Y > aabb.Bottom ? point.Y - aabb.Bottom : 0f;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool SegmentIntersectsAabb(Vector2 a, Vector2 b, WorldAabbCm aabb)
    {
        float distance = Vector2.Distance(a, b);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / 25f));
        for (int i = 0; i <= steps; i++)
        {
            if (ContainsAabb(aabb, Vector2.Lerp(a, b, i / (float)steps)))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required showcase data source is missing: {path}", path);
        }
    }

    private static string FormatBakeFailures(NavBakeResult result)
    {
        var lines = new List<string>();
        for (int i = 0; i < result.Entries.Count; i++)
        {
            NavBakeResultEntry entry = result.Entries[i];
            if (!entry.Success)
            {
                lines.Add($"{entry.Target} stage={entry.Artifact.Stage} code={entry.Artifact.ErrorCode} msg={entry.Artifact.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record ShowcaseAssets(
        string ModRoot,
        string HeightmapPath,
        string LogicTerrainPath,
        VisualHeightmapRuntime Heightmap,
        SparseGridLogicTerrainField Terrain,
        NavMeshBakeConfig NavConfig,
        AgentProfileRegistry AgentProfiles);

    private readonly record struct TerrainFacts(WorldAabbCm BlockedWorldAabb, float MinHeightCm, float MaxHeightCm);

    private readonly record struct NavigationEndpoints(Vector2 Start, Vector2 Goal);

    private readonly record struct AgentRunResult(
        bool ReachedGoal,
        float FinalDistanceCm,
        Vector2 FinalPositionCm,
        int WaypointIndex,
        int WaypointCount,
        Vector2 TargetWaypointCm,
        float MinDistanceToBlockedAabbCm,
        float MinSampledTerrainHeightCm,
        float MaxSampledTerrainHeightCm,
        int GroundingSampleCount);
}
