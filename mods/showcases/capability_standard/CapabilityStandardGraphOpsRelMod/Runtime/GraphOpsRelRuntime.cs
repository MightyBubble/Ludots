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
    public const string QueryFriendRankGraph = "Graph.GraphOpsRel.QueryFriendRank";
    public const string QueryChainProbeGraph = "Graph.GraphOpsRel.QueryChainProbe";
    public const string EffectBreakLinkGraph = "Graph.GraphOpsRel.EffectBreakLink";

    private readonly GraphShowcaseConfig _config = new();
    private GraphOpsRelShowcaseBundle? _bundle;
    private GraphProgramRegistry? _programs;
    private GraphOpsRelFunctionIndex? _functions;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private RelationshipRuntime? _relationships;
    private int _socialBondTypeId;
    private int _loyaltyMetricId;
    private int _trustedFlagId;
    private int _estrangedFlagId;
    private Entity _player;
    private Entity[] _friends = Array.Empty<Entity>();
    private byte[] _linked = Array.Empty<byte>();
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
    private int _loyaltySum;
    private int _loyaltyMin;
    private int _incomingCount;
    private int _mutualCount;
    private int _betweenCount;
    private int _brokenLinks;
    private string _topFriendLabel = "-";
    private string _weakFriendLabel = "-";
    private string _phase = "查好友链";

    public float PlayerX => 0f;
    public float PlayerY => 0f;
    public int FriendSlotCount => _friends.Length;
    public int FriendCount => _friendCount;
    public int LoyaltyTop => _loyaltyTop;
    public int LoyaltyAverage => _loyaltyAverage;
    public int LoyaltySum => _loyaltySum;
    public int LoyaltyMin => _loyaltyMin;
    public int IncomingCount => _incomingCount;
    public int MutualCount => _mutualCount;
    public int BetweenCount => _betweenCount;
    public int BrokenLinks => _brokenLinks;
    public string TopFriendLabel => _topFriendLabel;
    public string WeakFriendLabel => _weakFriendLabel;
    public string Phase => _phase;
    public bool IsFriendLinked(int index) => (uint)index < (uint)_linked.Length && _linked[index] != 0;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_rel" };

    public void BindStandaloneFromModAssets()
    {
        _bundle = GraphOpsRelShowcaseBootstrap.LoadStandalone();
        _programs = _bundle.Programs;
        _functions = _bundle.Functions;
    }

    public void Bind(GraphProgramRegistry programs, GraphOpsRelFunctionIndex functions)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null || _functions == null)
        {
            throw new InvalidOperationException(
                "GraphOpsRelRuntime.Bind(Registry, Functions) or BindStandaloneFromModAssets() required before EnsureWorld.");
        }

        _ = _functions.Require(QueryFriendRankName);
        _ = _functions.Require(QueryChainProbeName);
        _ = _functions.Require(EffectBreakLinkName);

        _world = World.Create();
        var typeRegistry = _bundle?.Types ?? new RelationshipTypeRegistry();
        var metricRegistry = _bundle?.Metrics ?? new RelationshipMetricRegistry();
        var flagRegistry = _bundle?.Flags ?? new RelationshipFlagRegistry();
        var reasonRegistry = _bundle?.Reasons ?? new RelationshipReasonRegistry();
        var bandRegistry = new RelationshipBandRegistry();
        var changeBuffer = new RelationshipChangeBuffer();

        if (_bundle == null)
        {
            _socialBondTypeId = typeRegistry.Register("SocialBond");
            _loyaltyMetricId = metricRegistry.Register("Loyalty", -100, 100, 0);
            _trustedFlagId = flagRegistry.Register("Trusted");
            _estrangedFlagId = flagRegistry.Register("Estranged");
            PatchPrograms(new GraphOpsRelSymbolResolver(typeRegistry, metricRegistry, flagRegistry));
        }
        else
        {
            _socialBondTypeId = typeRegistry.GetId("SocialBond");
            _loyaltyMetricId = metricRegistry.GetId("Loyalty");
            _trustedFlagId = flagRegistry.GetId("Trusted");
            _estrangedFlagId = flagRegistry.GetId("Estranged");
        }
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
            reasonRegistry: reasonRegistry,
            entityQueries: entityQueries);

        _player = _world.Create();
        _friends = new Entity[4];
        _linked = new byte[4];
        int[] loyalty = [85, 62, 48, 35];
        for (int i = 0; i < _friends.Length; i++)
        {
            _friends[i] = _world.Create();
            _relationships.SetMetric(_player, _friends[i], _socialBondTypeId, _loyaltyMetricId, loyalty[i], reasonId: 0);
            _linked[i] = 1;
            if (loyalty[i] >= 50)
            {
                _relationships.SetFlag(_player, _friends[i], _socialBondTypeId, _trustedFlagId, true);
            }
        }

        _relationships.SetMetric(_friends[1], _player, _socialBondTypeId, _loyaltyMetricId, 70, reasonId: 0);
        _relationships.SetMetric(_friends[2], _player, _socialBondTypeId, _loyaltyMetricId, 55, reasonId: 0);
        if (_trustedFlagId < 0 || _estrangedFlagId < 0)
        {
            throw new InvalidOperationException("Rel gallery requires Trusted and Estranged flags.");
        }

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
        RefreshLinkedFlags();
        Metrics.Detail =
            $"查好友链：Trusted筛选后按好感排序，好感区间剩{_friendCount}人；" +
            $"总和{_loyaltySum}、最高{_loyaltyTop}、最低{_loyaltyMin}、均值{_loyaltyAverage}；" +
            $"最强{_topFriendLabel}、最弱好友{_weakFriendLabel}；入链{_incomingCount}、互链{_mutualCount}、双人链{_betweenCount}";
    }

    private void RunUnlinkWave()
    {
        _phase = "拆链";
        string unlinked = "-";
        if (CountLinkedFriends() > 2)
        {
            Entity weakest = FindWeakestLinkedFriend();
            if (weakest != Entity.Null)
            {
                bool hasLink = _relationships!.HasLink(_player, weakest, _socialBondTypeId);
                int loyalty = _relationships.GetMetric(_player, weakest, _socialBondTypeId, _loyaltyMetricId);
                if (!hasLink)
                {
                    throw new InvalidOperationException(
                        $"Unlink wave selected {EntityLabel(weakest)} without a live SocialBond (loyalty={loyalty}).");
                }

                unlinked = EntityLabel(weakest);
                ExecuteEffect(EffectBreakLinkName, _player, weakest);
                _brokenLinks++;
            }
        }

        ExecuteQuery(QueryFriendRankName, _player, _friends[0]);
        ExecuteQuery(QueryChainProbeName, _player, _friends[1]);
        RefreshLinkedFlags();
        string unlinkBeat = unlinked != "-"
            ? $"确认仍有链后拆掉好感最低的{unlinked}并标记失和"
            : $"确认仍有链，已标记失和{_brokenLinks}次";
        Metrics.Detail =
            $"拆链：{unlinkBeat}；查好友链 Trusted筛选后按好感排序，好感区间剩{_friendCount}人，总和{_loyaltySum}、最低{_loyaltyMin}，最弱好友{_weakFriendLabel}；双人链{_betweenCount}";
    }

    private int CountLinkedFriends()
    {
        int count = 0;
        for (int i = 0; i < _friends.Length; i++)
        {
            if (_relationships!.HasLink(_player, _friends[i], _socialBondTypeId))
            {
                count++;
            }
        }

        return count;
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
        GraphFunctionEntry fn = _functions!.Require(funcName);
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
        GraphFunctionEntry fn = _functions!.Require(funcName);
        if (!_programs!.TryGetProgram(fn.GraphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"FuncLib '{funcName}' graph id {fn.GraphId} missing from Registry.");
        }

        var state = CreateState(caster, target);
        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
    }

    private GraphExecutionState CreateState(Entity caster, Entity explicitTarget)
    {
        _entities[0] = caster;
        _entities[1] = explicitTarget;
        return new()
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
    }

    private void RefreshFriendRankFromRuntime(int queriedCount)
    {
        _friendCount = queriedCount;
        int sum = 0;
        int count = 0;
        int top = int.MinValue;
        int min = int.MaxValue;
        Entity best = Entity.Null;
        Entity weakest = Entity.Null;
        for (int i = 0; i < _friends.Length; i++)
        {
            Entity friend = _friends[i];
            if (!_relationships!.HasLink(_player, friend, _socialBondTypeId))
            {
                continue;
            }

            if (!_relationships.HasFlag(_player, friend, _socialBondTypeId, _trustedFlagId))
            {
                continue;
            }

            int loyalty = _relationships.GetMetric(_player, friend, _socialBondTypeId, _loyaltyMetricId);
            if (loyalty < 30 || loyalty > 100)
            {
                continue;
            }

            sum += loyalty;
            count++;
            if (loyalty > top)
            {
                top = loyalty;
                best = friend;
            }

            if (loyalty < min)
            {
                min = loyalty;
                weakest = friend;
            }
        }

        _loyaltySum = sum;
        _loyaltyTop = count > 0 ? top : 0;
        _loyaltyMin = count > 0 ? min : 0;
        _loyaltyAverage = count > 0 ? sum / count : 0;
        _topFriendLabel = EntityLabel(best);
        _weakFriendLabel = EntityLabel(weakest);
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

        Span<Entity> between = stackalloc Entity[4];
        _incomingCount = incoming;
        _mutualCount = mutual;
        _betweenCount = _relationships!.CollectBetweenPair(_player, _friends[1], _socialBondTypeId, between);
    }

    private void RefreshLinkedFlags()
    {
        for (int i = 0; i < _friends.Length; i++)
        {
            _linked[i] = _relationships!.HasLink(_player, _friends[i], _socialBondTypeId) ? (byte)1 : (byte)0;
        }
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

    private void PatchPrograms(IGraphSymbolResolver resolver)
    {
        foreach (GraphFunctionEntry fn in new[]
                 {
                     _functions!.Require(QueryFriendRankName),
                     _functions.Require(QueryChainProbeName),
                     _functions.Require(EffectBreakLinkName),
                 })
        {
            if (!_programs!.TryGetRegistration(fn.GraphId, out GraphProgramRegistration registration))
            {
                throw new InvalidOperationException($"Rel graph '{fn.Name}' registration missing for symbol patch.");
            }

            GraphProgramSymbolPatcher.Patch(registration.Symbols, registration.Program, resolver);
        }
    }
}

internal sealed class GraphOpsRelSymbolResolver : IGraphSymbolResolver
{
    private readonly RelationshipTypeRegistry _types;
    private readonly RelationshipMetricRegistry _metrics;
    private readonly RelationshipFlagRegistry _flags;

    public GraphOpsRelSymbolResolver(
        RelationshipTypeRegistry types,
        RelationshipMetricRegistry metrics,
        RelationshipFlagRegistry flags)
    {
        _types = types;
        _metrics = metrics;
        _flags = flags;
    }

    public int ResolveTag(string name) => TagRegistry.Register(name);
    public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
    public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
    public int ResolveRelationshipType(string name) => _types.Register(name);
    public int ResolveRelationshipMetric(string name) => _metrics.Register(name, -100, 100, 0);
    public int ResolveRelationshipFlag(string name) => _flags.Register(name);
    public int ResolveRelationshipReason(string name) => ConfigKeyRegistry.Register($"relationship.reason.{name}");
    public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
    public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
}
