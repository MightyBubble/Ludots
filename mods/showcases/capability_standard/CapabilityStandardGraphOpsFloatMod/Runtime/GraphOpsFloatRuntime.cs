using System.Diagnostics;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsFloatMod.Runtime;

public sealed class GraphOpsFloatRuntime
{
    public const float OpeningHealth = 100f;
    public const float MaxRange = 45f;

    private readonly GraphShowcaseConfig _config = new();
    private World? _world;
    private GasGraphRuntimeApi? _api;
    private Entity _caster;
    private Entity _target;
    private GraphInstruction[] _effectProgram = Array.Empty<GraphInstruction>();
    private GraphInstruction[] _validationProgram = Array.Empty<GraphInstruction>();
    private byte _finalDamageReg;
    private byte _rangeValidReg;
    private byte _permitReg;
    private float _accum;
    private int _wave;
    private float _distance;
    private float _lastDamage;
    private bool _lastCritical;
    private bool _lastRangeValid;
    private bool _lastPermit;
    private bool _lastApplied;
    private float _targetX = 4f;

    public float CasterX => -2f;
    public float CasterY => 0f;
    public float TargetX => _targetX;
    public float TargetY => 0f;
    public float Distance => _distance;
    public float LastDamage => _lastDamage;
    public bool LastCritical => _lastCritical;
    public bool LastRangeValid => _lastRangeValid;
    public bool LastPermit => _lastPermit;
    public bool LastApplied => _lastApplied;
    public float TargetHealth { get; private set; }
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_graph_ops_float" };

    public void EnsureWorld()
    {
        if (_world != null) return;

        _world = World.Create();
        _api = new GasGraphRuntimeApi(_world, spatialQueries: null, eventBus: null, effectRequests: null);
        _caster = _world.Create();
        _target = _world.Create();
        _distance = 12f;
        _lastPermit = true;
        TargetHealth = OpeningHealth;
        RecompileGraphs(_distance, permit: true);

        Metrics.AgentCount = 2;
        Metrics.Detail = "浮点伤害管线就位：按距离衰减、乘伤害倍率，负面修正翻成正数再加算，再钳制到上下限。";
    }

    public void Tick(float dt)
    {
        EnsureWorld();

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _wave++;

        _distance = 10f + (_wave % 7) * 8f;
        _targetX = CasterX + _distance * 0.2f;
        bool permit = _wave % 4 != 0;
        RecompileGraphs(_distance, permit);

        var sw = Stopwatch.StartNew();
        (_lastDamage, _lastCritical) = ExecuteEffectGraph(_caster, _target);
        (_lastPermit, _lastRangeValid) = ExecuteValidationGraph(_caster, _target);
        sw.Stop();

        _lastApplied = _lastPermit && _lastRangeValid;
        if (_lastApplied)
        {
            TargetHealth -= _lastDamage;
            if (TargetHealth <= 0f)
            {
                TargetHealth = OpeningHealth;
            }
        }

        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;

        string criticalText = _lastCritical ? "暴击" : "普通";
        string permitText = _lastPermit ? "开" : "关";
        string rangeText = _lastRangeValid ? "够得着" : "够不着";
        string applyText = _lastApplied
            ? $"最终伤害{_lastDamage:F1}（{criticalText}），血条下降到 {TargetHealth:F0}"
            : $"演算得出{_lastDamage:F1}，这一刀没打出去";
        Metrics.Detail =
            $"距离{_distance:F0}：衰减后乘伤害倍率1.5，负面修正翻成正数再加算，随机扰动再钳制0~80 → {applyText}；" +
            $"出手许可：{permitText}；射程判定：{rangeText}。";
    }

    private void RecompileGraphs(float distance, bool permit)
    {
        GraphControlFlowCompileResult effect = GraphOpsFloatGraphAuthoring.CompileEffectGraph(distance);
        GraphControlFlowCompileResult validation = GraphOpsFloatGraphAuthoring.CompileValidationGraph(distance, permit);

        _effectProgram = effect.Program;
        _validationProgram = validation.Program;
        _finalDamageReg = GraphOpsFloatGraphAuthoring.RequireFloatDest(
            effect,
            GraphOpsFloatGraphAuthoring.FinalDamageNodeId,
            GraphNodeOp.MinFloat);
        _rangeValidReg = GraphOpsFloatGraphAuthoring.RequireBoolDest(
            validation,
            GraphOpsFloatGraphAuthoring.RangeValidNodeId,
            GraphNodeOp.CompareGtFloat);
        _permitReg = GraphOpsFloatGraphAuthoring.RequireBoolDest(
            validation,
            GraphOpsFloatGraphAuthoring.PermitNodeId,
            GraphNodeOp.ConstBool);
    }

    private (float damage, bool critical) ExecuteEffectGraph(Entity caster, Entity target)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = caster,
            ExplicitTarget = target,
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            CallStack = callStack,
            RandomSeed = (uint)(0xA5A5A5A5u ^ (uint)_wave),
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, _effectProgram, GasGraphOpHandlerTable.Instance);

        byte criticalReg = FindBoolDest(_effectProgram, GraphNodeOp.CompareGtFloat);
        return (floats[_finalDamageReg], bools[criticalReg] != 0);
    }

    private (bool permit, bool rangeValid) ExecuteValidationGraph(Entity caster, Entity target)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

        var state = new GraphExecutionState
        {
            World = _world!,
            Caster = caster,
            ExplicitTarget = target,
            Api = _api!,
            F = floats,
            I = ints,
            B = bools,
            E = entities,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running
        };

        GasGraphOpHandlerTable.Execute(ref state, _validationProgram, GasGraphOpHandlerTable.Instance);
        return (bools[_permitReg] != 0, bools[_rangeValidReg] != 0);
    }

    private static byte FindBoolDest(ReadOnlySpan<GraphInstruction> program, GraphNodeOp op)
    {
        for (int i = program.Length - 1; i >= 0; i--)
        {
            if (program[i].Op == (ushort)op)
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException($"Program missing bool op {op}.");
    }
}
