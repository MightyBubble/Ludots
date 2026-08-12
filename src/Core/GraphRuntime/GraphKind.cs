using System;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Authored graph execution contract. Parsed from authored graph kind and enforced
    /// at compile/load and at execution entrypoints that require a specific kind.
    /// L1 flow dialects today: Effect, Query, Score, Validation, Derived, Script.
    /// L2 behavior schedulers (BehaviorTree / Fsm / LevelTrigger) are not GraphKind
    /// values yet — they own separate topology IR and invoke L1 graphs at leaves.
    /// </summary>
    public enum GraphKind : byte
    {
        None = 0,
        Effect = 1,
        Query = 2,
        Score = 3,
        Validation = 4,
        Derived = 5,
        /// <summary>Reusable flow script: Call/Return/Yield and InvokeScript callee.</summary>
        Script = 6
    }

    public static class GraphKindParser
    {
        public static bool TryParse(string? value, out GraphKind kind)
        {
            kind = GraphKind.None;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (!Enum.TryParse(trimmed, ignoreCase: false, out kind) ||
                kind == GraphKind.None ||
                !Enum.IsDefined(typeof(GraphKind), kind) ||
                !string.Equals(kind.ToString(), trimmed, StringComparison.Ordinal))
            {
                kind = GraphKind.None;
                return false;
            }

            return true;
        }

        public static GraphKind ParseRequired(string? value, string graphId)
        {
            if (!TryParse(value, out GraphKind kind))
            {
                string shown = string.IsNullOrWhiteSpace(value) ? "<missing>" : value.Trim();
                throw new InvalidOperationException(
                    $"Graph '{graphId}' has unsupported or missing kind '{shown}'. Supported kinds: Effect, Query, Score, Validation, Derived, Script.");
            }

            return kind;
        }
    }
}
