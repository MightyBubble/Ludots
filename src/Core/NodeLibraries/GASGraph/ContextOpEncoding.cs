namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// ActivateContext encoding: the context profile key id and the optional parent context
    /// profile key id share the instruction Imm as two 16-bit halves, mirroring the
    /// SetPanelAudience packing precedent. Compile time packs symbol indices; the symbol
    /// patcher rewrites both halves to registered key ids.
    /// Parent 0 is an explicit "no parent" constraint. <see cref="OwnMountParent"/> is not a
    /// key id: the runtime resolves it to the context mount that is executing the graph, and
    /// fails when the graph is not running inside a mount. A real parent key therefore cannot
    /// use 0xFFFF.
    /// </summary>
    internal static class ContextOpEncoding
    {
        public const int OwnMountParent = 0xFFFF;

        /// <summary>
        /// Graph symbol for an explicit unconstrained parent. Omitted <c>parentContext</c>
        /// means <see cref="OwnMountParent"/>; graphs that activate a root outside any mount
        /// name this token instead.
        /// </summary>
        public const string UnconstrainedParent = "core.graph.parent.unconstrained";

        private const int MaxKeyId = 0xFFFF;

        public static int Pack(int contextKeyId, int parentContextKeyId)
        {
            if ((uint)(contextKeyId - 1) > MaxKeyId - 1 || (uint)parentContextKeyId > MaxKeyId)
            {
                throw new System.InvalidOperationException(
                    $"ActivateContext key ids out of range (context={contextKeyId}, parent={parentContextKeyId}).");
            }

            return contextKeyId | (parentContextKeyId << 16);
        }

        public static int UnpackContext(int imm) => imm & MaxKeyId;

        public static int UnpackParent(int imm) => (imm >> 16) & MaxKeyId;
    }
}
