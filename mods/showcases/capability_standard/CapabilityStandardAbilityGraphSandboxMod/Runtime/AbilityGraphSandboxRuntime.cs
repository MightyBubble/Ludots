using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

/// <summary>Ability/Effect-graph-only sandbox: func-lib Scripts + halt slices (no BT/HFSM/Level).</summary>
public sealed class AbilityGraphSandboxRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private readonly GraphFunctionCatalog _catalog = new();
    private GraphInstruction[]? _slashProgram;
    private GraphInstruction[]? _bashProgram;
    private float _accum;
    private int _castWave;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_ability_graph_sandbox" };

    public void EnsureWorld()
    {
        if (_slashProgram != null) return;

        _slashProgram = CompileConstHalt("ability.slash", 11);
        _bashProgram = CompileConstHalt("ability.bash", 22);
        _catalog.Register("ability.slash", graphId: 101, GraphKind.Script);
        _catalog.Register("ability.bash", graphId: 102, GraphKind.Script);
        Metrics.AgentCount = _config.AgentCount; // concurrent settle targets marker
        Metrics.Detail = $"Ability/Effect-only funcLib={_catalog.Count}";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;
        _castWave++;

        // Alternate which library function "casts" across the target cohort.
        GraphInstruction[] program = (_castWave & 1) == 0 ? _slashProgram! : _bashProgram!;
        int targets = Math.Min(_config.AgentCount, 1000); // readable sandbox; stress 10k is separate matrix

        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];

        var sw = Stopwatch.StartNew();
        int steps = 0;
        for (int t = 0; t < targets; t++)
        {
            var cursor = new GraphExecutionCursor();
            var state = new GraphExecutionState
            {
                I = ints,
                B = bools,
                CallStack = callStack,
                Status = GraphExecutionStatus.Running
            };
            GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                32);
            steps += result.Steps;
            if (!result.Halted)
            {
                throw new InvalidOperationException("Ability graph sandbox scripts must halt in one slice.");
            }
        }

        sw.Stop();
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;
        string fn = (_castWave & 1) == 0 ? "ability.slash" : "ability.bash";
        Metrics.Detail =
            $"Ability/Effect-only cast={fn} targets={targets} steps={steps} last={Metrics.LastThinkMs:F3}ms max={Metrics.MaxThinkMs:F3}ms";
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
        if (!compiled.Succeeded)
        {
            throw new InvalidOperationException($"Ability Script '{id}' failed to compile.");
        }

        return compiled.Program;
    }
}
