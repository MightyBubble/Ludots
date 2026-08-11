using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Tests.Gas.Graph.Codegen
{
    /// <summary>
    /// Contract for R0 generated graph entrypoints (#860).
    /// Host-owned delegate type so Collectible ALC assemblies can bind without splitting Core types.
    /// </summary>
    public delegate void GraphGeneratedExecute(ref GraphExecutionState state);
}
