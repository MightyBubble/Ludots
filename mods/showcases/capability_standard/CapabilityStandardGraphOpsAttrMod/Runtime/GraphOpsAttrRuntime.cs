using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsAttrMod.Runtime;

public sealed class GraphOpsAttrRuntime : IDisposable
{
    public const float OpeningHealth = 80f;
    public const int WoundLine = 70;
    public const float FullHit = 13f;
    public const float GlanceHit = 6f;
    public const float CasterX = -2.5f;
    public const float CasterY = 0f;
    public const float TargetX = 2.5f;
    public const float TargetY = 0f;

    private enum Phase
    {
        ReadHealth,
        StrikeFull,
        StrikeGlance,
        ApplyMark,
        RemoveMark,
        Complete
    }

    private readonly GraphShowcaseConfig _config = new();
    private readonly byte[] _lastBools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _lastInts = new int[GraphVmLimits.MaxIntRegisters];
    private readonly Entity[] _lastEntities = new Entity[GraphVmLimits.MaxEntityRegisters];
    private GraphProgramRegistry? _programs;
    private GraphOpsStageVisuals? _stage;
    private Entity _casterProxy;
    private Entity _targetProxy;
    private bool _visualsSpawned;
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private EffectRequestQueue? _requests;
    private Entity _caster;
    private Entity _target;
    private Phase _phase = Phase.ReadHealth;
    private float _accum;
    private int _healthAttrId;
    private int _bonusAttrId;
    private int _tallyAttrId;
    private int _lastHitAttrId;
    private int _markTemplateId;
    private bool _ownsPrograms;
    private GraphInstruction[] _strikeTemplate = Array.Empty<GraphInstruction>();
    private GraphInstructionSourceMap _strikeSourceMap = GraphInstructionSourceMap.Empty;

    public float TargetHealth { get; private set; }
    public float CasterHealth { get; private set; }
    public float DamageBonus { get; private set; }
    public float StrikeTally { get; private set; }
    public float LastHitPower { get; private set; }
    public int PendingEffectRequests { get; private set; }
    public bool TargetIsSelf { get; private set; }
    public bool LastStrikeWasGlance { get; private set; }
    public bool HitEnemy { get; private set; }
    public bool AllPhasesComplete => _phase == Phase.Complete;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_attr" };

