using System;
using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsSpatialMod.Runtime;

public sealed class GraphOpsSpatialRuntime : IDisposable
{
    private const int CasterTeamId = 1;
    private const int EnemyTeamId = 2;
    private const uint CasterLayer = 0b0001;
    private const uint EnemyLayer = 0b0010;
    private const string HitCountKey = "spatial.showcase.hitCount";
    private const string NearestKey = "spatial.showcase.nearest";
    private const string FirstKey = "spatial.showcase.first";
    private const string ConeGraph = "Graph.SpatialShowcase.Cone";
    private const string RectGraph = "Graph.SpatialShowcase.Rectangle";
    private const string LineGraph = "Graph.SpatialShowcase.Line";
    private const string HexRangeGraph = "Graph.SpatialShowcase.HexRange";
    private const string HexRingGraph = "Graph.SpatialShowcase.HexRing";
    private const string HexNeighborsGraph = "Graph.SpatialShowcase.HexNeighbors";

    public const string CaptionTitle = "空间圈人与六角圈人";

    private static readonly string[] FeaturedGraphs =
    {
        ConeGraph, RectGraph, LineGraph, HexRangeGraph, HexRingGraph, HexNeighborsGraph
    };

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphFunctionCatalog? _catalog;
    private World? _world;
    private int _hitCountKeyId;
    private int _nearestKeyId;
    private int _firstKeyId;
    private SpatialQueryService? _spatial;
    private SpatialCoordinateConverter? _coords;
    private GridSpatialPartitionWorld? _grid;
    private GasGraphRuntimeApi? _api;
    private EntitySetQueryRuntime? _entityQueries;
    private TagOps? _tagOps;
    private RelationshipRuntime? _relationships;
    private Entity _caster;
    private float _accum;
    private int _castWave;
    private int _lastHitIndex = -1;
    private int _coneHits;
    private int _rectHits;
    private int _lineHits;
    private int _hexRangeHits;
    private int _hexRingHits;
    private int _hexNeighborHits;
    private bool _showNearest;
    private bool _showFirst;
    private float _casterXM;
    private float _casterYM;
    private float[] _tx = Array.Empty<float>();
    private float[] _ty = Array.Empty<float>();
    private byte[] _flash = Array.Empty<byte>();
    private Entity[] _targets = Array.Empty<Entity>();

    public float CasterX => _casterXM;
    public float CasterY => _casterYM;
    public float[] TargetX => _tx;
    public float[] TargetY => _ty;
    public byte[] Flash => _flash;
    public int TargetCount => _tx.Length;
    public int LastHitIndex => _lastHitIndex;
    public int ConeHits => _coneHits;
    public int RectHits => _rectHits;
    public int LineHits => _lineHits;
    public int HexRangeHits => _hexRangeHits;
    public int HexRingHits => _hexRingHits;
    public int HexNeighborHits => _hexNeighborHits;
    public bool HasFirstHit => _showFirst;
    public bool HasNearest => _showNearest;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_spatial" };

