using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Graph.Codegen
{
    public sealed record GraphCodegenParityDiff(
        bool Matches,
        int InterpretReturnInt,
        int CodegenReturnInt,
        GraphExecutionStatus InterpretStatus,
        GraphExecutionStatus CodegenStatus,
        string? Detail);

    public static class GraphCodegenParity
    {
        public static GraphCodegenParityDiff CompareRunToHalt(
            ReadOnlySpan<GraphInstruction> program,
            GraphGeneratedExecute codegen,
            IGraphRuntimeApi? api = null)
        {
            SpanSnapshot interpret = RunInterpret(program, api);
            SpanSnapshot generated = RunCodegen(codegen, api);

            if (interpret.Status != generated.Status ||
                interpret.ReturnInt != generated.ReturnInt ||
                !interpret.I.AsSpan().SequenceEqual(generated.I) ||
                !interpret.B.AsSpan().SequenceEqual(generated.B) ||
                !FloatEquals(interpret.F, generated.F))
            {
                return new GraphCodegenParityDiff(
                    Matches: false,
                    InterpretReturnInt: interpret.ReturnInt,
                    CodegenReturnInt: generated.ReturnInt,
                    InterpretStatus: interpret.Status,
                    CodegenStatus: generated.Status,
                    Detail: "Register or status mismatch between interpret and codegen.");
            }

            return new GraphCodegenParityDiff(
                Matches: true,
                InterpretReturnInt: interpret.ReturnInt,
                CodegenReturnInt: generated.ReturnInt,
                InterpretStatus: interpret.Status,
                CodegenStatus: generated.Status,
                Detail: null);
        }

        private static SpanSnapshot RunInterpret(ReadOnlySpan<GraphInstruction> program, IGraphRuntimeApi? api)
        {
            float[] f = new float[GraphVmLimits.MaxFloatRegisters];
            int[] i = new int[GraphVmLimits.MaxIntRegisters];
            byte[] b = new byte[GraphVmLimits.MaxBoolRegisters];
            Entity[] e = new Entity[GraphVmLimits.MaxEntityRegisters];
            Entity[] targets = new Entity[GraphVmLimits.MaxTargets];
            int[] callStack = new int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                CallStack = callStack,
                CallStackCount = 0,
                Api = api!,
            };
            GasGraphOpHandlerTable.Instance.RunToHalt(ref state, program);
            return new SpanSnapshot(state.ReturnInt, state.Status, f, i, b);
        }

        private static SpanSnapshot RunCodegen(GraphGeneratedExecute codegen, IGraphRuntimeApi? api)
        {
            float[] f = new float[GraphVmLimits.MaxFloatRegisters];
            int[] i = new int[GraphVmLimits.MaxIntRegisters];
            byte[] b = new byte[GraphVmLimits.MaxBoolRegisters];
            Entity[] e = new Entity[GraphVmLimits.MaxEntityRegisters];
            Entity[] targets = new Entity[GraphVmLimits.MaxTargets];
            int[] callStack = new int[GraphVmLimits.MaxCallStackDepth];
            var state = new GraphExecutionState
            {
                F = f,
                I = i,
                B = b,
                E = e,
                Targets = targets,
                CallStack = callStack,
                CallStackCount = 0,
                Api = api!,
            };
            codegen(ref state);
            return new SpanSnapshot(state.ReturnInt, state.Status, f, i, b);
        }

        private static bool FloatEquals(float[] left, float[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct SpanSnapshot
        {
            public SpanSnapshot(int returnInt, GraphExecutionStatus status, float[] f, int[] i, byte[] b)
            {
                ReturnInt = returnInt;
                Status = status;
                F = f;
                I = i;
                B = b;
            }

            public int ReturnInt { get; }
            public GraphExecutionStatus Status { get; }
            public float[] F { get; }
            public int[] I { get; }
            public byte[] B { get; }
        }
    }
}
