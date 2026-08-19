using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// L1 authoring SSOT: every <see cref="GraphKind"/> uses ControlFlow edges;
    /// Kind only selects the authorable op matrix (see GraphControlFlowCompiler).
    /// </summary>
    public static class GraphAuthoringKindPolicy
    {
        public static bool IsControlFlowAuthoringKind(GraphKind kind)
            => kind is GraphKind.Script
                or GraphKind.Query
                or GraphKind.Effect
                or GraphKind.Score
                or GraphKind.Validation
                or GraphKind.Derived
                or GraphKind.MapTrigger;

        public static bool IsLinearAuthoringKind(GraphKind kind)
            => kind is GraphKind.Effect
                or GraphKind.Score
                or GraphKind.Validation
                or GraphKind.Derived;

        public static string DescribeSupportedKinds()
            => "Script, Query, Effect, Score, Validation, Derived, MapTrigger";
    }
}
