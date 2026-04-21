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

        public static void FinalizeVisuals(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            in PrefabFinalizationContext context,
            PrefabFinalizedLeafBuffer meshOutput,
            PrefabFinalizedVisualBuffer visualOutput,
            int maxDepth = DefaultMaxDepth)
        {
            if (meshes == null)
            {
                throw new System.ArgumentNullException(nameof(meshes));
            }

            if (meshOutput == null)
            {
                throw new System.ArgumentNullException(nameof(meshOutput));
            }

            if (visualOutput == null)
            {
                throw new System.ArgumentNullException(nameof(visualOutput));
            }

            FinalizeLeavesRecursive(meshes, meshAssetId, stableId, position, rotation, scale, color, context, meshOutput, visualOutput, depth: 0, maxDepth);
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
                throw new System.ArgumentNullException(nameof(meshes));
            }

            if (output == null)
            {
                throw new System.ArgumentNullException(nameof(output));
            }

            FinalizeLeavesRecursive(meshes, meshAssetId, stableId, position, rotation, scale, color, context, output, null, depth: 0, maxDepth);
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
            PrefabFinalizedLeafBuffer output,
            PrefabFinalizedVisualBuffer visualOutput,
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

                    int childStableId = PrefabTransformUtility.BuildChildStableId(stableId, depth, i, ResolvePartStableDiscriminator(in part));
                    PrefabGroundingUtility.Resolve(in part, in context, ref childPosition, ref childRotation, childStableId);

                    var childColor = new Vector4(
                        color.X * part.ColorTint.X,
                        color.Y * part.ColorTint.Y,
                        color.Z * part.ColorTint.Z,
                        color.W * part.ColorTint.W);

                    if (part.Kind != PrefabPartKind.Mesh)
                    {
                        if (visualOutput == null)
                        {
                            throw new System.InvalidOperationException(
                                $"Prefab part stableId={childStableId} kind={part.Kind} requires typed visual finalization.");
                        }

                        visualOutput.Add(new PrefabFinalizedVisual(
                            part.Kind,
                            part.MeshAssetId,
                            part.AssetKey,
                            childStableId,
                            childPosition,
                            childRotation,
                            childScale,
                            childColor,
                            part.Payload,
                            part.MaterialKey,
                            part.SurfaceLayerKey));
                        continue;
                    }

                    FinalizeLeavesRecursive(
                        meshes,
                        part.MeshAssetId,
                        childStableId,
                        childPosition,
                        childRotation,
                        childScale,
                        childColor,
                        context,
                        output,
                        visualOutput,
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

        private static int ResolvePartStableDiscriminator(in PrefabPart part)
        {
            if (part.Kind == PrefabPartKind.Mesh)
            {
                return part.MeshAssetId;
            }

            return System.HashCode.Combine((int)part.Kind, part.AssetKey, part.MaterialKey, part.SurfaceLayerKey);
        }
    }
}
