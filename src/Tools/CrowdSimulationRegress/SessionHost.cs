using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Registry;
using Ludots.Core.Systems;
using Ludots.Core.Vision;

namespace CrowdSimulationRegress;

/// <summary>
/// 无头会话装配。装载链与地图探针相同：标准参数、能力 Mod、种子目录三份资产，
/// 关系目录从仓库根往上找。结构仓和桥按部署真值会话的方式挂上。
/// </summary>
static class SessionHost
{
    public static CrowdSimRegressHost Open(string seedDir, string standardDir, string demoDir, bool movement)
    {
        var bundle = Load(seedDir, standardDir, demoDir);
        var world = World.Create();
        PathQueryService? service = null;
        try
        {
            var session = Bake(bundle, world, demoDir, standardDir, movement, out service, out double bakeMs);
            return new CrowdSimRegressHost
            {
                Session = session,
                BakeMs = bakeMs,
                Release = () =>
                {
                    service?.Dispose();
                    world.Dispose();
                },
            };
        }
        catch
        {
            service?.Dispose();
            world.Dispose();
            throw;
        }
    }

    private static CrowdSimSession Bake(
        SeedBundle bundle,
        World world,
        string demoDir,
        string standardDir,
        bool movement,
        out PathQueryService? service,
        out double bakeMs)
    {
        service = null;
        var runtime = bundle.Runtime;
        var started = Stopwatch.GetTimestamp();
        var deck = UpperLayerBake.RasterizeLayeredPaths(bundle.MapSurface.Bridges, runtime);
        var cache = new NavTileCache(
            runtime.NavtileCacheCapacity, runtime.Hpa.NavCellsPerTile,
            runtime.Navmesh.MinRegionNavCellArea.ToDouble(), runtime.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
            runtime.Navmesh.MaxEdgeLenNavCells.ToDouble(), runtime.Navmesh.MaxVertsPerPoly);
        var navs = new Dictionary<int, NavContext>();
        var navByLayerRadius = new Dictionary<(int, int), NavContext>();
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtime);
        var seen = new HashSet<int>();
        for (int a = 0; a < runtime.AgentTypes.Count; a++)
        {
            var clearances = new List<int>();
            for (int i = 0; i < runtime.Profiles.Count; i++)
            {
                var profile = runtime.Profiles[i];
                if (profile.AgentTypeIndex != a || clearances.Contains(profile.ClearanceNavCells)) continue;
                clearances.Add(profile.ClearanceNavCells);
            }

            for (int i = 0; i < clearances.Count; i++)
            {
                var nav = NavContextBaker.Bake(runtime, bundle.Grid, bundle.Heights, deck, a, clearances[i], cache);
                if (!seen.Add(nav.Id)) continue;
                navs[nav.Id] = nav;
            }
        }

        for (int i = 0; i < runtime.Profiles.Count; i++)
        {
            var profile = runtime.Profiles[i];
            int rIdx = CrowdDeployment.IndexOfRadiusClass(radiusClasses, (int)profile.RadiusCm.ToInt());
            navByLayerRadius[(profile.AgentTypeIndex, rIdx)] = navs[profile.NavContextId];
        }

        var session = new CrowdSimSession(runtime, world, navs, navByLayerRadius, BuildWiring(runtime, world, demoDir, standardDir));
        session.Structures = CrowdStructuresStore.Build(runtime, bundle.Grid, bundle.MapSurface.Blockers);
        session.Structures.UseUpperLayer(deck);
        session.Structures.InstallMapBridges(bundle.MapSurface.Bridges);

        session.RebakeSources = new CrowdRebakeSources(runtime, bundle.Heights, deck, bundle.Surface.JumpCandidates);
        session.NavTileCache = cache;
        bakeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        session.BindVision(new FogLayerRegistry(), new FogFieldStore());
        if (!movement) return session;

        service = new PathQueryService(
            navs, runtime, workerThreads: 1, TimeSpan.FromMilliseconds(bundle.Config.Worker.PlanTimeoutMs), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        return session;
    }

