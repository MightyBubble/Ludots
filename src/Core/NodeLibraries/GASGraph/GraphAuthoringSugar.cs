using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// ControlFlow authoring sugar SSOT. Kind gates live in <see cref="GraphControlFlowCompiler"/>
    /// (Wait/While/SwitchInt/Until/Break are Script/TriggerGraph; BranchBool also allows Effect).
    /// These names are compile-time sugar (not <see cref="GraphNodeOp"/> values);
    /// they lower in <see cref="GraphControlFlowCompiler"/> to Jump / Yield / compares.
    /// </summary>
    public static class GraphAuthoringSugar
    {
        public const string BranchBool = "BranchBool";
        public const string SwitchInt = "SwitchInt";
        /// <summary>
        /// Enum-driven value pick: selector(int) + one value input per bound member
        /// (case:{memberName}) + optional default, lowering to a ConstInt/CompareEqInt/
        /// JumpIfFalse/MoveInt chain. Produces an int; never a GraphNodeOp value.
        /// </summary>
        public const string SelectByEnum = "SelectByEnum";
        public const string Wait = "Wait";
        public const string While = "While";
        public const string Until = "Until";
        public const string Break = "Break";
        public const string BtSequence = "BtSequence";
        public const string BtSelector = "BtSelector";
        public const string BtDecorator = "BtDecorator";

        public static bool IsScriptOnlySugar(string? opName)
        {
            if (string.IsNullOrWhiteSpace(opName))
            {
                return false;
            }

            return string.Equals(opName, BranchBool, StringComparison.Ordinal) ||
                   string.Equals(opName, SwitchInt, StringComparison.Ordinal) ||
                   string.Equals(opName, SelectByEnum, StringComparison.Ordinal) ||
                   string.Equals(opName, Wait, StringComparison.Ordinal) ||
                   string.Equals(opName, While, StringComparison.Ordinal) ||
                   string.Equals(opName, Until, StringComparison.Ordinal) ||
                   string.Equals(opName, Break, StringComparison.Ordinal);
        }

        /// <summary>
        /// Behavior-tree composition sugar. Strictly Script-kind: the whole tree compiles into one
        /// Script program (Call/Return + CompareEqInt + JumpIfFalse; status channel 0/1/2 in an int
        /// register) driven by GraphBehaviorTreeHost. Never becomes a GraphNodeOp value.
        /// </summary>
        public static bool IsBtSugar(string? opName)
        {
            if (string.IsNullOrWhiteSpace(opName))
            {
                return false;
            }

            return string.Equals(opName, BtSequence, StringComparison.Ordinal) ||
                   string.Equals(opName, BtSelector, StringComparison.Ordinal) ||
                   string.Equals(opName, BtDecorator, StringComparison.Ordinal);
        }

        public static string DescribeScriptOnlySugar()
            => $"{BranchBool}, {SwitchInt}, {SelectByEnum}, {Wait}, {While}, {Until}, {Break}";

        public static string DescribeBtSugar()
            => $"{BtSequence}, {BtSelector}, {BtDecorator}";
    }
}
