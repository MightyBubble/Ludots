namespace Ludots.Core.Presentation.Instancing
{
    public readonly struct InstancedBatchBinding
    {
        public InstancedBatchBinding(int batchAssetId, int slotIndex = -1)
        {
            BatchAssetId = batchAssetId;
            SlotIndex = slotIndex;
        }

        public int BatchAssetId { get; }
        public int SlotIndex { get; }
    }
}
