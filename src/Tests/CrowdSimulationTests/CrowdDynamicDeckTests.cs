using System;
using System.Collections.Generic;
using System.IO;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Diagnostics;
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
using Ludots.Core.Systems;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 一条河把地分成两岸。桥放上之后单位从桥面层走过去，拆掉就落到地面，限时桥到期离场。
/// 每一步都和全量烘焙对过。同一份指令再跑一遍，校验码相同。
/// </summary>
[TestFixture]
public class CrowdDynamicDeckTests
{
    [Test]
    public void PlaceWalkRemoveExpire_MatchesFullBake_AndReplays()
    {
        using var first = RiverWorld.Open();
        var session = first.Session;
        int side = first.Side;
        int cell = first.Cell;
        int y = 8;
        int x0 = 2;
        int x1 = 13;
        var bridge = session.Config.Structures.Templates[session.Structures!.TemplateIndexOf("bridge")];
        var footprint = CrowdStructureFootprint.Make(
            bridge,
            Fix64.FromInt(Center(x0, cell)),
            Fix64.FromInt(Center(y, cell)),
            Fix64.FromInt(cell),
            Fix64.FromInt(Center(x1, cell)),
            Fix64.FromInt(Center(y, cell)));
        var judgeCells = Array.Empty<int>();
        var judgeFlags = Array.Empty<byte>();
        var judged = CrowdStructurePlacement.Judge(session.Structures, bridge, footprint, ref judgeCells, ref judgeFlags, out int painted);
        Assert.That(judged, Is.EqualTo(CrowdPlacementReject.None), "这条河上的桥应当能放");
        Assert.That(painted, Is.GreaterThan(0));
        bool anchor = false;
        bool portalOnly = false;
        for (int i = 0; i < painted; i++)
        {
            byte flag = judgeFlags[i];
            if ((flag & CrowdStructurePlacement.FlagEndpoint) != 0) anchor = true;
            if ((flag & CrowdStructurePlacement.FlagPortal) != 0 && (flag & CrowdStructurePlacement.FlagEndpoint) == 0)
            {
                portalOnly = true;
            }
        }

        Assert.That(anchor, Is.True, "预览应标出桥端锚点格");
        Assert.That(portalOnly, Is.True, "预览应标出桥头 portal 格");

        session.Beliefs!.Register(1, new List<(int Id, int TplIndex, CrowdStructureFootprint Fp)>(), null);
        foreach (var nav in session.Navs.Values)
        {
            session.Beliefs.Get(CrowdBeliefNavs.BeliefStride + nav.Id);
        }

        int before = session.Structures.EntityIds.Count;
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "bridge",
            XCm = Center(x0, cell),
            YCm = Center(y, cell),
            SizeCm = cell,
            ToXCm = Center(x1, cell),
            ToYCm = Center(y, cell),
        });
        Assert.That(session.LastPlacementReject, Is.EqualTo(CrowdPlacementReject.None));
        Assert.That(session.Structures.EntityIds.Count, Is.EqualTo(before + 1));
        int bridgeId = session.Structures.EntityIds[^1];
        var body = session.NavFor(first.Layer, first.RadiusIndex);
        int mid = y * side + (x0 + x1) / 2;
        Assert.That(body.UpPass[mid], Is.EqualTo(1), "河心应有桥面");
        Assert.That(body.CanReach(body.Comp[y * side + x0], body.Comp[y * side + x1]), Is.True, "两岸应经桥头连通");
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);

        int player = session.Config.Deploy.Bases[0].PlayerId;
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = player,
            XCm = Center(x0, cell),
            YCm = Center(y, cell),
            Count = 1,
            UnitType = first.UnitType,
            RIdx = first.RadiusIndex,
        });
        Assert.That(session.Units.Count, Is.EqualTo(1));
        int owned = session.Units.CopyOwnHandles(player, session.MemberCapture);
        var members = new uint[owned];
        Array.Copy(session.MemberCapture, members, owned);
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = Center(x1, cell),
            YCm = Center(y, cell),
            ShapeId = "box",
            Members = members,
        });

        var checksums = new List<ulong>();
        int ran = StepUntil(session, checksums, 400, OnDeck);
        Assert.That(OnDeck(session), Is.True, Describe(session, body, y));
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);

        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.RemoveStructureAt,
            XCm = Center((x0 + x1) / 2, cell),
            YCm = Center(y, cell),
        });
        Assert.That(session.Structures.EntityIds.Contains(bridgeId), Is.False);
        ran = StepUntil(session, checksums, ran + 6, static sim => !OnDeck(sim));
        CrowdStructureOps.FinishRebake(session);
        Assert.That(OnDeck(session), Is.False, "拆桥后单位应离开桥面层");
        Assert.That(LevelOf(session, 0), Is.EqualTo(0));
        int stood = CellOf(session, 0);
        Assert.That(body.Passable[stood], Is.EqualTo(1), $"拆桥后单位停在不可走格 {stood % side},{stood / side}");
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);

        int beforeTimed = session.Structures.EntityIds.Count;
        Submit(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = "timedBridge",
            XCm = Center(x0, cell),
            YCm = Center(y, cell),
            SizeCm = cell,
            ToXCm = Center(x1, cell),
            ToYCm = Center(y, cell),
        });
        Assert.That(session.LastPlacementReject, Is.EqualTo(CrowdPlacementReject.None));
        Assert.That(session.Structures.EntityIds.Count, Is.EqualTo(beforeTimed + 1));
        int timedId = session.Structures.EntityIds[^1];
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);
        ran = StepUntil(session, checksums, ran + 40, sim => !sim.Structures!.EntityIds.Contains(timedId));
        Assert.That(session.Structures.EntityIds.Contains(timedId), Is.False, "限时桥应在寿命到点后离场");
        CrowdSimulationValidator.ValidateStructures(session);
        CrowdSimulationValidator.ValidateNav(session);

        using var second = RiverWorld.Open();
        var replay = new CrowdCommand[session.Commands.Log.Count];
        for (int i = 0; i < replay.Length; i++)
        {
            var entry = session.Commands.Log[i];
            replay[i] = new CrowdCommand(entry.Tick, entry.Cmd);
        }

        second.Session.Commands.Schedule(second.Session, replay);
        var again = new List<ulong>();
        second.Session.Advance(checksums.Count, again);
        Assert.That(again, Is.EqualTo(checksums).AsCollection, "同一份放桥脚本回放两次，校验码应相同");
    }

    [Test]
    public void FuzzPlaceAndRemoveBridge_KeepsInvariants()
    {
        using var world = RiverWorld.Open();
        var place = new CrowdFuzzOp { Id = "placePath", Structure = true };
        var remove = new CrowdFuzzOp { Id = "removePath", Structure = true };
        var spawn = new CrowdFuzzOp { Id = "spawn" };
        var result = CrowdFuzzRunner.Run(world.Session, new CrowdFuzzPlan
        {
            Seed = 11,
            OpCount = 8,
            StepsPerOp = 2,
            ProbeHandleCount = 4,
            SpawnCount = 1,
            PlaceTemplate = "building",
            PlaceSizeCm = world.Cell,
            OrderShape = "box",
            OrderMemberCap = 1,
            StallGuard = 64,
            BoomRadiusCm = world.Cell,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
            PathTemplate = "bridge",
            PathWidthCm = world.Cell,
            PathLengthCm = world.Cell * 4,
            Ops = new[] { spawn, place, remove },
        });
        Assert.That(result.Passed, Is.True, $"step {result.Step} op {result.OpId}: {result.Error}");
    }

    private static void Submit(CrowdSimSession session, CrowdSimCommand command) =>
        session.Commands.Submit<object?>(session, command, CrowdSimCommands.Exec);

    private static int StepUntil(CrowdSimSession session, List<ulong> checksums, int limit, Func<CrowdSimSession, bool> done)
    {
        int guard = (limit + 1) * CrowdSimulationLimits.AdvanceStallGuardFactor;
        while (checksums.Count < limit)
        {
            if (guard-- <= 0) throw new InvalidOperationException("会话推进停摆过长:路径服务答复未到。");
            if (!session.Step(out ulong checksum)) continue;
            checksums.Add(checksum);
            if (done(session)) break;
        }

        return checksums.Count;
    }

    private static bool OnDeck(CrowdSimSession session)
    {
        for (int i = 0; i < session.Units.Count; i++)
        {
            if (LevelOf(session, i) != 0) return true;
        }

        return false;
    }

    private static byte LevelOf(CrowdSimSession session, int dense)
    {
        var entity = session.Units.EntityAt(dense);
        return session.World.Get<CrowdSimulationUnitState>(entity).Level;
    }

    private static int CellOf(CrowdSimSession session, int dense)
    {
        var entity = session.Units.EntityAt(dense);
        var pos = session.World.Get<Ludots.Core.Components.WorldPositionCm>(entity).Value;
        return CrowdSimulationSpace.CellAtStrict(pos.X, pos.Y, session.Config.NavCellSizeCm, session.Config.NavCellsPerSide);
    }

    private static string Describe(CrowdSimSession session, NavContext nav, int row)
    {
        int side = nav.NavCellsPerSide;
        int cell = session.Units.Count == 0 ? -1 : CellOf(session, 0);
        int up = 0;
        for (int x = 0; x < side; x++)
        {
            if (nav.UpPass[row * side + x] != 0) up++;
        }

        return $"单位格 {cell} 层 {(session.Units.Count == 0 ? -1 : LevelOf(session, 0))} 桥面可走 {up} tick {session.TickCount}";
    }

    private static int Center(int index, int size) => index * size + size / 2;

    private sealed class RiverWorld : IDisposable
    {
        public required CrowdSimulationRuntimeConfig Config { get; init; }
        public required CrowdSimSession Session { get; init; }
        public required int Side { get; init; }
        public required int Cell { get; init; }
        public required int UnitType { get; init; }
        public required int Layer { get; init; }
        public required int RadiusIndex { get; init; }
        private readonly PathQueryService _service;

        private RiverWorld(PathQueryService service) => _service = service;

        public static RiverWorld Open()
        {
            CrowdTestScheduler.Ensure();
            var map = TestDefaults.DemoMap();
            const int boardCm = 100_000;
            map.Boards[0] = new BoardConfig { WidthCm = boardCm, HeightCm = boardCm };
            var config = TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules());
            int side = config.NavCellsPerSide;
            int cell = config.NavCellSizeCm;
            if (side != 16 || cell != 6250)
            {
                throw new InvalidOperationException($"这条河按 16×16、格距 6250 厘米来画，实际是 {side} 格、{cell} 厘米。");
            }

            int land = IndexOf(config.TerrainTypeIds, "land");
            int water = IndexOf(config.TerrainTypeIds, "water");
            int n2 = side * side;
            var terrain = new byte[n2];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    bool dry = x <= 2 || x >= 13;
                    terrain[y * side + x] = (byte)(dry ? land : water);
                }
            }

            var surface = SurfaceGrid.Build(config, new NavSurfaceAsset
            {
                NavCellsX = side,
                NavCellsY = side,
                NavCellSizeCm = cell,
                TerrainTypeIds = CopyIds(config.TerrainTypeIds),
                LinkKindIds = TestDefaults.LinkKindIds(config),
                BakeInputsDigest = 0,
                TerrainCells = terrain,
                LinkCandidates = Array.Empty<NavSurfaceLinkCandidate>(),
            }, Array.Empty<BlockerFootprint>());
            var deck = UpperLayerBake.RasterizeLayeredPaths(Array.Empty<LayeredPathRecord>(), config);
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
            var navs = new Dictionary<int, NavContext>();
            var navBy = new Dictionary<(int, int), NavContext>();
            var radii = CrowdDeployment.DistinctRadiusClasses(config);
            var seen = new HashSet<int>();
            for (int a = 0; a < config.AgentTypes.Count; a++)
            {
                for (int r = 0; r < radii.Count; r++)
                {
                    RuntimeAgentProfile? profile = null;
                    for (int i = 0; i < config.Profiles.Count; i++)
                    {
                        var candidate = config.Profiles[i];
                        if (candidate.AgentTypeIndex == a && (int)candidate.RadiusCm.ToInt() == radii[r])
                        {
                            profile = candidate;
                            break;
                        }
                    }

                    if (profile == null) continue;
                    var nav = NavContextBaker.Bake(config, surface, heights, deck, a, profile.ClearanceNavCells, cache);
                    if (seen.Add(nav.Id)) navs[nav.Id] = nav;
                    navBy[(a, r)] = navs[nav.Id];
                }
            }

            int deckArea = IndexOfArea(config, "deck");
            int unitType = -1;
            int layer = -1;
            int radiusIndex = -1;
            for (int t = 0; t < config.UnitTypes.Count && unitType < 0; t++)
            {
                int agent = config.UnitTypes[t].AgentTypeIndex;
                var row = config.AgentTypes[agent].CostByArea;
                if (deckArea >= row.Length || row[deckArea] <= Fix64.Zero) continue;
                for (int r = 0; r < radii.Count; r++)
                {
                    if (!navBy.TryGetValue((agent, r), out var nav)) continue;
                    if (nav.ClearanceNavCells != 1) continue;
                    unitType = t;
                    layer = agent;
                    radiusIndex = r;
                    break;
                }
            }

            if (unitType < 0)
            {
                throw new InvalidOperationException("没有净空 1 且给桥面定了价的兵种。");
            }

            var store = CrowdStructuresStore.Build(config, surface, Array.Empty<BlockerFootprint>());
            store.UseUpperLayer(deck);
            var world = ArchWorld.Create();
            var session = new CrowdSimSession(config, world, navs, navBy, Wiring(config, world));
            session.Structures = store;
            session.RebakeSources = new CrowdRebakeSources(config, heights, deck, surface.LinkCandidates);
            session.NavTileCache = cache;
            session.BindVision(new FogLayerRegistry(), new FogFieldStore());
            var service = new PathQueryService(navs, config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
            session.BlockOnDueReplies = true;
            session.Presentation.TruthNavFrozen = true;
            session.VerifyIncrementalNav = true;
            return new RiverWorld(service)
            {
                Config = config,
                Session = session,
                Side = side,
                Cell = cell,
                UnitType = unitType,
                Layer = layer,
                RadiusIndex = radiusIndex,
            };
        }

        public void Dispose() => _service.Dispose();

        private static CrowdSimPresentationWiring Wiring(CrowdSimulationRuntimeConfig config, ArchWorld world)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine("assets", "crowddemo"));
            var pipeline = new ConfigPipeline(vfs, null!);
            var mapLoader = new MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
            mapLoader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
            var collections = new EntityCollectionStore(new StringIntRegistry());
            return new CrowdSimPresentationWiring
            {
                StableIds = new PresentationStableIdAllocator(),
                TemplateRegistry = mapLoader.TemplateRegistry,
                TemplateKeys = mapLoader.EntityTemplateKeys,
                TemplatesByUnitTypeRadius = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
                    config, mapLoader.TemplateRegistry, mapLoader.EntityTemplateKeys, TestDefaults.DemoProfiles(), "动态桥面测试"),
                RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                    Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusKey),
                SelectedCollectionKeyId = collections.KeyRegistry.Register("dynamic-deck-selected"),
                Collections = collections,
            };
        }

        private static int IndexOfArea(CrowdSimulationRuntimeConfig config, string id)
        {
            for (int i = 0; i < config.NavAreas.Count; i++)
            {
                if (config.NavAreas[i].Id == id) return i;
            }

            throw new InvalidOperationException($"没有区域 {id}");
        }

        private static int IndexOf(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return i;
            }

            throw new InvalidOperationException($"没有地形 {id}");
        }

        private static string[] CopyIds(IReadOnlyList<string> ids)
        {
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) copy[i] = ids[i];
            return copy;
        }
    }
}
