namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// ActivateContext encoding: the context profile key id and the optional parent context
    /// profile key id share the instruction Imm as two 16-bit halves (parent 0 = no parent
    /// constraint), mirroring the SetPanelAudience packing precedent. Compile time packs
    /// symbol indices; the symbol patcher rewrites both halves to registered key ids.
    /// </summary>
    internal static class ContextOpEncoding
    {
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
