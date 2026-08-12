using System;
using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public sealed class GraphOpsRelRuntime
{
    public const string QueryFriendRankName = "rel.query.friendRank";
    public const string QueryChainProbeName = "rel.query.chainProbe";
    public const string EffectBreakLinkName = "rel.effect.breakLink";

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphFunctionCatalog? _catalog;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private RelationshipRuntime? _relationships;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _trustedFlagId;
    private Entity _player;
    private Entity[] _friends = Array.Empty<Entity>();
    private readonly float[] _floats = new float[GraphVmLimits.MaxFloatRegisters];
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly Entity[] _entities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private float _accum;
    private int _wave;
    private int _friendCount;
    private int _loyaltyTop;
    private int _loyaltyAverage;
    private int _incomingCount;
    private int _mutualCount;
    private int _brokenLinks;
    private string _topFriendLabel = "-";
    private string _phase = "查好友链";

    public float PlayerX => 0f;
    public float PlayerY => 0f;
    public int FriendSlotCount => _friends.Length;
    public int FriendCount => _friendCount;
    public int LoyaltyTop => _loyaltyTop;
    public int LoyaltyAverage => _loyaltyAverage;
    public int IncomingCount => _incomingCount;
    public int MutualCount => _mutualCount;
    public int BrokenLinks => _brokenLinks;
    public string TopFriendLabel => _topFriendLabel;
    public string Phase => _phase;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_rel" };

    public void Bind(GraphProgramRegistry programs, GraphFunctionCatalog catalog)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _catalog == null)
        {
            throw new InvalidOperationException("GraphOpsRelRuntime.Bind(Registry, Catalog) required before EnsureWorld.");
        }

        _ = _catalog.Require(QueryFriendRankName);
        _ = _catalog.Require(QueryChainProbeName);
        _ = _catalog.Require(EffectBreakLinkName);

        _world = World.Create();
        var typeRegistry = new RelationshipTypeRegistry();
        var metricRegistry = new RelationshipMetricRegistry();
        var flagRegistry = new RelationshipFlagRegistry();
        var bandRegistry = new RelationshipBandRegistry();
        var changeBuffer = new RelationshipChangeBuffer();
        _socialBondTypeId = typeRegistry.Register("SocialBond");
        _loyaltyMetricId = metricRegistry.Register("Loyalty", -100, 100, 0);
        _trustedFlagId = flagRegistry.Register("Trusted");
        _ = flagRegistry.Register("Estranged");
        _relationships = new RelationshipRuntime(
            _world,
            typeRegistry,
            metricRegistry,
            flagRegistry,
            bandRegistry,
            changeBuffer,
            new RelationshipReverseIndex(_world));
        var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
        var entityQueries = new EntitySetQueryRuntime(_world, tagOps, _relationships);
        _api = new GasGraphRuntimeApi(
            _world,
            tagOps: tagOps,
            relationshipRuntime: _relationships,
            typeRegistry: typeRegistry,
            metricRegistry: metricRegistry,
            flagRegistry: flagRegistry,
            reasonRegistry: new RelationshipReasonRegistry(),
            entityQueries: entityQueries);

        _player = _world.Create();
        _friends = new Entity[4];
        int[] loyalty = [85, 62, 48, 35];
        for (int i = 0; i < _friends.Length; i++)
        {
            _friends[i] = _world.Create();
            _relationships.SetMetric(_player, _friends[i], _socialBondTypeId, _loyaltyMetricId, loyalty[i], reasonId: 0);
            if (loyalty[i] >= 50)
            {
                _relationships.SetFlag(_player, _friends[i], _socialBondTypeId, _trustedFlagId, true);
            }
        }

        _relationships.SetMetric(_friends[1], _player, _socialBondTypeId, _loyaltyMetricId, 70, reasonId: 0);
        _relationships.SetMetric(_friends[2], _player, _socialBondTypeId, _loyaltyMetricId, 55, reasonId: 0);

        Metrics.AgentCount = _friends.Length;
        Metrics.Detail = "好友链就位：查好友链、按好感排序、必要时拆链。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;

        var sw = Stopwatch.StartNew();
        if ((_wave & 1) == 1)
        {
            RunQueryWave();
        }
        else
        {
            RunUnlinkWave();
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
    }

    private void RunQueryWave()
    {
        _phase = "查好友链";
        ExecuteQuery(QueryFriendRankName, _player, _friends[0]);
        ExecuteQuery(QueryChainProbeName, _player, _friends[1]);
        Metrics.Detail =
            $"查好友链：{_friendCount}位好友；好感排序最高{_loyaltyTop}（{_topFriendLabel}），均值{_loyaltyAverage}；" +
            $"入链{_incomingCount}、互链{_mutualCount}；已拆链{_brokenLinks}次";
    }

    private void RunUnlinkWave()
    {
        _phase = "拆链";
        Entity weakest = FindWeakestLinkedFriend();
        if (weakest != Entity.Null)
        {
            ExecuteEffect(EffectBreakLinkName, _player, weakest);
            _brokenLinks++;
        }

        ExecuteQuery(QueryFriendRankName, _player, _friends[0]);
        Metrics.Detail =
            $"拆链：对{_topFriendLabel}执行拆链后剩{_friendCount}位；好感排序均值{_loyaltyAverage}；" +
            $"查好友链入链{_incomingCount}、互链{_mutualCount}";
    }

    private Entity FindWeakestLinkedFriend()
    {
        Entity weakest = Entity.Null;
        int min = int.MaxValue;
        for (int i = 0; i < _friends.Length; i++)
        {
            Entity friend = _friends[i];
            if (!_relationships!.HasLink(_player, friend, _socialBondTypeId))
            {
                continue;
            }

            int loyalty = _relationships.GetMetric(_player, friend, _socialBondTypeId, _loyaltyMetricId);
            if (loyalty < min)
            {
                min = loyalty;
                weakest = friend;
            }
        }

        return weakest;
    }

    private void ExecuteQuery(string funcName, Entity caster, Entity explicitTarget)
    {
        GraphFunctionEntry fn = _catalog!.Require(funcName);
        if (!_programs!.TryGetProgram(fn.GraphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"FuncLib '{funcName}' graph id {fn.GraphId} missing from Registry.");
        }

        var state = CreateState(caster, explicitTarget);
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

        if (string.Equals(funcName, QueryFriendRankName, StringComparison.Ordinal))
        {
            RefreshFriendRankFromRuntime(state.TargetList.Count);
        }
        else if (string.Equals(funcName, QueryChainProbeName, StringComparison.Ordinal))
        {
            RefreshChainProbeFromRuntime();
        }
    }

    private void ExecuteEffect(string funcName, Entity caster, Entity target)
    {
        GraphFunctionEntry fn = _catalog!.Require(funcName);
        if (!_programs!.TryGetProgram(fn.GraphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"FuncLib '{funcName}' graph id {fn.GraphId} missing from Registry.");
        }

        var state = CreateState(caster, target);
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        _topFriendLabel = EntityLabel(target);
    }

    private GraphExecutionState CreateState(Entity caster, Entity explicitTarget)
        => new()
        {
            World = _world,
            Caster = caster,
            ExplicitTarget = explicitTarget,
            TargetPosCm = default,
            Api = _api!,
            F = _floats,
            I = _ints,
            B = _bools,
            E = _entities,
            Targets = _targets,
            TargetList = new GraphTargetList(_targets),
            CallStack = _callStack,
            CallStackCount = 0,
        };

    private void RefreshFriendRankFromRuntime(int queriedCount)
    {
        _friendCount = queriedCount;
        int sum = 0;
        int count = 0;
        int top = int.MinValue;
        Entity best = Entity.Null;
        for (int i = 0; i < _friends.Length; i++)
        {
            Entity friend = _friends[i];
            if (!_relationships!.HasLink(_player, friend, _socialBondTypeId))
            {
                continue;
            }

            int loyalty = _relationships.GetMetric(_player, friend, _socialBondTypeId, _loyaltyMetricId);
            sum += loyalty;
            count++;
            if (loyalty > top)
            {
                top = loyalty;
                best = friend;
            }
        }

        _loyaltyTop = count > 0 ? top : 0;
        _loyaltyAverage = count > 0 ? sum / count : 0;
        _topFriendLabel = EntityLabel(best);
    }

    private void RefreshChainProbeFromRuntime()
    {
        int incoming = 0;
        int mutual = 0;
        for (int i = 0; i < _friends.Length; i++)
        {
            Entity friend = _friends[i];
            if (_relationships!.HasLink(friend, _player, _socialBondTypeId))
            {
                incoming++;
            }

            if (_relationships.HasLink(_player, friend, _socialBondTypeId) &&
                _relationships.HasLink(friend, _player, _socialBondTypeId))
            {
                mutual++;
            }
        }

        _incomingCount = incoming;
        _mutualCount = mutual;
    }

    private string EntityLabel(Entity entity)
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_friends[i] == entity)
            {
                return $"好友{i + 1}";
            }
        }

        return entity == Entity.Null ? "无" : "未知";
    }
}
