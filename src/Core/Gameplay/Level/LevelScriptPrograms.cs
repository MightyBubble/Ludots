using System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.Level
{
    public static class LevelScriptKeys
    {
        public const string PhaseAdvance = "level.phaseAdvance";
    }

    /// <summary>Runs Level Scripts from <see cref="GraphProgramRegistry"/> only.</summary>
    public sealed class GraphProgramLevelHost : ILevelGraphHost
    {
        private readonly GraphProgramRegistry _programs;
        private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
        public int LastRanGraphId { get; private set; }

        public GraphProgramLevelHost(GraphProgramRegistry programs)
        {
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        }

        public void RunScript(int scriptGraphId)
        {
            if (!_programs.TryGetProgram(scriptGraphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
            {
                throw new InvalidOperationException($"Level Script graph id {scriptGraphId} is not registered.");
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
                ref state, program, GasGraphOpHandlerTable.Instance, ref cursor, budgetSteps: 64);
            if (!result.Halted)
            {
                throw new InvalidOperationException($"Level Script {scriptGraphId} must halt (got {result.Status}).");
            }

            LastRanGraphId = scriptGraphId;
        }
    }
}
