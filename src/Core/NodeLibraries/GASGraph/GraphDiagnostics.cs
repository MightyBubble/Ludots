namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphDiagnosticSeverity : byte
    {
        Error = 1,
        Warning = 2
    }

    public readonly record struct GraphDiagnostic(GraphDiagnosticSeverity Severity, string Code, string Message, string GraphId, string? NodeId = null);

    public static class GraphDiagnosticCodes
    {
        public const string MissingGraphId = "GASG0001";
        public const string MissingEntry = "GASG0002";
        public const string DuplicateNodeId = "GASG0003";
        public const string UnknownNodeOp = "GASG0004";
        public const string MissingNodeRef = "GASG0005";
        public const string NextCycle = "GASG0006";
        public const string DataDependencyCycle = "GASG0007";
        public const string UnreachableNode = "GASG0008";
        public const string BudgetExceeded = "GASG0009";
        public const string TypeMismatch = "GASG0010";
    }
}

