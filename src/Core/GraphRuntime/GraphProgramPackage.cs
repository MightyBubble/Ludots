namespace Ludots.Core.GraphRuntime
{
    public readonly record struct GraphProgramPackage(
        string GraphName,
        string[] Symbols,
        GraphInstruction[] Program,
        GraphKind Kind,
        MapTriggerGraphEntry[] MapTriggerEntries);
}
