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
        /// <summary>
        /// BT leaf portal: functionName points at a Script function graph (often an ActionLib
        /// target). BehaviorGraphLeafWeaver splices that Script into the host tree before
        /// compile so Yield may live in the leaf. Editor double-click opens the function graph.
        /// Never a GraphNodeOp.
        /// </summary>
        public const string BtLeaf = "BtLeaf";
        /// <summary>
        /// FSM state dispatch container: reads the map variable named by stateVar, then
        /// SwitchInt-style case:{memberName} arms per enum member (enumType required).
        /// Lowers to ReadMapVarInt + ConstInt/CompareEqInt/JumpIfFalse/Jump; the running
        /// VM only ever sees existing ops. Re-evaluation is the author's explicit
        /// TriggerGraph entry (MapVariableChanged + filters.varName), never a hidden poll.
        /// </summary>
        public const string FsmState = "FsmState";
        /// <summary>
        /// FSM arm portal: functionName points at a Script function graph for one state body.
        /// BehaviorGraphLeafWeaver splices it (HaltReturnInt kept — GraphFsmHost requires halt).
        /// Editor double-click opens the function graph. Never a GraphNodeOp.
        /// </summary>
        public const string FsmAction = "FsmAction";
        /// <summary>
        /// Compile-time macro splice (Unreal Macro style): replace this site with the
        /// body of another TriggerGraph so AwaitCallback/Yield may appear inside the
        /// reusable fragment. Never becomes a GraphNodeOp; runtime InvokeGraph stays sync-only.
        /// </summary>
        public const string InlineGraph = "InlineGraph";
        /// <summary>
        /// Formal-text authoring sugar: template in <c>text</c> with <c>{0}</c>/<c>{name}</c>
        /// holes becomes ConstText + ConcatText (brace ports are Text). Never a GraphNodeOp.
        /// </summary>
        public const string FormatText = "FormatText";

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
                   string.Equals(opName, Break, StringComparison.Ordinal) ||
                   IsFsmSugar(opName);
        }

        /// <summary>
        /// Behavior-tree composition sugar (Sequence/Selector/Decorator). Strictly Script-kind.
        /// BtLeaf is a portal sugar expanded before compile — see <see cref="IsBtLeafPortal"/>.
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

        public static bool IsBtLeafPortal(string? opName)
            => string.Equals(opName, BtLeaf, StringComparison.Ordinal);

        /// <summary>
        /// FSM dispatch sugar (FSM-1a). Script/TriggerGraph only: reads stateVar, then SwitchInt-style
        /// enum arms. Driven by GraphFsmHost; never a GraphNodeOp value.
        /// </summary>
        public static bool IsFsmSugar(string? opName)
        {
            if (string.IsNullOrWhiteSpace(opName))
            {
                return false;
            }

            return string.Equals(opName, FsmState, StringComparison.Ordinal);
        }

        public static bool IsFsmActionPortal(string? opName)
            => string.Equals(opName, FsmAction, StringComparison.Ordinal);

        public static string DescribeScriptOnlySugar()
            => $"{BranchBool}, {SwitchInt}, {SelectByEnum}, {Wait}, {While}, {Until}, {Break}, {FsmState}, {FsmAction}";

        public static string DescribeBtSugar()
            => $"{BtSequence}, {BtSelector}, {BtDecorator}, {BtLeaf}";

        public static string DescribeFsmSugar()
            => $"{FsmState}, {FsmAction}";
    }
}
