using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Fsm;

/// <summary>
/// Evaluates HFSM condition/lifecycle bindings against <see cref="GraphProgramRegistry"/> only.
/// </summary>
public sealed class GraphProgramHfsmHost : IHfsmGraphHost
{
    private readonly GraphProgramRegistry _programs;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

    public GraphProgramHfsmHost(GraphProgramRegistry programs)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
    }

    public bool EvalCondition(int agentIndex, int conditionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(conditionGraphId);
        return result.ReturnInt != 0;
    }

    public void RunAction(int agentIndex, int actionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(actionGraphId);
        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"HFSM action graph {actionGraphId} did not halt (Yield is not allowed on lifecycle bindings).");
        }
    }

    private GraphSliceResult ExecuteHalt(int graphId)
    {
        if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"HFSM graph id {graphId} is not registered in GraphProgramRegistry.");
        }

        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_callStack, 0, _callStack.Length);
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
