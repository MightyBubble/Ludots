using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public struct PrefabPart
    {
        public int MeshAssetId;
        public Vector3 LocalPosition;
        public Vector3 LocalScale;
        public Vector4 ColorTint;

        public static PrefabPart Default(int meshAssetId)
        {
            return new PrefabPart
            {
                MeshAssetId = meshAssetId,
                LocalPosition = Vector3.Zero,
                LocalScale = Vector3.One,
                ColorTint = Vector4.One,
            };
        }
    }
}
