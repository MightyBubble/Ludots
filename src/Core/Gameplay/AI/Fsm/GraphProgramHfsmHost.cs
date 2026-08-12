using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Fsm;

/// <summary>
/// Evaluates HFSM condition/lifecycle bindings against registered GraphInstruction programs (L1 Script).
/// Caller owns program storage; host never compiles.
/// </summary>
public sealed class GraphProgramHfsmHost : IHfsmGraphHost
{
    private readonly Dictionary<int, GraphInstruction[]> _programs;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

    public GraphProgramHfsmHost(Dictionary<int, GraphInstruction[]> programs)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
    }

    public bool EvalCondition(int agentIndex, int conditionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(Require(conditionGraphId));
        return result.ReturnInt != 0;
    }

    public void RunAction(int agentIndex, int actionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(Require(actionGraphId));
        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"HFSM action graph {actionGraphId} did not halt (Yield is not allowed on lifecycle bindings).");
        }
    }

    private GraphInstruction[] Require(int graphId)
    {
        if (!_programs.TryGetValue(graphId, out GraphInstruction[]? program) || program == null)
        {
            throw new InvalidOperationException($"HFSM graph id {graphId} is not registered on GraphProgramHfsmHost.");
        }

        return program;
    }

    private GraphSliceResult ExecuteHalt(GraphInstruction[] program)
    {
        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        var cursor = new GraphExecutionCursor();
        var state = new GraphExecutionState
        {
            I = _ints,
            B = _bools,
            CallStack = _callStack,
            Status = GraphExecutionStatus.Running
        };
        GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
            ref state,
            program,
            GasGraphOpHandlerTable.Instance,
            ref cursor,
            budgetSteps: 64);
        if (!result.Halted)
        {
            throw new InvalidOperationException("HFSM-bound Script must halt within budget.");
        }

        return result;
    }
}
