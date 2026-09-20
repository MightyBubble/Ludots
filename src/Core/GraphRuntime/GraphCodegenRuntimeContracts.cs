using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.GraphRuntime
{
    public enum GraphExecutionBackend : byte
    {
        Interpret = 0,
        Codegen = 1,
        Parity = 2,
    }

    public enum GraphCodegenLoadMode : byte
    {
        Interpret = 0,
        Codegen = 1,
        CodegenPrefer = 2,
    }

    public delegate void GraphGeneratedExecute(ref GraphExecutionState state);

    public delegate GraphSliceResult GraphGeneratedExecuteSlice(
        ref GraphExecutionState state,
        ref GraphExecutionCursor cursor,
        int budgetSteps);

    public interface IGraphCodegenRuntimeBinder
    {
        void BindAll(GraphProgramRegistry registry, GraphCodegenLoadMode mode);
    }

    public static class GraphCodegenLoadModeParser
    {
        public static GraphCodegenLoadMode Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return GraphCodegenLoadMode.Interpret;
            }

            if (string.Equals(raw, "interpret", StringComparison.OrdinalIgnoreCase))
            {
                return GraphCodegenLoadMode.Interpret;
            }

            if (string.Equals(raw, "codegen", StringComparison.OrdinalIgnoreCase))
            {
                return GraphCodegenLoadMode.Codegen;
            }

            if (string.Equals(raw, "codegen-prefer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "codegenPrefer", StringComparison.OrdinalIgnoreCase))
            {
                return GraphCodegenLoadMode.CodegenPrefer;
            }

            throw new InvalidOperationException(
                $"Unknown graphExecutionBackend '{raw}'. Expected interpret | codegen | codegen-prefer.");
        }
    }
}
