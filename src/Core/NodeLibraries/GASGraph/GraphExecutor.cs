using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphExecutionStatus : byte
    {
        Completed = 0,
        InstructionBudgetExhausted = 1
    }

    public struct GraphInstructionBudget
    {
        private int _consumed;

        public GraphInstructionBudget(int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), limit, "Graph instruction budget must be positive.");
            }

            Limit = limit;
            _consumed = 0;
        }

        public readonly int Limit { get; }
        public readonly int Consumed => _consumed;
        public readonly int Remaining => Limit - _consumed;

        internal bool TryConsumeInstruction()
        {
            if (_consumed >= Limit)
            {
                return false;
            }

            _consumed++;
            return true;
        }
    }

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
        /// Context: caster (E[0]), explicit target (E[1]), target context, target position, and the graph API.
        /// </summary>
        internal static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            return ExecuteValidationCore(
                world,
                caster,
                explicitTarget,
                default,
                targetPosCm,
                program,
                api,
                GraphKind.Validation);
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
            return ExecuteValidation(
                world,
                caster,
                explicitTarget,
                default,
                targetPosCm,
                program,
                api,
                kind);
        }

        public static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind)
        {
            RequireKind(kind, GraphKind.Validation, nameof(ExecuteValidation));
            return ExecuteValidationCore(
                world,
                caster,
                explicitTarget,
                targetContext,
                targetPosCm,
                program,
                api,
                kind);
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
        /// Execute a score graph against a caller-owned total budget.
        /// </summary>
        public static GraphExecutionStatus ExecuteScore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind,
            ref GraphInstructionBudget instructionBudget,
            out float score)
        {
            RequireKind(kind, GraphKind.Score, nameof(ExecuteScore));
            GraphKindOperationPolicy.RequireAllowed(
                kind,
                program,
                GasGraphOpHandlerTable.Instance,
                entrypoint: nameof(ExecuteScore));
            return ExecuteScoreBudgeted(
                world,
                caster,
                explicitTarget,
                targetPosCm,
                program,
                api,
                ref instructionBudget,
                out score);
        }

        /// <summary>
        /// Executes a score program already certified and frozen by its owning compiled runtime.
        /// The caller must validate GraphKind.Score operation policy before publishing the program.
        /// </summary>
        internal static GraphExecutionStatus ExecutePrevalidatedScore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            ref GraphInstructionBudget instructionBudget,
            out float score)
        {
            return ExecuteScoreBudgeted(
                world,
                caster,
                explicitTarget,
                targetPosCm,
                program,
                api,
                ref instructionBudget,
                out score);
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
                TargetList = targetList
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }

        private static bool ExecuteValidationCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            Entity targetContext,
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
                TargetContext = targetContext,
                TargetPosCm = targetPosCm,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList
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
            var instructionBudget = new GraphInstructionBudget(GraphVmLimits.MaxInstructionsPerExecution);
            GraphExecutionStatus status = ExecuteScoreBudgeted(
                world,
                caster,
                explicitTarget,
                targetPosCm,
                program,
                api,
                ref instructionBudget,
                out float score);
            if (status != GraphExecutionStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Graph VM exceeded MaxInstructionsPerExecution ({GraphVmLimits.MaxInstructionsPerExecution}). Possible infinite loop.");
            }

            return score;
        }

        private static GraphExecutionStatus ExecuteScoreBudgeted(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            ref GraphInstructionBudget instructionBudget,
            out float score)
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
                TargetPosCm = targetPosCm,
                Api = api,
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                TargetList = targetList
            };

            GraphExecutionStatus status = GasGraphOpHandlerTable.Execute(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref instructionBudget);
            score = status == GraphExecutionStatus.Completed ? f[0] : 0f;
            return status;
        }
    }
}
