namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabMaterialBinding
    {
        public PrefabMaterialBinding(int submeshIndex, int materialAssetId)
        {
            SubmeshIndex = submeshIndex;
            MaterialAssetId = materialAssetId;
        }

        public int SubmeshIndex { get; }

        public int MaterialAssetId { get; }
    }
}
