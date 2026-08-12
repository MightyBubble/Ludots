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

        [ThreadStatic]
        private static PrefabFinalizedVisualBuffer? s_visualOutput;

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
            PrefabFinalizedVisualBuffer visualOutput = s_visualOutput ??= new PrefabFinalizedVisualBuffer();
            FinalizeVisuals(
                meshes,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                context,
                visualOutput,
                maxDepth);

            ReadOnlySpan<PrefabFinalizedVisual> visuals = visualOutput.GetSpan();
            for (int i = 0; i < visuals.Length; i++)
            {
                ref readonly PrefabFinalizedVisual visual = ref visuals[i];
                if (visual.Kind != PrefabVisualPartKind.Mesh && visual.Kind != PrefabVisualPartKind.ProceduralMesh)
                {
                    continue;
                }

                output.Add(new PrefabFinalizedLeaf(
                    visual.MeshAssetId,
                    visual.MeshDescriptor,
                    visual.StableId,
                    visual.Position,
                    visual.Rotation,
                    visual.Scale,
                    visual.Color,
                    visual.MaterialId,
                    visual.MaterialBindings,
                    visual.LocalBounds));
            }
        }

        public static void FinalizeVisuals(
            MeshAssetRegistry meshes,
            int meshAssetId,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabFinalizedVisualBuffer output,
            int maxDepth = DefaultMaxDepth,
            int instanceMaterialOverrideId = 0)
        {
            FinalizeVisuals(
                meshes,
                meshAssetId,
                stableId,
                position,
                rotation,
                scale,
                color,
                PrefabFinalizationContext.Empty,
                output,
                maxDepth,
                instanceMaterialOverrideId);
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
            PrefabFinalizedVisualBuffer output,
            int maxDepth = DefaultMaxDepth,
            int instanceMaterialOverrideId = 0)
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
            FinalizeVisualsRecursive(
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
                maxDepth,
                instanceMaterialOverrideId);
        }

        private static void FinalizeVisualsRecursive(
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
            PrefabFinalizedVisualBuffer output,
            int depth,
            int maxDepth,
            int instanceMaterialOverrideId)
        {
            if (depth > maxDepth)
            {
                throw new InvalidOperationException(
                    $"Prefab finalization exceeded maxDepth={maxDepth} at meshAssetId={meshAssetId} stableId={stableId}.");
            }

            if (!meshes.TryGetDescriptor(meshAssetId, out var descriptor))
            {
                throw new InvalidOperationException(
                    $"Prefab finalization references unknown meshAssetId={meshAssetId} at stableId={stableId}.");
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
                    EmitOrRecursePart(
                        meshes,
                        descriptor,
                        in part,
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
                        maxDepth,
                        instanceMaterialOverrideId);
                }

                return;
            }

            EmitFinalizedMeshVisual(
                meshAssetId,
                descriptor,
                stableId,
                position,
                rotation,
                scale,
                color,
                output,
                instanceMaterialOverrideId);
        }

        private static void EmitOrRecursePart(
            MeshAssetRegistry meshes,
            in MeshAssetDescriptor parentDescriptor,
            in PrefabPart part,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            in PrefabFinalizationContext context,
            PrefabGroundingBatchBuffer groundingRequests,
            PrefabGroundingBatchContext groundingBatchContext,
            PrefabFinalizedVisualBuffer output,
            int depth,
            int maxDepth,
            int instanceMaterialOverrideId)
        {
            switch (part.Kind)
            {
                case PrefabVisualPartKind.Decal:
                    ValidatePartContract(part, stableId);
                    output.Add(PrefabFinalizedVisual.Decal(
                        stableId,
                        position,
                        rotation,
                        scale,
                        color,
                        part.MaterialId,
                        part.Size,
                        part.AlignToSurface));
                    return;

                case PrefabVisualPartKind.Vfx:
                    ValidatePartContract(part, stableId);
                    int vfxMaterialId = part.MaterialId > 0 ? part.MaterialId : instanceMaterialOverrideId;
                    output.Add(PrefabFinalizedVisual.Vfx(
                        stableId,
                        position,
                        rotation,
                        scale,
                        color,
                        part.EffectAssetId,
                        part.VfxSpawnMode,
                        vfxMaterialId));
                    return;

                case PrefabVisualPartKind.Surface:
                    ValidatePartContract(part, stableId);
                    if (!meshes.TryGetDescriptor(part.MeshAssetId, out MeshAssetDescriptor surfaceDescriptor))
                    {
                        throw new InvalidOperationException(
                            $"Prefab surface part stableId={stableId} references unknown meshAssetId={part.MeshAssetId}.");
                    }

                    output.Add(PrefabFinalizedVisual.Surface(
                        stableId,
                        position,
                        rotation,
                        scale,
                        color,
                        part.MeshAssetId,
                        surfaceDescriptor,
                        part.MaterialId,
                        part.Tiling,
                        part.TerrainFacing,
                        ResolveLocalBounds(in surfaceDescriptor)));
                    return;

                case PrefabVisualPartKind.ProceduralMesh:
                    ValidatePartContract(part, stableId);
                    if (!meshes.TryGetDescriptor(part.MeshAssetId, out MeshAssetDescriptor proceduralDescriptor))
                    {
                        throw new InvalidOperationException(
                            $"Prefab procedural mesh part stableId={stableId} references unknown meshAssetId={part.MeshAssetId}.");
                    }

                    int proceduralMaterialId = part.MaterialId > 0 ? part.MaterialId : instanceMaterialOverrideId;
                    EmitFinalizedMeshVisual(
                        part.MeshAssetId,
                        proceduralDescriptor,
                        stableId,
                        position,
                        rotation,
                        scale,
                        color,
                        output,
                        proceduralMaterialId);
                    return;

                case PrefabVisualPartKind.Mesh:
                default:
                    int childMaterialOverrideId = part.MaterialId > 0 ? part.MaterialId : instanceMaterialOverrideId;
                    FinalizeVisualsRecursive(
                        meshes,
                        part.MeshAssetId,
                        stableId,
                        position,
                        rotation,
                        scale,
                        color,
                        context,
                        groundingRequests,
                        groundingBatchContext,
                        output,
                        depth,
                        maxDepth,
                        childMaterialOverrideId);
                    return;
            }
        }

        private static void ValidatePartContract(in PrefabPart part, int stableId)
        {
            switch (part.Kind)
            {
                case PrefabVisualPartKind.Decal:
                    if (part.MaterialId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Prefab decal part stableId={stableId} must declare a positive materialId.");
                    }

                    return;

                case PrefabVisualPartKind.Vfx:
                    if (part.EffectAssetId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Prefab VFX part stableId={stableId} must declare a positive effectAssetId.");
                    }

                    return;

                case PrefabVisualPartKind.Surface:
                    if (part.MeshAssetId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Prefab surface part stableId={stableId} must declare a positive meshAssetId.");
                    }

                    if (part.MaterialId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Prefab surface part stableId={stableId} must declare a positive materialId.");
                    }

                    return;

                case PrefabVisualPartKind.ProceduralMesh:
                    if (part.MeshAssetId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Prefab procedural mesh part stableId={stableId} must declare a positive meshAssetId.");
                    }

                    return;
            }
        }

        private static void EmitFinalizedMeshVisual(
            int meshAssetId,
            in MeshAssetDescriptor descriptor,
            int stableId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector4 color,
            PrefabFinalizedVisualBuffer output,
            int instanceMaterialOverrideId = 0)
        {
            if (descriptor.Type == MeshAssetType.ProceduralMesh)
            {
                ProceduralMeshAssetData? procedural = descriptor.ProceduralMeshData;
                if (procedural == null)
                {
                    throw new InvalidOperationException($"Procedural mesh assetId={meshAssetId} is missing ProceduralMeshData.");
                }

                PrefabMaterialBinding[] bindings = BuildMaterialBindings(meshAssetId, procedural, instanceMaterialOverrideId);
                output.Add(PrefabFinalizedVisual.ProceduralMesh(
                    meshAssetId,
                    descriptor,
                    stableId,
                    position,
                    rotation,
                    scale,
                    color,
                    bindings,
                    procedural.LocalBounds));
                return;
            }

            output.Add(PrefabFinalizedVisual.Mesh(
                meshAssetId,
                descriptor,
                stableId,
                position,
                rotation,
                scale,
                color,
                instanceMaterialOverrideId,
                materialBindings: null,
                localBounds: ResolveLocalBounds(in descriptor)));
        }

        private static PrefabMaterialBinding[] BuildMaterialBindings(int meshAssetId, ProceduralMeshAssetData procedural, int instanceMaterialOverrideId)
        {
            if (procedural.SubmeshCount <= 0)
            {
                throw new InvalidOperationException($"Procedural mesh assetId={meshAssetId} must commit at least one submesh.");
            }

            if (instanceMaterialOverrideId > 0 && procedural.SubmeshCount > 1)
            {
                throw new InvalidOperationException(
                    $"Procedural mesh assetId={meshAssetId} uses {procedural.SubmeshCount} submeshes and cannot receive an instance material override.");
            }

            var bindings = new PrefabMaterialBinding[procedural.SubmeshCount];
            for (int i = 0; i < procedural.SubmeshCount; i++)
            {
                int materialId = instanceMaterialOverrideId > 0
                    ? instanceMaterialOverrideId
                    : procedural.Submeshes[i].MaterialAssetId;
                bindings[i] = new PrefabMaterialBinding(i, materialId);
            }

            return bindings;
        }

        private static ProceduralMeshBounds ResolveLocalBounds(in MeshAssetDescriptor descriptor)
        {
            if (descriptor.Type == MeshAssetType.ProceduralMesh && descriptor.ProceduralMeshData != null)
            {
                return descriptor.ProceduralMeshData.LocalBounds;
            }

            return new ProceduralMeshBounds(Vector3.Zero, Vector3.One * 0.5f);
        }
    }
}
