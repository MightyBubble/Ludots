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
        internal static void Execute(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            ExecuteCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Effect);
        }

        public static void Execute(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            RequireKind(kind, GraphKind.Effect, nameof(Execute));
            ExecuteCore(world, caster, explicitTarget, targetPosCm, program, api, kind);
        }

        internal static void Execute(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
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

            Execute(world, caster, explicitTarget, targetPosCm, tmp.Slice(0, count), api);
        }

        /// <summary>
        /// Execute a graph program as a validation check.
        /// Returns the value of B[0] after execution: true = validation passed, false = rejected.
        /// Fail-closed: B[0] starts at 0 (reject). The validation graph must explicitly write B[0]=1 to pass.
        /// </summary>
        internal static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            return ExecuteValidationCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Validation);
        }

        public static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            RequireKind(kind, GraphKind.Validation, nameof(ExecuteValidation));
            return ExecuteValidationCore(world, caster, explicitTarget, targetPosCm, program, api, kind);
        }

        /// <summary>
        /// Execute a graph program and return F[0] as the score output.
        /// </summary>
        internal static float ExecuteScore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            return ExecuteScoreCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Score);
        }

        public static float ExecuteScore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            RequireKind(kind, GraphKind.Score, nameof(ExecuteScore));
            return ExecuteScoreCore(world, caster, explicitTarget, targetPosCm, program, api, kind);
        }

        /// <summary>
        /// Execute a validation graph from a <see cref="GraphProgramBuffer"/>.
        /// </summary>
        internal static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
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

            return ExecuteValidation(world, caster, explicitTarget, targetPosCm, tmp.Slice(0, count), api);
        }

        public static void ExecuteDerived(
            World world,
            Entity entity,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            RequireKind(kind, GraphKind.Derived, nameof(ExecuteDerived));
            ExecuteCore(world, entity, entity, default, program, api, kind);
        }

        private static void RequireKind(GraphKind actual, GraphKind expected, string entrypoint)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"Graph {entrypoint} requires kind '{expected}', but received '{actual}'.");
            }
        }

        private static void ExecuteCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance, entrypoint: nameof(GraphExecutor));
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);

            e[0] = caster;
            e[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPosCm = targetPosCm,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }

        private static bool ExecuteValidationCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance, entrypoint: nameof(ExecuteValidation));
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);

            // Fail-closed: B[0] defaults to 0 (reject). Validation graphs must explicitly set B[0]=1 to pass.
            b[0] = 0;

            e[0] = caster;
            e[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPosCm = targetPosCm,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            return b[0] != 0;
        }

        private static float ExecuteScoreCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance, entrypoint: nameof(ExecuteScore));
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            var targetList = new GraphTargetList(targets);

            e[0] = caster;
            e[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPosCm = targetPosCm,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return f[0];
        }

        /// <summary>
        /// Resumable Script execution. Caller owns register/call-stack spans across slices.
        /// </summary>
        public static GraphSliceResult ExecuteScriptSlice(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi? api,
            GraphProgramRegistry? programs,
            Span<float> floats,
            Span<int> ints,
            Span<byte> bools,
            Span<Entity> entities,
            Span<Entity> targets,
            Span<int> callStack,
            ref GraphExecutionCursor cursor,
            int budgetSteps,
            GraphKind kind = GraphKind.Script)
        {
            RequireKind(kind, GraphKind.Script, nameof(ExecuteScriptSlice));
            GraphKindOperationPolicy.RequireAllowed(kind, program, GasGraphOpHandlerTable.Instance, entrypoint: nameof(ExecuteScriptSlice));

            if (floats.Length < GraphVmLimits.MaxFloatRegisters ||
                ints.Length < GraphVmLimits.MaxIntRegisters ||
                bools.Length < GraphVmLimits.MaxBoolRegisters ||
                entities.Length < GraphVmLimits.MaxEntityRegisters ||
                targets.Length < GraphVmLimits.MaxTargets ||
                callStack.Length < GraphVmLimits.MaxCallStackDepth)
            {
                throw new ArgumentException("ExecuteScriptSlice register/call-stack spans are smaller than GraphVmLimits.");
            }

            var targetList = new GraphTargetList(targets);
            entities[0] = caster;
            entities[1] = explicitTarget;

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = explicitTarget,
                TargetPosCm = targetPosCm,
                Api = api!,
                Programs = programs,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
                CallStack = callStack,
                CallStackCount = cursor.CallStackCount,
                ReturnInt = cursor.ReturnInt,
                Status = GraphExecutionStatus.Running
            };

            return GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref cursor,
                budgetSteps);
        }
    }
}
