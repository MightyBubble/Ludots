using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Thin entry point for GAS Graph VM execution.
    /// Allocates registers on the stack and delegates to <see cref="GasGraphOpHandlerTable"/>.
    /// </summary>
    public static class GraphExecutor
    {
        public static void Execute(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPos,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var targetList = new GraphTargetList(targets);

            e[0] = caster;
            e[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPos = targetPos,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }

        public static void Execute(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPos,
            in GraphProgramBuffer program,
            IGraphRuntimeApi api)
        {
            Span<GraphInstruction> tmp = stackalloc GraphInstruction[GraphProgramBuffer.CAPACITY];
            int count = program.Count;
            if (count > GraphProgramBuffer.CAPACITY) count = GraphProgramBuffer.CAPACITY;
            for (int idx = 0; idx < count; idx++)
            {
                tmp[idx] = program.Get(idx);
            }

            Execute(world, caster, explicitTarget, targetPos, tmp.Slice(0, count), api);
        }

        /// <summary>
        /// Execute a graph program as a validation check.
        /// Returns the value of B[0] after execution: true = validation passed, false = rejected.
        /// Context: caster (E[0]), explicit target (E[1]), target position, and the graph API.
        /// The graph program can check range, resources, cooldowns, tags, etc.
        /// </summary>
        public static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPos,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var targetList = new GraphTargetList(targets);

            // B[0] defaults to 1 (pass) — validation graph must explicitly set B[0]=0 to reject
            b[0] = 1;

            e[0] = caster;
            e[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPos = targetPos,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            // B[0] = 1 → passed, B[0] = 0 → rejected
            return b[0] != 0;
        }

        /// <summary>
        /// Execute a validation graph from a <see cref="GraphProgramBuffer"/>.
        /// </summary>
        public static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPos,
            in GraphProgramBuffer program,
            IGraphRuntimeApi api)
        {
            Span<GraphInstruction> tmp = stackalloc GraphInstruction[GraphProgramBuffer.CAPACITY];
            int count = program.Count;
            if (count > GraphProgramBuffer.CAPACITY) count = GraphProgramBuffer.CAPACITY;
            for (int idx = 0; idx < count; idx++)
            {
                tmp[idx] = program.Get(idx);
            }

            return ExecuteValidation(world, caster, explicitTarget, targetPos, tmp.Slice(0, count), api);
        }
    }
}