    private static CrowdSimPresentationWiring BuildWiring(
        CrowdSimulationRuntimeConfig runtime, World world, string demoDir, string standardDir)
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", demoDir);
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var mapLoader = new MapLoader(world, new WorldMap(), pipeline);
        mapLoader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
        var collections = new EntityCollectionStore(new StringIntRegistry());
        var profiles = new AgentProfileRegistry(JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            File.ReadAllText(Path.Combine(standardDir, "Navigation", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!);
        return new CrowdSimPresentationWiring
        {
            StableIds = new PresentationStableIdAllocator(),
            TemplateRegistry = mapLoader.TemplateRegistry,
            TemplateKeys = mapLoader.EntityTemplateKeys,
            TemplatesByUnitTypeRadius = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
                runtime, mapLoader.TemplateRegistry, mapLoader.EntityTemplateKeys, profiles, "回归装配"),
            RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusKey),
            SelectedCollectionKeyId = collections.KeyRegistry.Register(CrowdSimPresentationWiring.SelectedCollectionKey),
            Collections = collections,
        };
    }

    private static SeedBundle Load(string seedDir, string standardDir, string demoDir)
    {
        string[] maps = Directory.GetFiles(Path.Combine(seedDir, "Maps"), "*.json");
        if (maps.Length != 1)
        {
            throw new InvalidOperationException($"种子目录的 Maps 需要恰好一张地图（{seedDir}）。");
        }

        string mapId = Path.GetFileNameWithoutExtension(maps[0]);
        var merged = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(standardDir, "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(demoDir, "CrowdSimulationConfig.json")))!);
        var overlay = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(seedDir, "CrowdSimulationConfig.json")))!;
        ConfigPipeline.DeepMerge(merged, overlay);
        var config = CrowdSimulationConfig.Load(merged);
        var map = JsonSerializer.Deserialize<MapConfig>(
            File.ReadAllText(Path.Combine(seedDir, "Maps", $"{mapId}.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine(seedDir, "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var templatesById = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        for (int i = 0; i < templates.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(templates[i].Id)) templatesById[templates[i].Id] = templates[i];
        }

        var profileList = JsonSerializer.Deserialize<List<AgentProfileConfig>>(
            File.ReadAllText(Path.Combine(standardDir, "Navigation", "agent_profiles.json")),
            StrictJsonOptions.CreateCamelCase())!;
        var profiles = new AgentProfileRegistry(profileList);
        var navmeshJson = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(standardDir, "Navigation", "navmesh.json")))!;
        string seedNavPath = Path.Combine(seedDir, "Navigation", "navmesh.json");
        if (File.Exists(seedNavPath))
        {
            ConfigPipeline.DeepMerge(navmeshJson, (JsonObject)JsonNode.Parse(File.ReadAllText(seedNavPath))!);
        }

        var pathing = PathingConfigLoader.Parse(
            (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(standardDir, "Navigation", "pathing.json")))!);
        var unitTemplates = JsonSerializer.Deserialize<List<EntityTemplate>>(
            File.ReadAllText(Path.Combine(demoDir, "Entities", "templates.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var bake = NavMeshBakeConfigLoader.Parse(navmeshJson, profiles);
        var runtime = CrowdSimulationConfigLoader.Load(
            config,
            map,
            profiles,
            bake,
            pathing,
            CrowdUnitTemplateFacts.FromTemplates(unitTemplates),
            ReadSeedFixedHz(seedDir),
            ProjectSeedMap(seedDir, config, map));
        var surface = NavSurfaceAsset.Read(File.OpenRead(Path.Combine(seedDir, "terrain", $"{mapId}.navsurface")));
        var mapSurface = CrowdSimulationMapSurfaceSource.Extract(map, templatesById);
        string heightPath = Path.Combine(seedDir, "terrain", $"{mapId}.height");
        var height = ContinuousHeightmapBinary.Read(File.OpenRead(heightPath));
        var board = NavSolverDeclaration.RequireBoard(map, NavSolverDeclaration.RequireMap(bake, map.Id));
        var ingested = CrowdSimulationWorldIngest.Build(
            runtime,
            board.WidthCm,
            board.HeightCm,
            surface,
            Path.Combine(seedDir, "terrain", $"{mapId}.navsurface"),
            height,
            heightPath,
            mapSurface.Blockers);
        return new SeedBundle(mapId, config, runtime, surface, height, mapSurface, ingested.Grid, ingested.Heights);
    }

    private static int ReadSeedFixedHz(string seedDir)
    {
        string path = Path.Combine(seedDir, "Engine", "clock.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"种子目录需要 Engine/clock.json 提供 FixedHz（{path}）。");
        }

        var clock = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"Engine/clock.json 根必须是对象（{path}）。");
        if (clock["FixedHz"] is not JsonValue hz || !hz.TryGetValue<int>(out int fixedHz) || fixedHz < 1)
        {
            throw new InvalidOperationException($"Engine/clock.json: FixedHz 需为 ≥ 1 的整数（{path}）。");
        }

        return fixedHz;
    }

    private static CrowdPairRules ProjectSeedMap(string seedDir, CrowdSimulationConfig config, MapConfig map)
    {
        using var world = World.Create();
        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var bands = new RelationshipBandRegistry();
        foreach (string catalogPath in RelationshipCatalogPaths(seedDir))
        {
            RelationshipCatalogConfig? catalog = JsonSerializer.Deserialize<RelationshipCatalogConfig>(
                File.ReadAllText(catalogPath),
                RelationshipCatalogPipelineLoader.SerializerOptions);
            if (catalog == null)
            {
                throw new InvalidOperationException($"关系目录反序列化为空：{catalogPath}");
            }

            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, metrics, flags, bands);
        }

        var relationships = new RelationshipRuntime(
            world,
            types,
            metrics,
            flags,
            bands,
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(world));
        var index = new MapLoadEntityIndex();
        foreach (var authored in map.Entities)
        {
            if (string.IsNullOrWhiteSpace(authored.InstanceId)) continue;
            index.Register(map.Id, authored.InstanceId, world.Create());
        }

        var teams = new TeamEntityLookup();
        foreach (var team in map.Teams)
        {
            Entity entity = index.GetRequired(map.Id, team.RepresentativeInstanceId, $"Teams TeamId {team.TeamId}");
            world.Add(entity, new Team { Id = team.TeamId });
            world.Add(entity, new TeamIdentity { TeamId = team.TeamId });
            teams.Register(team.TeamId, entity);
        }

        var players = new PlayerEntityLookup();
        foreach (var player in map.Players)
        {
            Entity entity = index.GetRequired(map.Id, player.RepresentativeInstanceId, $"Players PlayerId {player.PlayerId}");
            world.Add(entity, new PlayerIdentity { PlayerId = player.PlayerId });
            world.Add(entity, new Team { Id = player.TeamId });
            players.Register(player.PlayerId, entity);
        }

        using var session = new MapSession(new MapId(map.Id), map);
        InstanceRelationMaterializer.Materialize(session, map, index, relationships, types, metrics);
        return CrowdPairRulesProjection.Project(config, map, new TeamRelationQuery(relationships, teams), players, world).Rules;
    }

    private static IEnumerable<string> RelationshipCatalogPaths(string seedDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(seedDir));
        while (dir != null)
        {
            string engineCatalog = Path.Combine(dir.FullName, "assets", "Relationships", "catalog.json");
            string coreModCatalog = Path.Combine(dir.FullName, "mods", "LudotsCoreMod", "assets", "Relationships", "catalog.json");
            if (File.Exists(engineCatalog) && File.Exists(coreModCatalog))
            {
                yield return engineCatalog;
                yield return coreModCatalog;
                yield break;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"从 {seedDir} 往上找不到 assets/Relationships/catalog.json 和 mods/LudotsCoreMod 的关系目录。");
    }

    private sealed record SeedBundle(
        string MapId,
        CrowdSimulationConfig Config,
        CrowdSimulationRuntimeConfig Runtime,
        NavSurfaceAsset Surface,
        ContinuousHeightmapAsset Height,
        CrowdSimulationMapSurface MapSurface,
        SurfaceGrid Grid,
        NavHeightField Heights);
}
