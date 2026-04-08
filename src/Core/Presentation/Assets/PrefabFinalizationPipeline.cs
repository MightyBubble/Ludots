using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public static class PrefabFinalizationPipeline
    {
        public const int DefaultMaxDepth = 6;

        public static void FinalizeLeaves(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabFinalizedLeafBuffer output,
            int maxDepth = DefaultMaxDepth)
        {
            if (meshes == null)
            {
                throw new System.ArgumentNullException(nameof(meshes));
            }

            if (output == null)
            {
                throw new System.ArgumentNullException(nameof(output));
            }

            FinalizeLeavesRecursive(meshes, meshAssetId, stableId, position, rotation, scale, color, output, depth: 0, maxDepth);
        }

        private static void FinalizeLeavesRecursive(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabFinalizedLeafBuffer output,
            int depth,
            int maxDepth)
        {
            if (depth > maxDepth || !meshes.TryGetDescriptor(meshAssetId, out var descriptor))
            {
                return;
            }

            if (descriptor.Type == MeshAssetType.Prefab &&
                descriptor.PrefabParts != null &&
                descriptor.PrefabParts.Length > 0)
            {
                for (int i = 0; i < descriptor.PrefabParts.Length; i++)
                {
                    ref var part = ref descriptor.PrefabParts[i];
                    PrefabTransformUtility.Compose(position, rotation, scale, in part, out Vector3 childPosition, out Quaternion childRotation, out Vector3 childScale);

                    var childColor = new Vector4(
                        color.X * part.ColorTint.X,
                        color.Y * part.ColorTint.Y,
                        color.Z * part.ColorTint.Z,
                        color.W * part.ColorTint.W);

                    int childStableId = PrefabTransformUtility.BuildChildStableId(stableId, depth, i, part.MeshAssetId);
                    FinalizeLeavesRecursive(
                        meshes,
                        part.MeshAssetId,
                        childStableId,
                        childPosition,
                        childRotation,
                        childScale,
                        childColor,
                        output,
                        depth + 1,
                        maxDepth);
                }

                return;
            }

            output.Add(new PrefabFinalizedLeaf(
                meshAssetId,
                descriptor,
                stableId,
                position,
                rotation,
                scale,
                color));
        }
    }
}
