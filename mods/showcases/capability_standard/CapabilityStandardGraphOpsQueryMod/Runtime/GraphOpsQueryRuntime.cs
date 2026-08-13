using System;
using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsQueryMod.Runtime;

public sealed class GraphOpsQueryRuntime : IDisposable
{
    public const int EnemyTeamId = 2;
    public const int AllyTeamId = 1;
    public const int SeededMapEntityCount = 12;
    public const string SquadCollectionKey = GraphOpsQueryCatalogBootstrap.SquadCollectionKey;

    private readonly GraphShowcaseConfig _config = new();
    private GraphOpsQueryShowcaseBundle? _bundle;
    private GraphProgramRegistry? _programs;
    private GraphOpsStageVisuals? _stage;
    private GameEngine? _engine;
    private GraphOpsEngineWorld? _session;
    private bool _visualsSpawned;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private EntitySetQueryRuntime? _entityQueries;
    private EntityCollectionStore? _collections;
    private Entity _caster;
    private Entity[] _units = Array.Empty<Entity>();
    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private float _accum;
    private int _allCount;
    private int _rangeCount;
    private float _sumHp;
    private float _avgHp;
    private float _maxHp;
    private float _minHp;
    private int _squadCount;
    private string _strongestLabel = "-";
    private string _weakestLabel = "-";
    private int _strongestIndex = -1;
    private int _weakestIndex = -1;

    public float CasterX => 0f;
    public float CasterY => -2.2f;
    public int UnitCount => _units.Length;
    public float[] UnitX { get; private set; } = Array.Empty<float>();
    public float[] UnitY { get; private set; } = Array.Empty<float>();
    public float[] UnitHp { get; private set; } = Array.Empty<float>();
    public byte[] UnitEnemy { get; private set; } = Array.Empty<byte>();
    public byte[] UnitDead { get; private set; } = Array.Empty<byte>();
    public byte[] UnitInRange { get; private set; } = Array.Empty<byte>();
    public int AllCount => _allCount;
    public int RangeCount => _rangeCount;
    public float SumHp => _sumHp;
    public float AvgHp => _avgHp;
    public float MaxHp => _maxHp;
    public float MinHp => _minHp;
    public int SquadCount => _squadCount;
    public string StrongestLabel => _strongestLabel;
    public string WeakestLabel => _weakestLabel;
    public int StrongestIndex => _strongestIndex;
    public int WeakestIndex => _weakestIndex;
    public int CompiledGraphs { get; private set; }
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_query" };

    public void AttachEngine(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public void BindStandaloneFromModAssets()
    {
        _bundle = GraphOpsQueryCatalogBootstrap.LoadStandalone();
        _programs = _bundle.Programs;
        _collections = _bundle.Collections;
        CompiledGraphs = 2;
    }

    public void BindStageVisuals(GraphOpsStageVisuals stage)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _bundle == null || _collections == null)
        {
            throw new InvalidOperationException(
                "GraphOpsQueryRuntime.BindStandaloneFromModAssets() required before EnsureWorld.");
        }

        _session = GraphOpsEngineWorld.AttachOrCreate(_engine, AppContext.BaseDirectory);
        _world = _session.World;
        var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
        var relationships = new RelationshipRuntime(
            _world,
            new RelationshipTypeRegistry(),
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(_world));
        _entityQueries = new EntitySetQueryRuntime(_world, tagOps, relationships);
        _api = new GasGraphRuntimeApi(
            _world,
            tagOps: tagOps,
            relationshipRuntime: relationships,
            entityQueries: _entityQueries,
            entityCollections: _collections);

        _caster = _world.Create();
        _units = new Entity[SeededMapEntityCount];
        UnitX = new float[SeededMapEntityCount];
        UnitY = new float[SeededMapEntityCount];
        UnitHp = new float[SeededMapEntityCount];
        UnitEnemy = new byte[SeededMapEntityCount];
        UnitDead = new byte[SeededMapEntityCount];
        UnitInRange = new byte[SeededMapEntityCount];