    public void Bind(GraphProgramRegistry programs)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _ownsPrograms = false;
    }

    public void BindStandaloneFromModAssets()
    {
        _programs = GraphOpsAttrGraphBootstrap.LoadModGraphs(GraphOpsAttrGraphBootstrap.FindModAssetsRoot());
        _ownsPrograms = true;
    }

    public void BindStageVisuals(GraphOpsStageVisuals stage)
    {
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public void EnsureWorld()
    {
        if (_world != null) return;
        if (_programs == null)
        {
            throw new InvalidOperationException("GraphOpsAttrRuntime.Bind(Registry) or BindStandaloneFromModAssets() required.");
        }

        _healthAttrId = AttributeRegistry.Register("Health");
        _bonusAttrId = AttributeRegistry.Register("DamageBonus");
        _tallyAttrId = AttributeRegistry.Register("StrikeTally");
        _lastHitAttrId = AttributeRegistry.Register("LastHitPower");
        _markTemplateId = EffectTemplateIdRegistry.Register(GraphOpsAttrGraphKeys.MarkEffect);

        RequireGraph(GraphOpsAttrGraphKeys.ReadHealth);
        RequireGraph(GraphOpsAttrGraphKeys.Strike);
        RequireGraph(GraphOpsAttrGraphKeys.ApplyMark);
        RequireGraph(GraphOpsAttrGraphKeys.RemoveMark);

        int strikeId = GraphIdRegistry.GetId(GraphOpsAttrGraphKeys.Strike);
        if (!_programs.TryGetProgram(strikeId, out ReadOnlySpan<GraphInstruction> strikeProgram) ||
            !_programs.TryGetSourceMap(strikeId, out _strikeSourceMap))
        {
            throw new InvalidOperationException("Strike graph program or source map is missing.");
        }

        _strikeTemplate = strikeProgram.ToArray();

        _world = World.Create();
        _requests = new EffectRequestQueue();
        var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        _api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null, effectRequests: _requests, tagOps: tagOps);

        _caster = _world.Create(new AttributeBuffer(), new DirtyFlags());
        _target = _world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());

        ResetCombatants();
        Metrics.AgentCount = 2;
        Metrics.Detail = "读血量：准备查看目标当前生命值。";
        SpawnStageVisuals();
    }

    private void SpawnStageVisuals()
    {
        if (_stage == null || _visualsSpawned)
        {
            return;
        }

        _casterProxy = _stage.Spawn(GraphOpsVisualTemplates.Caster, "施法者", CasterX, CasterY, CasterHealth, 100f);
        _targetProxy = _stage.Spawn(GraphOpsVisualTemplates.Target, "目标", TargetX, TargetY, TargetHealth, OpeningHealth);
        _visualsSpawned = true;
    }

    private void SyncStageVisuals()
    {
        if (_stage == null || !_visualsSpawned)
        {
            return;
        }

        _stage.SetHealth(_casterProxy, CasterHealth, 100f);
        _stage.SetHealth(_targetProxy, TargetHealth, OpeningHealth);
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        SpawnStageVisuals();
        if (_phase == Phase.Complete) return;

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        var sw = Stopwatch.StartNew();
        RunActivePhase();
        sw.Stop();

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        SyncStageVisuals();
    }

    public void Dispose()
    {
        _world?.Dispose();
        _world = null;
        if (_ownsPrograms)
        {
            _programs = null;
        }
    }

    private void RunActivePhase()
    {
        switch (_phase)
        {
            case Phase.ReadHealth:
                ExecuteReadHealth();
                _phase = Phase.StrikeFull;
                break;
            case Phase.StrikeFull:
                ExecuteStrike();
                _phase = Phase.StrikeGlance;
                break;
            case Phase.StrikeGlance:
                ExecuteStrike();
                _phase = Phase.ApplyMark;
                break;
            case Phase.ApplyMark:
                ExecuteEffectGraph(GraphOpsAttrGraphKeys.ApplyMark);
                PendingEffectRequests = _requests!.Count;
                Metrics.Detail = $"上效果：已向对面投递标记效果（队列 {PendingEffectRequests} 条）。";
                _phase = Phase.RemoveMark;
                break;
            case Phase.RemoveMark:
                SpawnActiveMarkForRemoval();
                ExecuteEffectGraph(GraphOpsAttrGraphKeys.RemoveMark);
                Metrics.Detail = "卸效果：已请求移除目标身上的标记效果。";
                _phase = Phase.Complete;
                break;
        }
    }

    private void ExecuteReadHealth()
    {
        int graphId = GraphIdRegistry.GetId(GraphOpsAttrGraphKeys.ReadHealth);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) ||
            !_programs.TryGetSourceMap(graphId, out GraphInstructionSourceMap map))
        {
            throw new InvalidOperationException($"Graph '{GraphOpsAttrGraphKeys.ReadHealth}' is not registered with a source map.");
        }

        ExecuteGraph(program);
        RefreshAttributes();
        TargetIsSelf = ReadLastBool(program, map, "sameEntity", GraphNodeOp.CompareEqEntity);
        string selfText = TargetIsSelf ? "目标就是自己" : "目标不是自己";
        Metrics.Detail = $"读血量：目标还有 {TargetHealth:F0} 点血，施法者加伤 {DamageBonus:F0}；{selfText}。";
    }

    private void ExecuteStrike()
    {
        GraphInstruction[] program = (GraphInstruction[])_strikeTemplate.Clone();
        PatchConstInt(program, _strikeSourceMap, "healthNow", (int)MathF.Floor(TargetHealth));
        ExecuteGraph(program);
        RefreshAttributes();

        TargetIsSelf = ReadLastBool(program, _strikeSourceMap, "sameEntity", GraphNodeOp.CompareEqEntity);
        HitEnemy = ReadLastEntity(program, _strikeSourceMap, "pickTarget", GraphNodeOp.SelectEntity) == _target;
        LastStrikeWasGlance = ReadLastBool(program, _strikeSourceMap, "lowHp", GraphNodeOp.CompareLtInt);
        bool stillOpening = ReadLastBool(program, _strikeSourceMap, "stillOpening", GraphNodeOp.CompareEqInt);
        int combinedPower = ReadLastInt(program, _strikeSourceMap, "damageInt", GraphNodeOp.AddInt);
        string hitStyle = LastStrikeWasGlance ? "已经残血，改打轻击6" : "血量还够，打全力13";
        string openingText = stillOpening ? "还是开场满血80" : "不是开场满血80";
        string selfText = TargetIsSelf ? "目标就是自己" : "目标不是自己";
        string victimText = HitEnemy ? "选出对面挨打" : "选出自己挨打";
        Metrics.Detail =
            $"加伤：{hitStyle}；基础8加加伤5得到{combinedPower}；{openingText}；{selfText}，{victimText}；本轮出手次数记为 {StrikeTally:F0}。";
    }

    private void ExecuteEffectGraph(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException($"Graph '{graphKey}' is not registered.");
        }

        GraphExecutor.Execute(_world!, _caster, _target, default, program, _api!, GraphKind.Effect);
    }

    private void ExecuteGraph(ReadOnlySpan<GraphInstruction> program)
    {
        GraphKindOperationPolicy.RequireAllowed(GraphKind.Effect, program, GasGraphOpHandlerTable.Instance);
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var targetList = new GraphTargetList(targets);

        entities[0] = _caster;
        entities[1] = _target;

        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = _caster,
            ExplicitTarget = _target,
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            Targets = targets,
            TargetList = targetList,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        bools.CopyTo(_lastBools);
        ints.CopyTo(_lastInts);
        entities.CopyTo(_lastEntities);
    }

    private bool ReadLastBool(
        ReadOnlySpan<GraphInstruction> program,
        GraphInstructionSourceMap map,
        string nodeId,
        GraphNodeOp op)
        => _lastBools[RequireDest(program, map, nodeId, op)] != 0;

    private int ReadLastInt(
        ReadOnlySpan<GraphInstruction> program,
        GraphInstructionSourceMap map,
        string nodeId,
        GraphNodeOp op)
        => _lastInts[RequireDest(program, map, nodeId, op)];

    private Entity ReadLastEntity(
        ReadOnlySpan<GraphInstruction> program,
        GraphInstructionSourceMap map,
        string nodeId,
        GraphNodeOp op)
        => _lastEntities[RequireDest(program, map, nodeId, op)];

    private void SpawnActiveMarkForRemoval()
    {
        var mark = _world!.Create(
            new GameplayEffect { LifetimeKind = EffectLifetimeKind.After, ClockId = GasClockId.FixedFrame, AggregatesModifiers = true },
            new EffectTemplateRef { TemplateId = _markTemplateId });
        ref var container = ref _world.Get<ActiveEffectContainer>(_target);
        if (!container.Add(mark))
        {
            throw new InvalidOperationException("Failed to attach showcase mark effect for removal phase.");
        }
    }

    private void RefreshAttributes()
    {
        TargetHealth = _world!.Get<AttributeBuffer>(_target).GetCurrent(_healthAttrId);
        CasterHealth = _world.Get<AttributeBuffer>(_caster).GetCurrent(_healthAttrId);
        DamageBonus = _world.Get<AttributeBuffer>(_caster).GetCurrent(_bonusAttrId);
        StrikeTally = _world.Get<AttributeBuffer>(_caster).GetCurrent(_tallyAttrId);
        LastHitPower = _world.Get<AttributeBuffer>(_caster).GetCurrent(_lastHitAttrId);
    }

    private void ResetCombatants()
    {
        ref var casterAttrs = ref _world!.Get<AttributeBuffer>(_caster);
        ref var targetAttrs = ref _world.Get<AttributeBuffer>(_target);
        casterAttrs.SetBase(_healthAttrId, 100f);
        casterAttrs.SetCurrent(_healthAttrId, 100f);
        casterAttrs.SetBase(_bonusAttrId, 5f);
        casterAttrs.SetCurrent(_bonusAttrId, 5f);
        casterAttrs.SetBase(_tallyAttrId, 0f);
        casterAttrs.SetCurrent(_tallyAttrId, 0f);
        casterAttrs.SetBase(_lastHitAttrId, 0f);
        casterAttrs.SetCurrent(_lastHitAttrId, 0f);
        targetAttrs.SetBase(_healthAttrId, OpeningHealth);
        targetAttrs.SetCurrent(_healthAttrId, OpeningHealth);
        TargetHealth = OpeningHealth;
        CasterHealth = 100f;
        DamageBonus = 5f;
        StrikeTally = 0f;
        LastHitPower = 0f;
        _requests!.Clear();
        PendingEffectRequests = 0;
    }

    private void RequireGraph(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId <= 0 || !_programs!.TryGetProgram(graphId, out _))
        {
            throw new InvalidOperationException($"Required graph '{graphKey}' is missing from registry.");
        }
    }

    private static void PatchConstInt(
        GraphInstruction[] program,
        GraphInstructionSourceMap map,
        string nodeId,
        int value)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, nodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op != (ushort)GraphNodeOp.ConstInt)
            {
                continue;
            }

            program[i].Imm = value;
            return;
        }

        throw new InvalidOperationException($"Strike graph missing ConstInt node '{nodeId}'.");
    }

    private static byte RequireDest(
        ReadOnlySpan<GraphInstruction> program,
        GraphInstructionSourceMap map,
        string nodeId,
        GraphNodeOp op)
    {
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, nodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op == (ushort)op)
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException($"Graph missing node '{nodeId}' ({op}).");
    }
}
