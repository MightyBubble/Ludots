using System;
using System.Collections.Generic;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

public sealed class AbilityGraphSandboxRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private readonly GraphFunctionCatalog _catalog = new();
    private readonly Dictionary<int, GraphInstruction[]> _programs = new();
    private float _accum;
    private int _castWave;
    private float[] _tx = Array.Empty<float>();
    private float[] _ty = Array.Empty<float>();
    private byte[] _flash = Array.Empty<byte>();
    private int _lastHit = -1;
    private string _lastSpell = string.Empty;

    public float CasterX => 0f;
    public float CasterY => 0f;
    public float[] TargetX => _tx;
    public float[] TargetY => _ty;
    public byte[] Flash => _flash;
    public int TargetCount => _tx.Length;
    public int LastHit => _lastHit;
    public string LastSpell => _lastSpell;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_ability_graph_sandbox" };

    public void EnsureWorld()
    {
        if (_programs.Count > 0) return;
        _programs[101] = CompileConstHalt("ability.slash", 11);
        _programs[102] = CompileConstHalt("ability.bash", 22);
        _catalog.Register("ability.slash", 101, GraphKind.Script);
        _catalog.Register("ability.bash", 102, GraphKind.Script);

        int targets = Math.Min(_config.FeaturedAgentCount, 8);
        _tx = new float[targets];
        _ty = new float[targets];
        _flash = new byte[targets];
        for (int i = 0; i < targets; i++)
        {
            float t = targets <= 1 ? 0.5f : i / (float)(targets - 1);
            float ang = -0.7f + t * 1.4f;
            _tx[i] = MathF.Sin(ang) * 6f;
            _ty[i] = 4.5f + MathF.Cos(ang) * 0.8f;
        }

        Metrics.AgentCount = targets;
        Metrics.Detail = $"Ability FuncLib consume catalog={_catalog.Count}";
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

        _lastSpell = (_castWave & 1) == 0 ? "ability.slash" : "ability.bash";
        GraphFunctionEntry fn = _catalog.Require(_lastSpell);
        if (!_programs.TryGetValue(fn.GraphId, out GraphInstruction[]? program) || program == null)
        {
            throw new InvalidOperationException($"FuncLib '{_lastSpell}' graph id {fn.GraphId} has no program.");
        }

        _lastHit = _castWave % _tx.Length;

        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

        var sw = Stopwatch.StartNew();
        // Featured cast on one target; also run a cheap crowd of halt scripts for pressure band budget.
        var cursor = new GraphExecutionCursor();
        var state = new GraphExecutionState
        {
            I = ints,
            B = bools,
            CallStack = callStack,
            Status = GraphExecutionStatus.Running
        };
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, 32);
        if (!result.Halted) throw new InvalidOperationException("Ability scripts must halt.");
        _flash[_lastHit] = 12;

        int crowd = Math.Min(_config.CrowdBandCount, 2000);
        for (int i = 0; i < crowd; i++)
        {
            cursor.Reset();
            state.Status = GraphExecutionStatus.Running;
            GasGraphOpHandlerTable.ExecuteSlice(
                ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, 16);
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        Metrics.Detail =
            $"Ability FuncLib cast={_lastSpell} id={fn.GraphId} hit={_lastHit} last={Metrics.LastThinkMs:F3}ms";
    }

    private static GraphInstruction[] CompileConstHalt(string id, int value)
    {
        var doc = new GraphControlFlowDocument
        {
            Id = id,
            Entry = "c",
            Nodes =
            {
                new GraphControlFlowNode { Id = "c", Op = nameof(GraphNodeOp.ConstInt), IntValue = value },
                new GraphControlFlowNode { Id = "h", Op = nameof(GraphNodeOp.HaltReturnInt) }
            },
            ControlEdges = { new GraphControlFlowEdge("c", GraphControlFlowPorts.Next, "h") },
            ValueEdges = { new GraphControlFlowValueEdge("c", GraphControlFlowPorts.Value, "h", GraphControlFlowPorts.Value) }
        };
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
        if (!compiled.Succeeded) throw new InvalidOperationException($"Ability Script '{id}' failed to compile.");
        return compiled.Program;
    }
}
