using System;
using System.Collections.Generic;
using System.Diagnostics;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace CapabilityStandardScriptFlowSandboxMod.Runtime;

/// <summary>
/// Atomic L1 Script demo: drink-until-full with Call + Yield + HaltReturnInt.
/// Water level rises one unit per think wave until halt.
/// </summary>
public sealed class ScriptFlowSandboxRuntime
{
    private readonly GraphShowcaseConfig _config = new();
    private GraphInstruction[]? _program;
    private GraphExecutionCursor _cursor;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private bool _started;
    private bool _halted;
    private float _accum;
    private int _water;

    public int Water => _water;
    public int Limit { get; } = 5;
    public bool Halted => _halted;
    public GraphShowcaseMetrics Metrics { get; } = new() { ShowcaseId = "capability_standard_script_flow_sandbox" };

    public void EnsureWorld()
    {
        if (_program != null) return;
        var doc = CreateDrinkUntilFull(Limit);
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
        if (!compiled.Succeeded)
        {
            throw new InvalidOperationException("ScriptFlowSandbox drink graph failed to compile.");
        }

        _program = compiled.Program;
        Metrics.AgentCount = 1;
        Metrics.Detail = "Script L1 drink-until-full (Call/Yield/Halt)";
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
            _program!,
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
                $"Script slice returned unexpected status {result.Status}; Running must be handled by increasing budget.");
        }
    }

    private static GraphControlFlowDocument CreateDrinkUntilFull(int limit)
    {
        return new GraphControlFlowDocument
        {
            Id = "showcase.script.drink-until-full",
            Entry = "zeroWater",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "zeroWater", Op = nameof(GraphNodeOp.ConstInt), IntValue = 0, PinRegister = 0 },
                new() { Id = "limitValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = limit, PinRegister = 1 },
                new() { Id = "oneValue", Op = nameof(GraphNodeOp.ConstInt), IntValue = 1, PinRegister = 2 },
                new() { Id = "readWater", Op = nameof(GraphNodeOp.MoveInt) },
                new() { Id = "readLimit", Op = nameof(GraphNodeOp.MoveInt) },
                new() { Id = "waterBelowLimit", Op = nameof(GraphNodeOp.CompareLtInt) },
                new() { Id = "branchNeedDrink", Op = GraphControlFlowCompiler.BranchBoolOp },
                new() { Id = "callDrink", Op = nameof(GraphNodeOp.Call) },
                new() { Id = "readWaterForReturn", Op = nameof(GraphNodeOp.MoveInt) },
                new() { Id = "done", Op = nameof(GraphNodeOp.HaltReturnInt) },
                new() { Id = "drinkReadWater", Op = nameof(GraphNodeOp.MoveInt) },
                new() { Id = "drinkReadOne", Op = nameof(GraphNodeOp.MoveInt) },
                new() { Id = "drinkAdd", Op = nameof(GraphNodeOp.AddInt), PinRegister = 0 },
                new() { Id = "drinkYield", Op = nameof(GraphNodeOp.Yield) },
                new() { Id = "drinkReturn", Op = nameof(GraphNodeOp.Return) }
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("zeroWater", GraphControlFlowPorts.Next, "limitValue"),
                new("limitValue", GraphControlFlowPorts.Next, "oneValue"),
                new("oneValue", GraphControlFlowPorts.Next, "readWater"),
                new("readWater", GraphControlFlowPorts.Next, "readLimit"),
                new("readLimit", GraphControlFlowPorts.Next, "waterBelowLimit"),
                new("waterBelowLimit", GraphControlFlowPorts.Next, "branchNeedDrink"),
                new("branchNeedDrink", GraphControlFlowPorts.True, "callDrink"),
                new("branchNeedDrink", GraphControlFlowPorts.False, "readWaterForReturn"),
                new("callDrink", GraphControlFlowPorts.Call, "drinkReadWater"),
                new("callDrink", GraphControlFlowPorts.Next, "readWater"),
                new("readWaterForReturn", GraphControlFlowPorts.Next, "done"),
                new("drinkReadWater", GraphControlFlowPorts.Next, "drinkReadOne"),
                new("drinkReadOne", GraphControlFlowPorts.Next, "drinkAdd"),
                new("drinkAdd", GraphControlFlowPorts.Next, "drinkYield"),
                new("drinkYield", GraphControlFlowPorts.Next, "drinkReturn")
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("zeroWater", GraphControlFlowPorts.Value, "readWater", GraphControlFlowPorts.Value),
                new("limitValue", GraphControlFlowPorts.Value, "readLimit", GraphControlFlowPorts.Value),
                new("readWater", GraphControlFlowPorts.Value, "waterBelowLimit", GraphControlFlowPorts.A),
                new("readLimit", GraphControlFlowPorts.Value, "waterBelowLimit", GraphControlFlowPorts.B),
                new("waterBelowLimit", GraphControlFlowPorts.Value, "branchNeedDrink", GraphControlFlowPorts.Condition),
                new("zeroWater", GraphControlFlowPorts.Value, "readWaterForReturn", GraphControlFlowPorts.Value),
                new("readWaterForReturn", GraphControlFlowPorts.Value, "done", GraphControlFlowPorts.Value),
                new("zeroWater", GraphControlFlowPorts.Value, "drinkReadWater", GraphControlFlowPorts.Value),
                new("oneValue", GraphControlFlowPorts.Value, "drinkReadOne", GraphControlFlowPorts.Value),
                new("drinkReadWater", GraphControlFlowPorts.Value, "drinkAdd", GraphControlFlowPorts.A),
                new("drinkReadOne", GraphControlFlowPorts.Value, "drinkAdd", GraphControlFlowPorts.B)
            }
        };
    }
}
