using System;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Authored graph execution contract. Parsed from GraphConfig.Kind and enforced
    /// at compile/load and at execution entrypoints that require a specific kind.
    /// </summary>
    public enum GraphKind : byte
    {
        None = 0,
        Effect = 1,
        Query = 2,
        Score = 3,
        Validation = 4,
        Derived = 5
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

            if (!Enum.TryParse(value.Trim(), ignoreCase: false, out kind))
            {
                kind = GraphKind.None;
                return false;
            }

            return kind != GraphKind.None && Enum.IsDefined(typeof(GraphKind), kind);
        }

        public static GraphKind ParseRequired(string? value, string graphId)
        {
            if (!TryParse(value, out GraphKind kind))
            {
                string shown = string.IsNullOrWhiteSpace(value) ? "<missing>" : value.Trim();
                throw new InvalidOperationException(
                    $"Graph '{graphId}' has unsupported or missing kind '{shown}'. Supported kinds: Effect, Query, Score, Validation, Derived.");
            }

            return kind;
        }
    }
}
