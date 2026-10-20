namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Two symbol ids share one instruction Imm as 16-bit halves: key in the low half,
    /// the compared value or the parent context in the high half. Compile time packs
    /// symbol indices; the symbol patcher rewrites both halves to registered key ids.
    /// Both halves are required.
    /// </summary>
    internal static class PrefOpEncoding
    {
        private const int MaxKeyId = 0xFFFF;

        public static int Pack(int keyId, int otherId)
        {
            if ((uint)(keyId - 1) > MaxKeyId - 1 || (uint)(otherId - 1) > MaxKeyId - 1)
            {
                throw new System.InvalidOperationException(
                    $"Pref key ids out of range (key={keyId}, other={otherId}).");
            }

            return keyId | (otherId << 16);
        }

        /// <summary>
        /// Symbol-table indices start at 0, and 0 is not a legal packed half.
        /// Store index + 1 until the patcher replaces both halves with registry ids.
        /// </summary>
        public static int PackSymbolIndices(int keyIndex, int otherIndex)
        {
            if (keyIndex < 0 || otherIndex < 0)
            {
                throw new System.InvalidOperationException(
                    $"Pref symbol indices out of range (key={keyIndex}, other={otherIndex}).");
            }

            return Pack(keyIndex + 1, otherIndex + 1);
        }

        public static int UnpackKey(int imm) => imm & MaxKeyId;

        public static int UnpackOther(int imm) => (imm >> 16) & MaxKeyId;

        public static int UnpackSymbolKey(int imm) => UnpackKey(imm) - 1;

        public static int UnpackSymbolOther(int imm) => UnpackOther(imm) - 1;
    }
}