    public void Bind(GraphProgramRegistry programs, GraphFunctionCatalog catalog)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null)
        {
            throw new InvalidOperationException("GraphOpsSpatialRuntime.Bind(Registry, Catalog) required before EnsureWorld.");
        }

        RequireGraph(ConeGraph);
        RequireGraph(RectGraph);
        RequireGraph(LineGraph);
        RequireGraph(HexRangeGraph);
        RequireGraph(HexRingGraph);
        RequireGraph(HexNeighborsGraph);
        RequireTargetListGetOnAllGraphs();
        _hitCountKeyId = ConfigKeyRegistry.GetId(HitCountKey);
        _nearestKeyId = ConfigKeyRegistry.GetId(NearestKey);
        _firstKeyId = ConfigKeyRegistry.GetId(FirstKey);

        TeamManager.SetRelationship(CasterTeamId, EnemyTeamId, TeamRelationship.Hostile);
        TeamManager.SetRelationship(EnemyTeamId, CasterTeamId, TeamRelationship.Hostile);

        _world = World.Create();
        _coords = new SpatialCoordinateConverter(gridCellSizeCm: 100);
        _grid = new GridSpatialPartitionWorld(cellSize: 4);
        _spatial = new SpatialQueryService(new GridSpatialPartitionBackend(_grid, _coords));
        _spatial.SetCoordinateConverter(_coords);
        _spatial.SetPositionProvider(entity =>
        {
            ref WorldPositionCm pos = ref _world!.Get<WorldPositionCm>(entity);
            return pos.Value.ToWorldCmInt2();
        });

        _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        _relationships = new RelationshipRuntime(
            _world,
            new RelationshipTypeRegistry(),
            new RelationshipMetricRegistry(),
            new RelationshipFlagRegistry(),
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(_world));
        _entityQueries = new EntitySetQueryRuntime(_world, _tagOps, _relationships);
        _api = new GasGraphRuntimeApi(
            _world,
            _spatial,
            _coords,
            tagOps: _tagOps,
            relationshipRuntime: _relationships,
            entityQueries: _entityQueries);

        WorldCmInt2 origin = _coords.HexToWorld(new HexCoordinates(0, 0));
        _casterXM = origin.X * 0.01f;
        _casterYM = origin.Y * 0.01f;
        _caster = SpawnCombatant(origin.X, origin.Y, CasterTeamId, CasterLayer);

        int targets = Math.Min(_config.FeaturedAgentCount, 8);
        _tx = new float[targets];
        _ty = new float[targets];
        _flash = new byte[targets];
        _targets = new Entity[targets];

        SpawnEnemy(_coords.HexToWorld(new HexCoordinates(0, 1)), 0);
        SpawnEnemy(_coords.HexToWorld(new HexCoordinates(1, 0)), 1);
        SpawnEnemy(_coords.HexToWorld(new HexCoordinates(2, 0)), 2);
        SpawnEnemy(new WorldCmInt2(origin.X, origin.Y + 450), 3);
        SpawnEnemy(new WorldCmInt2(origin.X + 180, origin.Y + 180), 4);
        SpawnEnemy(new WorldCmInt2(origin.X + 250, origin.Y + 250), 5);
        SpawnEnemy(new WorldCmInt2(origin.X - 900, origin.Y), 6);
        for (int i = 7; i < targets; i++)
        {
            SpawnEnemy(new WorldCmInt2(origin.X + 120 * i, origin.Y + 60), i);
        }

        Metrics.AgentCount = targets;
        Metrics.Detail = "扇形、矩形、直线与六角圈人：排除自己，只打敌对层与敌对关系。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        for (int i = 0; i < _flash.Length; i++)
        {
            if (_flash[i] > 0) _flash[i]--;
        }

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _castWave++;

        int phase = _castWave % 4;
        var sw = Stopwatch.StartNew();
        switch (phase)
        {
            case 0:
                _coneHits = RunSpell(ConeGraph);
                break;
            case 1:
                _rectHits = RunSpell(RectGraph);
                break;
            case 2:
                _lineHits = RunSpell(LineGraph);
                break;
            default:
                _hexRangeHits = RunSpell(HexRangeGraph);
                _hexRingHits = RunSpell(HexRingGraph);
                _hexNeighborHits = RunSpell(HexNeighborsGraph);
                break;
        }

        CapturePresentationFromBlackboard();
        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;

        int crowd = Math.Min(_config.CrowdBandCount, 2000);
        for (int i = 0; i < crowd; i++)
        {
            RunSpell(ConeGraph);
        }

        Metrics.Detail =
            $"扇形命中{_coneHits}人；矩形命中{_rectHits}人；直线命中{_lineHits}人；" +
            $"六角圈人（范围内{_hexRangeHits}/外环{_hexRingHits}/邻格{_hexNeighborHits}）；" +
            "排除自己；只打敌对层；只打敌对关系；" +
            (_showNearest ? "最近目标已锁定" : "最近目标未命中") +
            (_showFirst ? "；名单上第一个命中" : string.Empty);
    }

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
    }

    private int RunSpell(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"Spatial graph '{graphKey}' missing from Registry.");
        }

        WorldCmInt2 originWorld = _world!.Get<WorldPositionCm>(_caster).ToWorldCmInt2();
        var originCm = new IntVector2(originWorld.X, originWorld.Y);
        GraphExecutor.Execute(_world, _caster, Entity.Null, originCm, program, _api!, GraphKind.Effect);

        ref BlackboardIntBuffer ints = ref _world.Get<BlackboardIntBuffer>(_caster);
        if (!ints.TryGet(_hitCountKeyId, out int hitCount))
        {
            throw new InvalidOperationException($"Spatial graph '{graphKey}' did not write {HitCountKey}.");
        }

        return hitCount;
    }

    private void CapturePresentationFromBlackboard()
    {
        ref BlackboardEntityBuffer entities = ref _world!.Get<BlackboardEntityBuffer>(_caster);
        _showNearest = entities.TryGet(_nearestKeyId, out Entity nearest) && nearest != Entity.Null;
        _showFirst = entities.TryGet(_firstKeyId, out Entity first) && first != Entity.Null;
        _lastHitIndex = _showNearest ? IndexOfTarget(nearest) : -1;
        if (_showNearest && _lastHitIndex < 0)
        {
            throw new InvalidOperationException("Nearest blackboard entity is not a spawned presentation target.");
        }

        if (_lastHitIndex >= 0 && _lastHitIndex < _flash.Length)
        {
            _flash[_lastHitIndex] = 12;
        }
    }

    private int IndexOfTarget(Entity entity)
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i].Equals(entity)) return i;
        }

        return -1;
    }

    private void RequireGraph(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId <= 0 || !_programs!.TryGetProgram(graphId, out _))
        {
            throw new InvalidOperationException($"Required spatial graph '{graphKey}' is missing.");
        }
    }

    private void RequireTargetListGetOnAllGraphs()
    {
        for (int g = 0; g < FeaturedGraphs.Length; g++)
        {
            string graphKey = FeaturedGraphs[g];
            int graphId = GraphIdRegistry.GetId(graphKey);
            if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
            {
                throw new InvalidOperationException($"Required spatial graph '{graphKey}' is missing.");
            }

            bool saw = false;
            for (int i = 0; i < program.Length; i++)
            {
                if ((GraphNodeOp)program[i].Op == GraphNodeOp.TargetListGet)
                {
                    saw = true;
                    break;
                }
            }

            if (!saw)
            {
                throw new InvalidOperationException($"Spatial graph '{graphKey}' must emit TargetListGet.");
            }
        }
    }

    private Entity SpawnCombatant(int xCm, int yCm, int teamId, uint layerCategory)
    {
        Entity entity = _world!.Create(
            new MapEntity(),
            new Team { Id = teamId },
            WorldPositionCm.FromCm(xCm, yCm),
            new EntityLayer(category: layerCategory, mask: uint.MaxValue),
            new BlackboardIntBuffer(),
            new BlackboardEntityBuffer());
        AddToGrid(entity, xCm, yCm);
        return entity;
    }

    private void SpawnEnemy(WorldCmInt2 worldCm, int presentationIndex)
    {
        Entity entity = SpawnCombatant(worldCm.X, worldCm.Y, EnemyTeamId, EnemyLayer);
        if (presentationIndex >= 0 && presentationIndex < _tx.Length)
        {
            _tx[presentationIndex] = worldCm.X * 0.01f;
            _ty[presentationIndex] = worldCm.Y * 0.01f;
            _targets[presentationIndex] = entity;
        }
    }

    private void AddToGrid(Entity entity, int xCm, int yCm)
    {
        IntVector2 grid = _coords!.WorldToGrid(new WorldCmInt2(xCm, yCm));
        _grid!.Add(entity, new IntRect(grid.X, grid.Y, grid.X + 1, grid.Y + 1));
    }
}
