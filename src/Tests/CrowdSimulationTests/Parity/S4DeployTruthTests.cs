using System;
using System.Collections.Generic;
using Arch.Core;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Registry;
using Ludots.Core.Systems;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// S4 对拍:同一份指令脚本(spawn 12000 → 框选 → 点名生成 → 全选 → 清空),
/// 逐帧规范校验码(Fix64 原始值口径)与 CrowdSimulation 沙盒一致;
/// 部署的单位逐条(玩家 / 模板 / 半径级 / 组 / 位置原始值)一致;
/// 同一份记录回放两次,每帧校验码相同。
/// </summary>
[TestFixture]
public class S4DeployTruthTests
{
    public static readonly string[] Seeds = { "s1337", "s2024", "s7" };

    [Test]
    public void DeployAndChecksums_MatchWebTruth([ValueSource(nameof(Seeds))] string seed)
    {
        var (runtime, session) = BuildSession(seed);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s4-deploy-truth.json")))!;
        var frames = truth["frames"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray();
        var script = CrowdReferenceScript.Load(truth["script"]!.AsArray(), session.Units.Capacity);
        for (int t = 0; t < frames.Length; t++)
        {
            Assert.That(script.Step(session, out var hash), Is.True, $"{seed} 第 {t + 1} 帧停摆");
            Assert.That(CrowdSimChecksum.Format(hash), Is.EqualTo(frames[t]), $"{seed} 第 {t + 1} 帧校验码不一致");
        }

        // 单位转储逐条比对
        var dump = File.ReadAllBytes(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s4-units.bin"));
        int cursor = 8; // LS4U + count
        int count = BitConverter.ToInt32(dump, 4);
        Assert.That(session.Units.Count, Is.EqualTo(count), $"{seed} 单位总数");
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(runtime);
        for (int i = 0; i < count; i++)
        {
            int player = ReadI32(dump, ref cursor);
            int unitType = ReadI32(dump, ref cursor);
            int rIdx = ReadI32(dump, ref cursor);
            int group = ReadI32(dump, ref cursor);
            double xm = BitConverter.ToDouble(dump, cursor); cursor += 8;
            double ym = BitConverter.ToDouble(dump, cursor); cursor += 8;

            var entity = session.Units.EntityAt(i);
            Assert.That(session.World.Get<PlayerOwner>(entity).PlayerId, Is.EqualTo(player), $"{seed} 单位 {i} 玩家");
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            Assert.That(state.GroupId, Is.EqualTo(group), $"{seed} 单位 {i} 组");
            var agent = session.World.Get<CrowdSimulationAgent>(entity);
            var profile = CrowdDeployment.ProfileOf(runtime, runtime.UnitTypes[unitType].AgentTypeIndex, rIdx);
            int profileIndex = CrowdDeployment.ProfileIndexOf(runtime, runtime.UnitTypes[unitType].AgentTypeIndex, rIdx);
            Assert.That(agent.ProfileIndex, Is.EqualTo(profileIndex), $"{seed} 单位 {i} 档案下标");
            Assert.That(agent.AgentTypeIndex, Is.EqualTo(profile.AgentTypeIndex), $"{seed} 单位 {i} 移动类型下标");
            Assert.That(runtime.Profiles[agent.ProfileIndex].Id, Is.EqualTo(profile.Id), $"{seed} 单位 {i} 体型");
            var pos = session.World.Get<WorldPositionCm>(entity).Value;
            Assert.That(pos.X.RawValue, Is.EqualTo(CanonRaw(xm)), $"{seed} 单位 {i} X 原始值");
            Assert.That(pos.Y.RawValue, Is.EqualTo(CanonRaw(ym)), $"{seed} 单位 {i} Y 原始值");
        }
    }

    [Test]
    public void Replay_TwiceProducesIdenticalChecksums([ValueSource(nameof(Seeds))] string seed)
    {
        var (runtime, session) = BuildSession(seed);
        var truth = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "CrowdSimulation", "parity", "s4-deploy-truth.json")))!;
        var frames = truth["frames"]!.AsArray().Select(f => f!.GetValue<string>()).ToArray();
        var script = CrowdReferenceScript.Load(truth["script"]!.AsArray(), session.Units.Capacity);
        var first = new List<ulong>();
        script.Advance(session, frames.Length, first);
        var log = session.Commands.Log.Select(e => new CrowdCommand(e.Tick, e.Cmd)).ToArray();
        Assert.That(log.Any(e => e.Cmd.Kind is CrowdSimCommandKind.Order && (e.Cmd.Members == null || e.Cmd.Members.Length == 0)), Is.False);

        // 同一份译后日志回放:全新会话,不再带选中记录
        var (_, session2) = BuildSession(seed);
        session2.Commands.Schedule(session2, log);
        var second = new List<ulong>();
        session2.Advance(frames.Length, second);

