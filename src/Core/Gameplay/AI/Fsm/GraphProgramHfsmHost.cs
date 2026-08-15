using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.AI.Fsm;

public sealed class GraphProgramHfsmHost : IHfsmGraphHost
{
    private const int ScriptCacheCapacity = 8;
    private readonly GraphProgramRegistry _programs;
    private readonly World? _world;
    private readonly IGraphRuntimeApi? _api;
    private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
    private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
    private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
    private readonly int[] _cachedGraphIds = new int[ScriptCacheCapacity];
    private readonly int[] _cachedVersions = new int[ScriptCacheCapacity];
    private readonly GraphInstruction[]?[] _cachedPrograms = new GraphInstruction[ScriptCacheCapacity][];
    private int _nextCacheSlot;

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
        GraphInstruction[] program = ResolveScriptProgram(graphId, hostLabel);

        Array.Clear(_ints, 0, _ints.Length);
        Array.Clear(_bools, 0, _bools.Length);
        Array.Clear(_callStack, 0, _callStack.Length);
        var cursor = new GraphExecutionCursor();
        GraphSliceResult result = GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
            _programs,
            program,
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

    private GraphInstruction[] ResolveScriptProgram(int graphId, string hostLabel)
    {
        int version = _programs.Version;
        for (int i = 0; i < ScriptCacheCapacity; i++)
        {
            if (_cachedGraphIds[i] != graphId || _cachedVersions[i] != version)
            {
                continue;
            }

            GraphInstruction[]? cached = _cachedPrograms[i];
            if (cached != null)
            {
                return cached;
            }
        }

        GraphInstruction[] program = _programs.RequireProgramArray(graphId, GraphKind.Script, hostLabel);
        int slot = _nextCacheSlot;
        _nextCacheSlot = (slot + 1) % ScriptCacheCapacity;
        _cachedGraphIds[slot] = graphId;
        _cachedVersions[slot] = version;
        _cachedPrograms[slot] = program;
        return program;
    }
}
