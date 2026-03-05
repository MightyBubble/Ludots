namespace Ludots.Core.Presentation.Assets
{
    public struct MeshAssetDescriptor
    {
        public int Id;
        public MeshAssetType Type;
        public PrimitiveMeshKind PrimitiveKind;
        public string[] SourceUris;
        public PrefabPart[] PrefabParts;

        public static MeshAssetDescriptor Primitive(int id, PrimitiveMeshKind kind)
        {
            return new MeshAssetDescriptor
            {
                Id = id,
                Type = MeshAssetType.Primitive,
                PrimitiveKind = kind,
            };
        }

        public static MeshAssetDescriptor Model(int id, params string[] sourceUris)
        {
            return new MeshAssetDescriptor
            {
                Id = id,
                Type = MeshAssetType.Model,
                SourceUris = sourceUris,
            };
        }

        public static MeshAssetDescriptor Prefab(int id, params PrefabPart[] parts)
        {
            return new MeshAssetDescriptor
            {
                Id = id,
                Type = MeshAssetType.Prefab,
                PrefabParts = parts,
            };
        }
    }
}
