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
        public const string UnreachableNode = "GASG0008";
        public const string BudgetExceeded = "GASG0009";
        public const string TypeMismatch = "GASG0010";
        public const string UnsupportedGraphKind = "GASG0011";
        public const string MissingControlEdge = "GASG0012";
        public const string UnexpectedControlEdge = "GASG0013";
        public const string DuplicateControlEdge = "GASG0014";
        public const string MissingValueInput = "GASG0015";
        public const string DuplicateValueEdge = "GASG0016";
        public const string RegisterOutOfRange = "GASG0017";
        public const string UninitializedRegisterRead = "GASG0018";
        public const string EmptyGraph = "GASG0019";
        public const string MissingNodeId = "GASG0020";
        public const string RegisterAliasConflict = "GASG0021";
        public const string ForbiddenEntryTable = "GASG0022";
        public const string DuplicateEntryLabel = "GASG0023";
        public const string InvalidEntryFilters = "GASG0024";
        public const string InvalidEntryRefire = "GASG0025";
        public const string InvalidPanelAnchor = "GASG0026";
        public const string InvalidEntryHook = "GASG0027";
        public const string DuplicateAnchor = "GASG0028";
    }
}

