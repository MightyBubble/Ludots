using System.Collections.Generic;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Registry;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 认知场从地形源起底，再重标 believed 足迹。建筑只挡格、不改区域；道路才把区域写成 road。
/// 未见的道路若还留在场里，说明起底用了当时的真相活栅格。
/// </summary>
[TestFixture]
public class CrowdBeliefTerrainTests
{
    [Test]
    public void BeliefField_IsIdenticalAcrossUnseenTruthChanges()
    {
        using var world = LandWorld.Open();
        var initial = LandWorld.Entities(world.Store);
        var ranks = System.Array.Empty<BeliefRank>();
        var before = CrowdStructuresStore.BuildBeliefField(world.Config, world.Store.TerrainSurface(), initial, ref ranks);
        Assert.That(before.Area, Is.EqualTo(world.Store.Area), "开局认知应与真相地面逐格相同");
        Assert.That(before.Blocked, Is.EqualTo(world.Store.Blocked));

        uint digest = LandWorld.Digest(before.Area, before.Blocked);
        world.PlaceUnseen();

        var after = CrowdStructuresStore.BuildBeliefField(world.Config, world.Store.TerrainSurface(), initial, ref ranks);
        Assert.That(after.Area, Is.EqualTo(before.Area));
        Assert.That(after.Blocked, Is.EqualTo(before.Blocked));
        Assert.That(LandWorld.Digest(after.Area, after.Blocked), Is.EqualTo(digest));

        var everyone = LandWorld.Entities(world.Store);
        var aligned = CrowdStructuresStore.BuildBeliefField(world.Config, world.Store.TerrainSurface(), everyone, ref ranks);
        Assert.That(aligned.Area, Is.EqualTo(world.Store.Area), "认知等于真相时，重标结果应与活栅格逐格相同");
        Assert.That(aligned.Blocked, Is.EqualTo(world.Store.Blocked));
        Assert.That(after.Area[world.BuildingCell], Is.EqualTo(world.TerrainArea));
        Assert.That(after.Blocked[world.BuildingCell], Is.EqualTo(0));
        Assert.That(world.Store.Blocked[world.BuildingCell], Is.EqualTo(1));
        Assert.That(after.Area[world.RoadCell], Is.EqualTo(world.TerrainArea));
        Assert.That(world.Store.Area[world.RoadCell], Is.EqualTo(world.RoadArea));
    }

    [Test]
    public void UnseenBuilding_UsesTerrainOnOtherGroupsVariant()
    {
        using var world = LandWorld.Open();
        var initial = LandWorld.Entities(world.Store);
        world.PlaceUnseen();
        foreach (var rect in world.PlacedRects)
        {
            CrowdNavRebake.RebakeNav(world.Nav, world.Sources, world.Store, rect, world.Cache);
        }

        world.Session.Beliefs!.Register(1, initial, null);
        var hidden = world.Session.ResolveNavContext(CrowdBeliefNavs.BeliefStride + world.Nav.Id);
        Assert.That(world.Nav.Passable[world.BuildingCell], Is.EqualTo(0));
        Assert.That(hidden.Passable[world.BuildingCell], Is.EqualTo(1));
        Assert.That(hidden.Cost[world.BuildingCell], Is.EqualTo(world.TerrainCost));
        Assert.That(hidden.Cost[world.RoadCell], Is.EqualTo(world.TerrainCost));
        Assert.That(world.Nav.Cost[world.RoadCell], Is.EqualTo(world.RoadCost));
        Assert.That(hidden.Cost[world.FireCell], Is.EqualTo(world.TerrainCost));
        Assert.That(world.Nav.Cost[world.FireCell], Is.EqualTo(world.FireCost));

        world.Session.Beliefs.Register(2, LandWorld.Entities(world.Store), null);
        var seen = world.Session.ResolveNavContext(CrowdBeliefNavs.BeliefStride * 2 + world.Nav.Id);
        Assert.That(seen.Passable[world.BuildingCell], Is.EqualTo(0));
        Assert.That(seen.Cost[world.RoadCell], Is.EqualTo(world.RoadCost));
    }

    private sealed class LandWorld : System.IDisposable
    {
        public CrowdSimulationRuntimeConfig Config { get; }
        public CrowdStructuresStore Store { get; }
        public NavContext Nav { get; }
        public CrowdRebakeSources Sources { get; }
        public NavTileCache Cache { get; }
        public CrowdSimSession Session { get; }
        public int BuildingCell { get; }
        public int RoadCell { get; }
        public int FireCell { get; }
        public byte TerrainArea { get; }
        public byte RoadArea { get; }
        public Fix64 TerrainCost { get; }
        public Fix64 RoadCost { get; }
        public Fix64 FireCost { get; }
        public List<(int X0, int Y0, int X1, int Y1)> PlacedRects { get; } = new();

