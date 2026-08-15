using System;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.AI.BehaviorTree;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardScriptFlowSandboxMod.Runtime;

/// <summary>Atomic Script slice demo loaded through ActionLib.</summary>
public sealed class ScriptFlowSandboxRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphProgramRegistry? _programs;
    private GraphActionCatalog? _actions;
    private int _drinkGraphId;
    private GraphExecutionCursor _cursor;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private bool _started;
    private bool _halted;
    private float _accum;
    private int _water;

    public const string DrinkActionName = "script.drinkUntilFull";
    public int Water => _water;
    public int Limit { get; } = 5;
    public bool Halted => _halted;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_script_flow_sandbox" };

    public void Bind(GraphProgramRegistry programs, GraphActionCatalog actions)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public void EnsureWorld()
    {
        if (_drinkGraphId > 0) return;
        if (_programs == null || _actions == null)
        {
            throw new InvalidOperationException("ScriptFlowSandboxRuntime.Bind(Registry, ActionCatalog) required.");
        }

        _drinkGraphId = GraphRegistryScriptResolver.RequireActionId(_actions, DrinkActionName, GraphActionHost.Script);
        _ = GraphRegistryScriptResolver.RequireProgram(_programs, _drinkGraphId);
        Metrics.AgentCount = 1;
        Metrics.Detail = "Script drink-until-full from ActionLib";
    }

    public void Tick(float dt)
    {
        EnsureWorld();
        if (_halted) return;

        _accum += dt;
        if (_accum < _config.ThinkPeriodSeconds) return;
        _accum = 0f;

        if (!_started)
        {
            _cursor.Reset();
            Array.Clear(_ints, 0, _ints.Length);
            Array.Clear(_bools, 0, _bools.Length);
            Array.Clear(_callStack, 0, _callStack.Length);
            _started = true;
        }

        ReadOnlySpan<GraphInstruction> program =
            GraphRegistryScriptResolver.RequireProgram(_programs!, _drinkGraphId);

        var state = new GraphExecutionState
        {
            I = _ints,
            B = _bools,
            CallStack = _callStack,
            CallStackCount = _cursor.CallStackCount,
            ReturnInt = _cursor.ReturnInt,
            Status = GraphExecutionStatus.Running
        };

        var sw = Stopwatch.StartNew();
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state,
            program,
            GasGraphOpHandlerTable.Instance,
            ref _cursor,
            budgetSteps: 64);
        sw.Stop();

        _water = _ints[0];
        Metrics.LastThinkMs = sw.Elapsed.TotalMilliseconds;
        if (Metrics.LastThinkMs > Metrics.MaxThinkMs) Metrics.MaxThinkMs = Metrics.LastThinkMs;
        Metrics.ThinkWaves++;

        if (result.Halted)
        {
            _halted = true;
            _water = result.ReturnInt;
            Metrics.Detail = $"Script halted water={_water} waves={Metrics.ThinkWaves} last={Metrics.LastThinkMs:F3}ms";
        }
        else if (result.Yielded)
        {
            Metrics.Detail = $"Script yielded water={_water}/{Limit} waves={Metrics.ThinkWaves} last={Metrics.LastThinkMs:F3}ms";
        }
        else
        {
            throw new InvalidOperationException(
                $"Script slice returned unexpected status {result.Status}; increase budget.");
        }
    }
}
