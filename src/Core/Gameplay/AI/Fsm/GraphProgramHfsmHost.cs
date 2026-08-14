using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Fsm;

public sealed class GraphProgramHfsmHost : IHfsmGraphHost
{
    private readonly GraphProgramRegistry _programs;
    private readonly World? _world;
    private readonly IGraphRuntimeApi? _api;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];

    public GraphProgramHfsmHost(
        GraphProgramRegistry programs,
        World? world = null,
        IGraphRuntimeApi? api = null)
    {
        _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        _world = world;
        _api = api;
    }

    public bool EvalCondition(int agentIndex, int conditionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(conditionGraphId, "状态机条件");
        return result.ReturnInt != 0;
    }

    public void RunAction(int agentIndex, int actionGraphId)
    {
        GraphSliceResult result = ExecuteHalt(actionGraphId, "状态机生命周期");
        if (!result.Halted)
        {
            throw new InvalidOperationException(
                $"HFSM action graph {actionGraphId} did not halt (Yield is not allowed on lifecycle bindings).");
        }
    }

    private GraphSliceResult ExecuteHalt(int graphId, string hostLabel)
    {
        _programs.RequireHostKind(graphId, GraphKind.Script, hostLabel);
        if (!_programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
        {
            throw new InvalidOperationException($"HFSM graph id {graphId} is not registered in GraphProgramRegistry.");
        }

        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_callStack, 0, _callStack.Length);
        var cursor = new GraphExecutionCursor();
        GraphSliceResult result = GraphExecutor.ExecuteRegisteredSlice(
            _programs,
            graphId,
            _ints,
            _bools,
            _callStack,
            ref cursor,
            budgetSteps: 64,
            _world,
            api: _api);
        if (!result.Halted)
        {
            throw new InvalidOperationException("HFSM-bound Script must halt within budget.");
        }

        return result;
    }
}
