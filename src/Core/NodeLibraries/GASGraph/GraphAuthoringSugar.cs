using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Script-only ControlFlow authoring sugar SSOT.
    /// These names are compile-time sugar (not <see cref="GraphNodeOp"/> values);
    /// they lower in <see cref="GraphControlFlowCompiler"/> to Jump / Yield / compares.
    /// </summary>
    public static class GraphAuthoringSugar
    {
        public const string BranchBool = "BranchBool";
        public const string SwitchInt = "SwitchInt";
        public const string Wait = "Wait";
        public const string While = "While";
        public const string Until = "Until";

        public static bool IsScriptOnlySugar(string? opName)
        {
            if (string.IsNullOrWhiteSpace(opName))
            {
                return false;
            }

            return string.Equals(opName, BranchBool, StringComparison.Ordinal) ||
                   string.Equals(opName, SwitchInt, StringComparison.Ordinal) ||
                   string.Equals(opName, Wait, StringComparison.Ordinal) ||
                   string.Equals(opName, While, StringComparison.Ordinal) ||
                   string.Equals(opName, Until, StringComparison.Ordinal);
        }

        public static string DescribeScriptOnlySugar()
            => $"{BranchBool}, {SwitchInt}, {Wait}, {While}, {Until}";
    }
}
