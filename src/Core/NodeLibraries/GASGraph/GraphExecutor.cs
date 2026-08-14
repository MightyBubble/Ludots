using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
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
            ExecuteCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Effect, GraphEntityPreset.None);
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
            ExecuteCore(world, caster, explicitTarget, targetPosCm, program, api, kind, GraphEntityPreset.None);
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

        internal static bool ExecuteValidation(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            return ExecuteValidationCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Validation, GraphEntityPreset.None);
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
            return ExecuteValidationCore(world, caster, explicitTarget, targetPosCm, program, api, kind, GraphEntityPreset.None);
        }

        internal static float ExecuteScore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api)
        {
            return ExecuteScoreCore(world, caster, explicitTarget, targetPosCm, program, api, GraphKind.Score, GraphEntityPreset.None);
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
            return ExecuteScoreCore(world, caster, explicitTarget, targetPosCm, program, api, kind, GraphEntityPreset.None);
        }

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
            ExecuteCore(world, entity, entity, default, program, api, kind, GraphEntityPreset.None);
        }

        public static void Execute(
            ref GraphFrame frame,
            ReadOnlySpan<GraphInstruction> program,
            bool programAlreadyValidated = false)
        {
            if (!programAlreadyValidated)
            {
                GraphKindOperationPolicy.RequireAllowed(
                    frame.Kind,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    entrypoint: nameof(GraphExecutor));
            }

            GraphExecutionState state = frame.CreateState();
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            frame.Cursor.CallStackCount = state.CallStackCount;
            frame.Cursor.ReturnInt = state.ReturnInt;
            frame.Cursor.InvokeDepth = state.InvokeDepth;
            frame.Cursor.Status = state.Status;
            frame.TargetList = state.TargetList;
        }

        public static GraphSliceResult ExecuteSlice(
            ref GraphFrame frame,
            ReadOnlySpan<GraphInstruction> program,
            int budgetSteps,
            bool programAlreadyValidated = false)
        {
            if (frame.Kind != GraphKind.Script)
            {
                throw new InvalidOperationException(
                    $"{GraphKindOperationPolicy.KindMismatchError}: ExecuteSlice 只接受 Script，收到的种类是「{frame.Kind}」。");
            }

            if (!programAlreadyValidated)
            {
                GraphKindOperationPolicy.RequireAllowed(
                    frame.Kind,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    entrypoint: nameof(ExecuteSlice));
            }

            GraphExecutionState state = frame.CreateState();
            GraphSliceResult result = GasGraphOpHandlerTable.ExecuteSlice(
                ref state,
                program,
                GasGraphOpHandlerTable.Instance,
                ref frame.Cursor,
                budgetSteps);
            frame.TargetList = state.TargetList;
            return result;
        }

        public static void ExecuteRegistered(
            GraphProgramRegistry programs,
            int graphId,
            GraphKind expectedKind,
            ref GraphFrame frame)
        {
            ArgumentNullException.ThrowIfNull(programs);
            programs.RequireHostKind(graphId, expectedKind, "这道执行门");
            if (frame.Kind != expectedKind)
            {
                throw new InvalidOperationException(
                    $"{GraphKindOperationPolicy.KindMismatchError}: 帧种类是「{frame.Kind}」，登记图 {graphId} 要求「{expectedKind}」。");
            }

            if (!programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            frame.Programs = programs;
            Execute(ref frame, program, programAlreadyValidated: true);
        }

        public static GraphSliceResult ExecuteRegisteredSlice(
            GraphProgramRegistry programs,
            int graphId,
            Span<int> ints,
            Span<byte> bools,
            Span<int> callStack,
            ref GraphExecutionCursor cursor,
            int budgetSteps,
            World? world = null,
            Entity caster = default,
            Entity explicitTarget = default,
            IGraphRuntimeApi? api = null)
        {
            ArgumentNullException.ThrowIfNull(programs);
            programs.RequireHostKind(graphId, GraphKind.Script, "切片宿主");
            if (!programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program) || program.Length == 0)
            {
                throw new InvalidOperationException($"Graph program id {graphId} is not registered.");
            }

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            GraphFrame frame = GraphFrame.Bind(
                GraphKind.Script,
                GraphEntityPreset.None,
                world,
                caster,
                explicitTarget,
                default,
                api,
                programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                callStack,
                cursor);
            GraphSliceResult result = ExecuteSlice(ref frame, program, budgetSteps, programAlreadyValidated: true);
            cursor = frame.Cursor;
            return result;
        }

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
            GraphFrame frame = GraphFrame.Bind(
                kind,
                GraphEntityPreset.None,
                world,
                caster,
                explicitTarget,
                targetPosCm,
                api,
                programs,
                floats,
                ints,
                bools,
                entities,
                targets,
                callStack,
                cursor);
            GraphSliceResult result = ExecuteSlice(ref frame, program, budgetSteps);
            cursor = frame.Cursor;
            return result;
        }

        private static void RequireKind(GraphKind actual, GraphKind expected, string entrypoint)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"{GraphKindOperationPolicy.KindMismatchError}: Graph {entrypoint} requires kind '{expected}', but received '{actual}'.");
            }
        }

        private static void ExecuteCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind,
            GraphEntityPreset slot2)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            GraphFrame frame = GraphFrame.Bind(
                kind,
                slot2,
                world,
                caster,
                explicitTarget,
                targetPosCm,
                api,
                programs: null,
                f,
                i,
                b,
                e,
                targets,
                callStack);
            Execute(ref frame, program);
        }

        private static bool ExecuteValidationCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind,
            GraphEntityPreset slot2)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            b[0] = 0;
            GraphFrame frame = GraphFrame.Bind(
                kind,
                slot2,
                world,
                caster,
                explicitTarget,
                targetPosCm,
                api,
                programs: null,
                f,
                i,
                b,
                e,
                targets,
                callStack);
            Execute(ref frame, program);
            return frame.B[0] != 0;
        }

        private static float ExecuteScoreCore(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            ReadOnlySpan<GraphInstruction> program,
            IGraphRuntimeApi api,
            GraphKind kind,
            GraphEntityPreset slot2)
        {
            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            GraphFrame frame = GraphFrame.Bind(
                kind,
                slot2,
                world,
                caster,
                explicitTarget,
                targetPosCm,
                api,
                programs: null,
                f,
                i,
                b,
                e,
                targets,
                callStack);
            Execute(ref frame, program);
            return frame.F[0];
        }
    }
}
