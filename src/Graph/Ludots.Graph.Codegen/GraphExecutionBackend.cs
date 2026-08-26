namespace Ludots.Graph.Codegen
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
}
