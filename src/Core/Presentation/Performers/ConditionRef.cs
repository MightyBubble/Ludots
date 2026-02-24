namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// A reference to either an inline predicate or a Graph program that evaluates
    /// to a boolean result. Used by <see cref="PerformerRule"/> conditions and
    /// <see cref="PerformerDefinition.VisibilityCondition"/>.
    ///
    /// Evaluation priority:
    ///   1. If <see cref="Inline"/> != None → evaluate the inline predicate.
    ///   2. Else if <see cref="GraphProgramId"/> > 0 → execute the Graph program,
    ///      read B[0] as the boolean result.
    ///   3. Else (both default) → always true.
    /// </summary>
    public struct ConditionRef
    {
        /// <summary>
        /// Non-None: evaluate this inline predicate (fast path, no Graph overhead).
        /// </summary>
        public InlineConditionKind Inline;

        /// <summary>
        /// When Inline == None and this is > 0: execute the registered Graph program
        /// and read B[0] as the boolean result. Graph programs are registered in
        /// GraphProgramRegistry; the Performer system is a one-way consumer.
        /// </summary>
        public int GraphProgramId;

        /// <summary>A default ConditionRef that always evaluates to true.</summary>
        public static readonly ConditionRef AlwaysTrue = default;
    }
}
