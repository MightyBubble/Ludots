using System;
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
    private enum Phase
    {
        ReadHealth,
        Strike,
        ApplyMark,
        RemoveMark,
        Complete
    }

    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
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
    private int _markTemplateId;
    private bool _ownsPrograms;

    public float TargetHealth { get; private set; }
    public float DamageBonus { get; private set; }
    public int PendingEffectRequests { get; private set; }
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
        _markTemplateId = EffectTemplateIdRegistry.Register(GraphOpsAttrGraphKeys.MarkEffect);

        RequireGraph(GraphOpsAttrGraphKeys.ReadHealth);
        RequireGraph(GraphOpsAttrGraphKeys.Strike);
        RequireGraph(GraphOpsAttrGraphKeys.ApplyMark);
        RequireGraph(GraphOpsAttrGraphKeys.RemoveMark);

        _world = World.Create();
        _requests = new EffectRequestQueue();
        var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        _api = new GasGraphRuntimeApi(_world, spatialQueries: null, coords: null, eventBus: null, effectRequests: _requests, tagOps: tagOps);

        _caster = _world.Create(new AttributeBuffer(), new DirtyFlags());
        _target = _world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());

        ResetCombatants();
        Metrics.AgentCount = 2;
        Metrics.Detail = "读血量：准备查看目标当前生命值。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
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
                ExecuteEffectGraph(GraphOpsAttrGraphKeys.ReadHealth);
                TargetHealth = _world!.Get<AttributeBuffer>(_target).GetCurrent(_healthAttrId);
                DamageBonus = _world.Get<AttributeBuffer>(_caster).GetCurrent(_bonusAttrId);
                Metrics.Detail = $"读血量：目标还有 {TargetHealth:F0} 点血，施法者加伤 {DamageBonus:F0}。";
                _phase = Phase.Strike;
                break;
            case Phase.Strike:
                ExecuteEffectGraph(GraphOpsAttrGraphKeys.Strike);
                TargetHealth = _world!.Get<AttributeBuffer>(_target).GetCurrent(_healthAttrId);
                float tally = _world.Get<AttributeBuffer>(_caster).GetCurrent(_tallyAttrId);
                Metrics.Detail = $"加伤：结算后目标剩 {TargetHealth:F0} 血，本轮伤害已记入 tally={tally:F0}。";
                _phase = Phase.ApplyMark;
                break;
            case Phase.ApplyMark:
                ExecuteEffectGraph(GraphOpsAttrGraphKeys.ApplyMark);
                PendingEffectRequests = _requests!.Count;
                Metrics.Detail = $"上效果：已向目标投递标记效果（队列 {PendingEffectRequests} 条）。";
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

    private void ExecuteEffectGraph(string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (!_programs!.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException($"Graph '{graphKey}' is not registered.");
        }

        GraphExecutor.Execute(_world!, _caster, _target, default, program, _api!, GraphKind.Effect);
    }

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
        targetAttrs.SetBase(_healthAttrId, 80f);
        targetAttrs.SetCurrent(_healthAttrId, 80f);
        TargetHealth = 80f;
        DamageBonus = 5f;
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
}
