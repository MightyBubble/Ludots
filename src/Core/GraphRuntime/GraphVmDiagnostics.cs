namespace Ludots.Core.GraphRuntime
{
    public enum GraphVmDiagnosticSeverity : byte
    {
        Error = 1
    }

    public readonly record struct GraphVmDiagnostic(
        GraphVmDiagnosticSeverity Severity,
        string Code,
        string Message,
        string? NodeId = null);

    public static class GraphVmDiagnosticCodes
    {
        public const string MissingGraphId = "GVM0001";
        public const string MissingEntry = "GVM0002";
        public const string EmptyGraph = "GVM0003";
        public const string EntryMustBeFirstNode = "GVM0004";
        public const string DuplicateNodeId = "GVM0005";
        public const string UnknownOp = "GVM0006";
        public const string MissingTarget = "GVM0007";
        public const string MissingTargetNode = "GVM0008";
        public const string BudgetExceeded = "GVM0009";
        public const string MissingNodeId = "GVM0010";
        public const string DuplicateControlEdge = "GVM0011";
        public const string MissingControlEdge = "GVM0012";
        public const string UnexpectedControlEdge = "GVM0013";
        public const string DuplicateValueEdge = "GVM0014";
        public const string MissingValueInput = "GVM0015";
        public const string MissingValueSource = "GVM0016";
        public const string TypeMismatch = "GVM0017";
        public const string RegisterOutOfRange = "GVM0018";
    }
}