        private readonly PathQueryService _service;
        private bool _placed;

        private LandWorld(
            CrowdSimulationRuntimeConfig config,
            CrowdStructuresStore store,
            NavContext nav,
            CrowdRebakeSources sources,
            NavTileCache cache,
            CrowdSimSession session,
            PathQueryService service,
            int buildingCell,
            int roadCell,
            int fireCell,
            byte terrainArea,
            byte roadArea,
            Fix64 terrainCost,
            Fix64 roadCost,
            Fix64 fireCost)
        {
            Config = config;
            Store = store;
            Nav = nav;
            Sources = sources;
            Cache = cache;
            Session = session;
            _service = service;
            BuildingCell = buildingCell;
            RoadCell = roadCell;
            FireCell = fireCell;
            TerrainArea = terrainArea;
            RoadArea = roadArea;
            TerrainCost = terrainCost;
            RoadCost = roadCost;
            FireCost = fireCost;
        }

        public static LandWorld Open()
        {
            var map = TestDefaults.DemoMap();
            const int boardCm = 100_000;
            map.Boards[0] = new BoardConfig { WidthCm = boardCm, HeightCm = boardCm };
            var config = TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules());
            int side = config.NavCellsPerSide;
            int cell = config.NavCellSizeCm;
            int land = IndexOf(config.TerrainTypeIds, "land");
            int n2 = side * side;
            var terrain = new byte[n2];
            System.Array.Fill(terrain, (byte)land);
            int buildingCell = 2 * side + 2;
            int roadCell = 6 * side + 6;
            int fireCell = 12 * side + 12;
            var blocker = new BlockerFootprint(cell / 2, cell / 2, cell / 2);
            var surface = SurfaceGrid.Build(config, new NavSurfaceAsset
            {
                NavCellsX = side,
                NavCellsY = side,
                NavCellSizeCm = cell,
                TerrainTypeIds = CopyIds(config.TerrainTypeIds),
                TerrainCells = terrain,
                JumpCandidates = System.Array.Empty<NavSurfaceJumpCandidate>(),
            }, new[] { blocker });

            var span = new CrowdSimulationBridgeSpan
            {
                X0Cm = cell / 2,
                Y0Cm = cell + cell / 2,
                X1Cm = 3 * cell + cell / 2,
                Y1Cm = cell + cell / 2,
                WidthCm = cell,
            };
            var deck = UpperLayerBake.RasterizeDecks(new[] { new BridgeDeckRecord(span, "deck") }, config);
            var heights = new NavHeightField
            {
                NavCellsPerSide = side,
                NavCellSizeCm = cell,
                HeightsCm = new Fix64[n2],
                Slope = new Fix64[n2],
            };
            var cache = new NavTileCache(
                config.NavtileCacheCapacity, config.Hpa.NavCellsPerTile,
                config.Navmesh.MinRegionNavCellArea.ToDouble(),
                config.Navmesh.MaxSimplificationErrorNavCells.ToDouble(),
                config.Navmesh.MaxEdgeLenNavCells.ToDouble(),
                config.Navmesh.MaxVertsPerPoly);
            int foot = IndexOfAgent(config, "foot");
            int clearance = ClearanceOf(config, "foot_r100");
            var nav = NavContextBaker.Bake(config, surface, heights, deck, foot, clearance, cache);
            var store = CrowdStructuresStore.Build(config, surface, new[] { blocker });
            store.InstallMapBridges(store.TemplateIndexOf("bridge"), new[] { span });
            var navs = new Dictionary<int, NavContext> { [nav.Id] = nav };
            var sources = new CrowdRebakeSources(config, heights, deck, surface.JumpCandidates);
            var radiusClasses = CrowdDeployment.DistinctRadiusClasses(config);
            var navBy = new Dictionary<(int, int), NavContext>();
            foreach (var profile in config.Profiles)
            {
                if (profile.Id != "foot_r100") continue;
                int rIdx = CrowdDeployment.IndexOfRadiusClass(radiusClasses, (int)profile.RadiusCm.ToInt());
                navBy[(profile.AgentTypeIndex, rIdx)] = nav;
            }

            var session = new CrowdSimSession(config, ArchWorld.Create(), navs, navBy, Wiring(config));
            session.Structures = store;
            session.RebakeSources = sources;
            session.NavTileCache = cache;
            session.BindVision(new FogLayerRegistry(), new FogFieldStore());
            var service = new PathQueryService(navs, config, 1, System.TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));

