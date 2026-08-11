using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.Gameplay.Level
{
    public static class LevelScriptPrograms
    {
        public static Dictionary<int, GraphInstruction[]> CreateTwoPhaseTrialPrograms()
        {
            return new Dictionary<int, GraphInstruction[]>
            {
                [LevelBlueprintFactory.PhaseAdvanceScriptGraphId] = CompileConstHalt(
                    "level.phase.advance",
                    LevelBlueprintFactory.PhaseAdvanceScriptGraphId)
            };
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
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("c", GraphControlFlowPorts.Value, "h", GraphControlFlowPorts.Value)
                }
            };
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
            if (!compiled.Succeeded)
            {
                throw new InvalidOperationException($"Failed to compile Level Script '{id}'.");
            }

            return compiled.Program;
        }
    }

    /// <summary>Runs registered Level Scripts to halt with a caller-owned CallStack.</summary>
    public sealed class GraphProgramLevelHost : ILevelGraphHost
    {
        private readonly Dictionary<int, GraphInstruction[]> _programs;
        private readonly int[] _ints = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _bools = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly int[] _callStack = new int[GraphVmLimits.MaxCallStackDepth];
        public int LastRanGraphId { get; private set; }

        public GraphProgramLevelHost(Dictionary<int, GraphInstruction[]> programs)
        {
            _programs = programs ?? throw new ArgumentNullException(nameof(programs));
        }

        public void RunScript(int scriptGraphId)
        {
            if (!_programs.TryGetValue(scriptGraphId, out GraphInstruction[]? program) || program == null)
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