        Assert.That(second, Is.EqualTo(first).AsCollection, $"{seed} 回放与首跑校验码不一致");
        Assert.That(second.Select(CrowdSimChecksum.Format), Is.EqualTo(frames).AsCollection, $"{seed} 回放与沙盒校验码不一致");
    }

    [Test]
    public void Clear_ExcludesUnitsImmediately_AndRequestsLifecycleDestroy()
    {
        var (_, session) = BuildSession("s1337");
        int made = CrowdDeployment.Spawn(session, 1);
        Assert.That(made, Is.EqualTo(1));
        var entity = session.Units.EntityAt(0);
        Assert.That(session.World.IsAlive(entity), Is.True);
        session.Units.Clear();
        Assert.That(session.Units.Count, Is.EqualTo(0));
        Assert.That(session.World.IsAlive(entity), Is.False);
        Assert.That(session.World.Size, Is.EqualTo(0));
        CrowdMovementKernel.Create(session).GatherUnits();
    }

    [Test]
    public void Clear_HeadlessWaves_KeepLiveEntityCountAtLastWave()
    {
        var (_, session) = BuildSession("s1337");
        int[] waves = { 40, 40, 25 };
        foreach (int count in waves)
        {
            session.Units.Clear();
            int made = CrowdDeployment.Spawn(session, count);
            Assert.That(made, Is.EqualTo(count));
            Assert.That(session.Units.Count, Is.EqualTo(count));
            Assert.That(session.World.Size, Is.EqualTo(count));
        }
    }

    [Test]
    public void Deploy_ChunkOrderMatchesDenseIndex()
    {
        var (_, session) = BuildSession("s1337");
        int made = CrowdDeployment.Spawn(session, 200);
        Assert.That(made, Is.EqualTo(200));
        var query = new QueryDescription()
            .WithAll<CrowdSimulationUnitState, CrowdSimulationKinematics, WorldPositionCm, CrowdSimulationAgent, PlayerOwner>()
            .WithNone<PresentationDestroyPending>();
        int expect = 0;
        foreach (ref var chunk in session.World.Query(in query))
        {
            var states = chunk.GetSpan<CrowdSimulationUnitState>();
            foreach (int index in chunk)
            {
                int dense = session.Units.DenseOfSlot(states[index].Slot);
                Assert.That(dense, Is.EqualTo(expect));
                expect++;
            }
        }

        Assert.That(expect, Is.EqualTo(made));
    }

    [Test]
    public void Deploy_ChunkOrderRejectsArchetypeThatIsNotTheDenseSet()
    {
        var (_, session) = BuildSession("s1337");
        Assert.That(CrowdDeployment.Spawn(session, 2), Is.EqualTo(2));
        Archetype archetype = session.World.GetArchetype(session.Units.EntityAt(0));
        session.World.Create(archetype.Signature);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CrowdDeployment.Spawn(session, 2))!;
        Assert.That(ex.Message, Does.Contain("must contain exactly the dense set"));
    }

    [Test]
    public void Deploy_UsesTemplateBatchCreate()
    {
        var (_, session) = BuildSession("s1337");
        int made = CrowdDeployment.Spawn(session, 200);
        Assert.That(made, Is.EqualTo(200));
        Assert.That(session.Units.LargestSpawnBatch, Is.GreaterThan(1));
    }

    private static long CanonRaw(double meters) => (long)(meters * 100 * 4294967296.0);

    private static int ReadI32(byte[] buffer, ref int cursor)
    {
        int v = BitConverter.ToInt32(buffer, cursor);
        cursor += 4;
        return v;
    }

    internal static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(string seed, bool lineOfSight = false)
        => BuildSession(seed, lineOfSight, pairRules: null);

    internal static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(string seed, CrowdPairRules pairRules)
        => BuildSession(seed, lineOfSight: false, pairRules);

    internal static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(string seed, NavSurfaceAsset surface)
        => BuildSession(seed, lineOfSight: false, pairRules: null, surface);

    internal static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(string seed, Action<JsonObject> tuneConfig)
        => BuildSession(seed, lineOfSight: false, pairRules: null, S1SurfaceTruthTests.ReadSurface(seed), tuneConfig);

    internal static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(
        string seed, Action<JsonObject>? tuneConfig, CrowdTestBodies bodies)
        => BuildSession(seed, lineOfSight: false, pairRules: null, S1SurfaceTruthTests.ReadSurface(seed), tuneConfig, bodies);

    private static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(
        string seed, bool lineOfSight, CrowdPairRules? pairRules)
        => BuildSession(seed, lineOfSight, pairRules, S1SurfaceTruthTests.ReadSurface(seed));

    private static (CrowdSimulationRuntimeConfig Runtime, CrowdSimSession Session) BuildSession(
        string seed, bool lineOfSight, CrowdPairRules? pairRules, NavSurfaceAsset surface,
        Action<JsonObject>? tuneConfig = null, CrowdTestBodies? bodies = null)
    {
        CrowdTestScheduler.Ensure();
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed, lineOfSight, pairRules, tuneConfig, bodies);
        var mapSurface = S1SurfaceTruthTests.LoadMapSurface(seed);
        var map = JsonNode.Parse(File.ReadAllText(Path.Combine(S1Dir(seed), "Maps", $"crowd_simulation_{seed}.json")))!;
        int boardWidthCm = map["Boards"]![0]!["WidthCm"]!.GetValue<int>();
        int boardHeightCm = map["Boards"]![0]!["HeightCm"]!.GetValue<int>();
        string heightAssetPath = map["ContinuousHeightmap"]!["Asset"]!.GetValue<string>();
        var heightAsset = ContinuousHeightmapBinary.Read(File.OpenRead(
            Path.Combine(S1Dir(seed), heightAssetPath["assets/".Length..])));
        var ingested = CrowdSimulationWorldIngest.Build(
            runtime,
            boardWidthCm,
            boardHeightCm,
            surface,
            "assets/" + runtime.SurfaceAsset,
            heightAsset,
            heightAssetPath,
            mapSurface.Blockers);
        var grid = ingested.Grid;
        var heights = ingested.Heights;
        var deck = UpperLayerBake.RasterizeDecks(mapSurface.Bridges, runtime);
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
            foreach (var clearance in runtime.Profiles.Where(p => p.AgentTypeIndex == a).Select(p => p.ClearanceNavCells).Distinct())
            {
                var nav = NavContextBaker.Bake(runtime, grid, heights, deck, a, clearance, cache);
                if (!seen.Add(nav.Id)) continue;
                navs[nav.Id] = nav;
            }
        }

        foreach (var profile in runtime.Profiles)
        {
            int rIdx = CrowdDeployment.IndexOfRadiusClass(radiusClasses, (int)profile.RadiusCm.ToInt());
            navByLayerRadius[(profile.AgentTypeIndex, rIdx)] = navs[profile.NavContextId];
        }

        var world = ArchWorld.Create();
        var session = new CrowdSimSession(runtime, world, navs, navByLayerRadius, BuildWiring(runtime, world, bodies?.Profiles ?? TestDefaults.DemoProfiles()));
        // S7 结构动态化:仓(静态阻挡物入仓)+ 增量重烘源 + tile 缓存挂会话。
        // S4/S5 脚本没有结构指令,这些挂载不改变既有语义(Step 只在结构 op 时走新路径)。
        session.Structures = Ludots.Core.CrowdSimulation.Structures.CrowdStructuresStore.Build(runtime, grid, mapSurface.Blockers);
        session.Structures.UseDeck(deck);
        session.Structures.InstallMapBridges(mapSurface.Bridges);

        session.RebakeSources = new CrowdRebakeSources(runtime, heights, deck, surface.JumpCandidates);
        session.NavTileCache = cache;
        session.BindVision(new FogLayerRegistry(), new FogFieldStore());
        return (runtime, session);
    }

    /// <summary>单位创建单路径的无头装配:模板注册表与模板键走引擎同一装载链
    /// (VFS 挂载 CrowdSimulationDemoContentMod 资产 → ConfigPipeline → MapLoader.LoadTemplates),
    /// 单位实例化与真机同走 EntityBuilder.UseTemplate→Build;选中属主置空,不镜像。</summary>
    private static CrowdSimPresentationWiring BuildWiring(
        CrowdSimulationRuntimeConfig runtime, ArchWorld world, Ludots.Core.Navigation.AgentProfiles.AgentProfileRegistry profiles)
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine("assets", "crowddemo"));
        var pipeline = new ConfigPipeline(vfs, modLoader: null!);
        var mapLoader = new MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
        mapLoader.LoadTemplates(ConfigCatalogLoader.Load(pipeline));
        var collections = new EntityCollectionStore(new StringIntRegistry());
        return new CrowdSimPresentationWiring
        {
            StableIds = new PresentationStableIdAllocator(),
            TemplateRegistry = mapLoader.TemplateRegistry,
            TemplateKeys = mapLoader.EntityTemplateKeys,
            TemplatesByUnitTypeRadius = CrowdSimPresentationWiring.BuildUnitTypeTemplates(
                runtime, mapLoader.TemplateRegistry, mapLoader.EntityTemplateKeys, profiles, "测试装配(CrowdSimulationTests)"),
            RadiusMetersBlackboardKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(
                Ludots.Core.CrowdSimulation.Runtime.CrowdSimulationRuntime.Keys.UnitRadiusKey),
            SelectedCollectionKeyId = collections.KeyRegistry.Register(CrowdSimPresentationWiring.SelectedCollectionKey),
            Collections = collections,
        };
    }

    private static string S1Dir(string seed) => Path.Combine("assets", "s1", seed);
}