            int roadArea = IndexOfArea(config, "road");
            int fireArea = IndexOfArea(config, "burning");
            var costs = config.AgentTypes[foot].CostByArea;
            return new LandWorld(
                config, store, nav, sources, cache, session, service,
                buildingCell, roadCell, fireCell,
                surface.Area[buildingCell], (byte)roadArea,
                costs[surface.Area[buildingCell]], costs[roadArea], costs[fireArea]);
        }

        public void PlaceUnseen()
        {
            if (_placed) return;
            _placed = true;
            int cell = Config.NavCellSizeCm;
            int building = Store.TemplateIndexOf("building");
            var box = CrowdStructureFootprint.Make(
                Config.Structures.Templates[building],
                Fix64.FromInt(2 * cell + cell / 2),
                Fix64.FromInt(2 * cell + cell / 2),
                Fix64.FromInt(cell),
                Fix64.Zero,
                Fix64.Zero);
            PlacedRects.Add(Store.PlaceEntity(building, box, -1).Rect);

            int road = Store.TemplateIndexOf("road");
            var path = CrowdStructureFootprint.Make(
                Config.Structures.Templates[road],
                Fix64.FromInt(5 * cell + cell / 2),
                Fix64.FromInt(6 * cell + cell / 2),
                Fix64.FromInt(cell),
                Fix64.FromInt(8 * cell + cell / 2),
                Fix64.FromInt(6 * cell + cell / 2));
            PlacedRects.Add(Store.PlaceEntity(road, path, -1).Rect);

            int fire = Store.TemplateIndexOf("fire");
            var disc = CrowdStructureFootprint.Make(
                Config.Structures.Templates[fire],
                Fix64.FromInt(12 * cell + cell / 2),
                Fix64.FromInt(12 * cell + cell / 2),
                Fix64.FromInt(2000),
                Fix64.Zero,
                Fix64.Zero);
            PlacedRects.Add(Store.PlaceEntity(fire, disc, -1).Rect);
            Assert.That(Store.Area[RoadCell], Is.EqualTo(RoadArea), "道路没有涂到预期格");
            Assert.That(Store.Blocked[BuildingCell], Is.EqualTo(1), "建筑没有挡住预期格");
        }

        public static List<(int Id, int TplIndex, CrowdStructureFootprint Fp)> Entities(CrowdStructuresStore store)
        {
            var list = new List<(int, int, CrowdStructureFootprint)>();
            foreach (int id in store.EntityIds)
            {
                store.TryGetFootprint(id, out var fp);
                store.TryGetTemplate(id, out int tpl);
                list.Add((id, tpl, fp));
            }

            return list;
        }

        public static uint Digest(byte[] area, byte[] blocked)
        {
            uint hash = 2166136261;
            void Mix(byte[] bytes)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619;
                }
            }

            Mix(area);
            Mix(blocked);
            return hash;
        }

        public void Dispose() => _service.Dispose();

        private static CrowdSimPresentationWiring Wiring(CrowdSimulationRuntimeConfig config)
        {
            var pipeline = new ConfigPipeline(new VirtualFileSystem(), null!);
            var rows = new (string TemplateId, int TemplateKeyId)[config.UnitTypes.Count][];
            for (int i = 0; i < rows.Length; i++) rows[i] = System.Array.Empty<(string, int)>();
            var collections = new EntityCollectionStore(new StringIntRegistry());
            return new CrowdSimPresentationWiring
            {
                StableIds = new PresentationStableIdAllocator(),
                TemplateRegistry = new DataRegistry<EntityTemplate>(pipeline),
                TemplateKeys = new EntityTemplateKeyRegistry(),
                TemplatesByUnitTypeRadius = rows,
                RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                    Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusKey),
                SelectedCollectionKeyId = collections.KeyRegistry.Register("belief-terrain-selected"),
                Collections = collections,
            };
        }

        private static int ClearanceOf(CrowdSimulationRuntimeConfig config, string profileId)
        {
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                if (config.Profiles[i].Id == profileId) return config.Profiles[i].ClearanceNavCells;
            }

            throw new System.InvalidOperationException($"没有档案 {profileId}");
        }

        private static int IndexOfAgent(CrowdSimulationRuntimeConfig config, string id)
        {
            for (int i = 0; i < config.AgentTypes.Count; i++)
            {
                if (config.AgentTypes[i].Id == id) return i;
            }

            throw new System.InvalidOperationException($"没有移动类型 {id}");
        }

        private static int IndexOfArea(CrowdSimulationRuntimeConfig config, string id)
        {
            for (int i = 0; i < config.NavAreas.Count; i++)
            {
                if (config.NavAreas[i].Id == id) return i;
            }

            throw new System.InvalidOperationException($"没有区域 {id}");
        }

        private static int IndexOf(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return i;
            }

            throw new System.InvalidOperationException($"没有地形 {id}");
        }

        private static string[] CopyIds(IReadOnlyList<string> ids)
        {
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) copy[i] = ids[i];
            return copy;
        }
    }
}
