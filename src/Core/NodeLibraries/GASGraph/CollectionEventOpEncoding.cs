namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// DispatchCollectionEvent encoding: the event key id and the collection key id share
    /// the instruction Imm as two 16-bit halves (same packing family as
    /// <see cref="UI.PanelHosting.PanelOpEncoding"/> and the context ops). Compile time packs
    /// symbol indices; the symbol patcher rewrites both halves to registered key ids.
    /// </summary>
    internal static class CollectionEventOpEncoding
    {
        private const int MaxKeyId = 0xFFFF;

        public static int Pack(int eventKeyId, int collectionKeyId)
        {
            if ((uint)(eventKeyId - 1) > MaxKeyId - 1 || (uint)(collectionKeyId - 1) > MaxKeyId - 1)
            {
                throw new System.InvalidOperationException(
                    $"DispatchCollectionEvent key ids out of range (event={eventKeyId}, collection={collectionKeyId}).");
            }

            return eventKeyId | (collectionKeyId << 16);
        }

        public static int UnpackEventKey(int imm) => imm & MaxKeyId;

        public static int UnpackCollectionKey(int imm) => (imm >> 16) & MaxKeyId;
    }
}
