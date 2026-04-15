using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public static class PrefabFinalizationPipeline
    {
        public const int DefaultMaxDepth = 6;

        [ThreadStatic]
        private static PrefabGroundingBatchBuffer? s_groundingRequests;

        [ThreadStatic]
        private static PrefabGroundingBatchContext? s_groundingBatchContext;

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
            FinalizeLeaves(
                meshes,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                PrefabFinalizationContext.Empty,
                output,
                maxDepth);
        }

        public static void FinalizeLeaves(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            in PrefabFinalizationContext context,
            PrefabFinalizedLeafBuffer output,
            int maxDepth = DefaultMaxDepth)
        {
            if (meshes == null)
            {
                throw new ArgumentNullException(nameof(meshes));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            PrefabGroundingBatchBuffer groundingRequests = s_groundingRequests ??= new PrefabGroundingBatchBuffer();
            PrefabGroundingBatchContext groundingBatchContext = s_groundingBatchContext ??= new PrefabGroundingBatchContext();
            FinalizeLeavesRecursive(
                meshes,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                context,
                groundingRequests,
                groundingBatchContext,
                output,
                depth: 0,
                maxDepth);
        }

        private static void FinalizeLeavesRecursive(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            in PrefabFinalizationContext context,
            PrefabGroundingBatchBuffer groundingRequests,
            PrefabGroundingBatchContext groundingBatchContext,
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
                groundingRequests.Clear();
                int childCount = descriptor.PrefabParts.Length;
                var childPositions = new Vector3[childCount];
                var childRotations = new Quaternion[childCount];
                var childScales = new Vector3[childCount];
                var childColors = new Vector4[childCount];
                var childStableIds = new int[childCount];

                for (int i = 0; i < childCount; i++)
                {
                    ref var part = ref descriptor.PrefabParts[i];
                    PrefabTransformUtility.Compose(position, rotation, scale, in part, out Vector3 childPosition, out Quaternion childRotation, out Vector3 childScale);

                    childPositions[i] = childPosition;
                    childRotations[i] = childRotation;
                    childScales[i] = childScale;
                    childStableIds[i] = PrefabTransformUtility.BuildChildStableId(stableId, depth, i, part.MeshAssetId);
                    childColors[i] = new Vector4(
                        color.X * part.ColorTint.X,
                        color.Y * part.ColorTint.Y,
                        color.Z * part.ColorTint.Z,
                        color.W * part.ColorTint.W);

                    if (part.Grounding.RequiresVisualHeightmap)
                    {
                        groundingRequests.Add(new PrefabGroundingRequest
                        {
                            MeshAssetId = part.MeshAssetId,
                            StableId = childStableIds[i],
                            Grounding = part.Grounding,
                            Position = childPosition,
                            Rotation = childRotation,
                        });
                    }
                }

                PrefabGroundingUtility.ResolveBatch(groundingRequests, groundingBatchContext, in context);

                for (int i = 0; i < groundingRequests.Count; i++)
                {
                    ref readonly PrefabGroundingRequest request = ref groundingRequests[i];
                    for (int childIndex = 0; childIndex < childCount; childIndex++)
                    {
                        if (childStableIds[childIndex] != request.StableId)
                        {
                            continue;
                        }

                        childPositions[childIndex] = request.Position;
                        childRotations[childIndex] = request.Rotation;
                        break;
                    }
                }

                for (int i = 0; i < childCount; i++)
                {
                    ref var part = ref descriptor.PrefabParts[i];
                    FinalizeLeavesRecursive(
                        meshes,
                        part.MeshAssetId,
                        childStableIds[i],
                        childPositions[i],
                        childRotations[i],
                        childScales[i],
                        childColors[i],
                        context,
                        groundingRequests,
                        groundingBatchContext,
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