        SeedUnit(0, EnemyTeamId, _bundle.SoldierTemplateId, 90f, enemy: true, dead: false);
        SeedUnit(1, EnemyTeamId, _bundle.SoldierTemplateId, 70f, enemy: true, dead: false);
        SeedUnit(2, EnemyTeamId, _bundle.SoldierTemplateId, 50f, enemy: true, dead: false);
        SeedUnit(3, EnemyTeamId, _bundle.SoldierTemplateId, 30f, enemy: true, dead: false);
        SeedUnit(4, EnemyTeamId, _bundle.SoldierTemplateId, 10f, enemy: true, dead: false);
        SeedUnit(5, EnemyTeamId, _bundle.SoldierTemplateId, 100f, enemy: true, dead: false);
        SeedUnit(6, EnemyTeamId, _bundle.SoldierTemplateId, 0f, enemy: true, dead: false);
        SeedUnit(7, EnemyTeamId, _bundle.SoldierTemplateId, 150f, enemy: true, dead: false);
        SeedUnit(8, EnemyTeamId, _bundle.SoldierTemplateId, 40f, enemy: true, dead: true);
        SeedUnit(9, EnemyTeamId, _bundle.ScoutTemplateId, 80f, enemy: false, dead: false);
        SeedUnit(10, AllyTeamId, _bundle.SoldierTemplateId, 60f, enemy: false, dead: false);
        SeedUnit(11, AllyTeamId, _bundle.ScoutTemplateId, 20f, enemy: false, dead: false);

        var roster = new Entity[] { _units[0], _units[1], _units[2], _units[9], _units[10], _units[11] };
        var descriptor = EntityCollectionDescriptor.Create(
            SquadCollectionKey,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.Display,
            contextEntity: _caster,
            primaryEntity: _caster,
            title: "花名册",
            summary: "小队成员");
        _collections.Replace(_caster, descriptor, roster, _caster);

