namespace Ludots.Core.Presentation.Instancing
{
    public readonly struct InstancedBatchBinding
    {
        public InstancedBatchBinding(int batchAssetId)
        {
            BatchAssetId = batchAssetId;
        }

        public int BatchAssetId { get; }
    }
}