        Metrics.AgentCount = SeededMapEntityCount;
        Metrics.Detail = "全图搜人：花名册与敌军生命档案已就位。";
        SpawnStageVisuals();
    }

    private void SpawnStageVisuals()
    {
        if (_stage == null || _visualsSpawned)
        {
            return;
        }

        _stage.BindMapEntity(
            _caster,
            GraphOpsVisualTemplates.Caster,
            "指挥",
            CasterX,
            CasterY,
            100f,
            100f,
            bindAsViewer: true);
        for (int i = 0; i < _units.Length; i++)
        {
            bool scout = i == 9 || i == 11;
            bool ally = i == 10 || i == 11;
            string template = scout
                ? GraphOpsVisualTemplates.Scout
                : ally
                    ? GraphOpsVisualTemplates.Ally
                    : GraphOpsVisualTemplates.Soldier;
            string name = ally ? $"友军{i + 1}" : $"敌军{i + 1}";
            _stage.BindMapEntity(_units[i], template, name, UnitX[i], UnitY[i], UnitHp[i], 150f, bindAsViewer: false);
        }

        _visualsSpawned = true;
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        SpawnStageVisuals();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var sw = Stopwatch.StartNew();
        RunFilterPipeline();
        RunFromCollection();
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"全图搜到{_allCount}人 → 敌阵营且带敌军标签、排除阵亡 → 生命区间剩{_rangeCount}人 → 按生命排序 → " +
            $"总和{_sumHp:F0}/均值{_avgHp:F0}，最强{_strongestLabel}（生命{_maxHp:F0}）/最弱{_weakestLabel}（生命{_minHp:F0}）；" +
            $"花名册模板筛出{_squadCount}人";
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        _world = null;
    }

    private void RunFilterPipeline()
    {
        int rangedCount = ExecuteGraph(GraphOpsQueryCatalogBootstrap.FilterPipelineGraph, _caster);
        Span<Entity> scratch = stackalloc Entity[GraphVmLimits.MaxTargets];
        _allCount = _entityQueries!.CollectMapEntities(scratch);
        _rangeCount = rangedCount;
        ReadOnlySpan<Entity> ranged = _targets.AsSpan(0, rangedCount);
        _sumHp = _entityQueries.SumAttribute(ranged, _bundle!.HealthAttrId);
        _avgHp = _entityQueries.AverageAttribute(ranged, _bundle.HealthAttrId);
        _maxHp = _entityQueries.MaxAttribute(ranged, _bundle.HealthAttrId);
        _minHp = _entityQueries.MinAttribute(ranged, _bundle.HealthAttrId);
        _entityQueries.TryMaxEntityByAttribute(ranged, _bundle.HealthAttrId, out Entity strongest, out _);
        _entityQueries.TryMinEntityByAttribute(ranged, _bundle.HealthAttrId, out Entity weakest, out _);
        _strongestIndex = IndexOf(strongest);
        _weakestIndex = IndexOf(weakest);
        _strongestLabel = UnitLabel(_strongestIndex);
        _weakestLabel = UnitLabel(_weakestIndex);

        for (int i = 0; i < _units.Length; i++)
        {
            UnitInRange[i] = 0;
        }

        for (int i = 0; i < ranged.Length; i++)
        {
            int idx = IndexOf(ranged[i]);
            if (idx >= 0)
            {
                UnitInRange[idx] = 1;
            }
        }
    }

    private void RunFromCollection()
    {
        _squadCount = ExecuteGraph(GraphOpsQueryCatalogBootstrap.FromCollectionGraph, _caster);
    }

    private int ExecuteGraph(string graphKey, Entity caster)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"Query gallery graph '{graphKey}' missing from Registry.");
        }

        Array.Clear(_floats, 0, _floats.Length);
        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_entities, 0, _entities.Length);
        Array.Clear(_targets, 0, _targets.Length);
        _entities[0] = caster;
        var targetList = new GraphTargetList(_targets);
        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = caster,
            Api = _api!,
            F = _floats,
            I = _ints,
            B = _bools,
            E = _entities,
            Targets = _targets,
            TargetList = targetList,
            CallStack = _callStack,
            CallStackCount = 0,
        };
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        return state.TargetList.Count;
    }

    private void SeedUnit(int index, int teamId, int templateId, float health, bool enemy, bool dead)
    {
        int col = index % 6;
        int row = index / 6;
        float x = (col - 2.5f) * 2.2f;
        float y = 1.2f + row * 2.8f;
        int xCm = (int)MathF.Round(x * 100f);
        int yCm = (int)MathF.Round(y * 100f);
        Entity entity = _world!.Create(
            new MapEntity(),
            new Team { Id = teamId },
            new EntityTemplateKeyRef { TemplateKeyId = templateId },
            new AttributeBuffer(),
            new GameplayTagContainer(),
            WorldPositionCm.FromCm(xCm, yCm));
        ref AttributeBuffer attrs = ref _world.Get<AttributeBuffer>(entity);
        attrs.SetBase(_bundle!.HealthAttrId, health);
        ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
        if (enemy)
        {
            tags.AddTag(_bundle.EnemyTagId);
        }

        if (dead)
        {
            tags.AddTag(_bundle.DeadTagId);
        }

        _units[index] = entity;
        UnitX[index] = x;
        UnitY[index] = y;
        UnitHp[index] = health;
        UnitEnemy[index] = enemy ? (byte)1 : (byte)0;
        UnitDead[index] = dead ? (byte)1 : (byte)0;
    }

    private int IndexOf(Entity entity)
    {
        for (int i = 0; i < _units.Length; i++)
        {
            if (_units[i] == entity)
            {
                return i;
            }
        }

        return -1;
    }

    private static string UnitLabel(int index) => index < 0 ? "无" : $"敌军{index + 1}";
}
