using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    internal static class PrefabGroundingUtility
    {
        private const float MetersToCm = 100f;
        private const float CmToMeters = 0.01f;
        private const float GroundRayOriginMeters = 10000f;

        public static void ResolveBatch(
            PrefabGroundingBatchBuffer requests,
            PrefabGroundingBatchContext batchContext,
            in PrefabFinalizationContext context)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (batchContext == null)
            {
                throw new ArgumentNullException(nameof(batchContext));
            }

            if (requests.Count == 0)
            {
                return;
            }

            batchContext.EnsureCapacity(requests.Count);
            Array.Clear(batchContext.Processed, 0, requests.Count);

            IVisualHeightmap? heightmap = context.VisualHeightmap;
            if (heightmap == null)
            {
                ref readonly PrefabGroundingRequest first = ref requests[0];
                throw new InvalidOperationException(
                    $"Prefab part meshAssetId={first.MeshAssetId} stableId={first.StableId} requests visual grounding but '{nameof(IVisualHeightmap)}' is unavailable.");
            }

            for (int i = 0; i < requests.Count; i++)
            {
                ref readonly PrefabGroundingRequest request = ref requests[i];
                if (batchContext.Processed[i] || !request.Grounding.RequiresVisualHeightmap)
                {
                    continue;
                }

                if (request.Grounding.AlignToGroundNormal)
                {
                    ResolveNormalAlignedGroup(requests, batchContext, heightmap, request.Grounding.LayerIndex, i);
                }
                else
                {
                    ResolveSampledGroup(requests, batchContext, heightmap, request.Grounding.LayerIndex, i);
                }
            }
        }

        private static void ResolveSampledGroup(
            PrefabGroundingBatchBuffer requests,
            PrefabGroundingBatchContext batchContext,
            IVisualHeightmap heightmap,
            int layerIndex,
            int startIndex)
        {
            ref readonly PrefabGroundingRequest start = ref requests[startIndex];
            if (batchContext.Processed[startIndex] ||
                !start.Grounding.RequiresVisualHeightmap ||
                start.Grounding.AlignToGroundNormal ||
                start.Grounding.LayerIndex != layerIndex)
            {
                return;
            }

            int count = 0;
            for (int i = startIndex; i < requests.Count; i++)
            {
                ref readonly PrefabGroundingRequest request = ref requests[i];
                if (batchContext.Processed[i] ||
                    !request.Grounding.RequiresVisualHeightmap ||
                    request.Grounding.AlignToGroundNormal ||
                    request.Grounding.LayerIndex != layerIndex)
                {
                    continue;
                }

                batchContext.RequestIndices[count] = i;
                batchContext.XsCm[count] = request.Position.X * MetersToCm;
                batchContext.YsCm[count] = request.Position.Z * MetersToCm;
                batchContext.Processed[i] = true;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            if (!heightmap.SampleHeightsCm(
                    batchContext.XsCm.AsSpan(0, count),
                    batchContext.YsCm.AsSpan(0, count),
                    batchContext.HeightsCm.AsSpan(0, count),
                    layerIndex))
            {
                throw new InvalidOperationException(
                    $"Prefab part meshAssetId={start.MeshAssetId} stableId={start.StableId} requests visual grounding, but the visual heightmap layer {layerIndex} is unavailable.");
            }

            for (int i = 0; i < count; i++)
            {
                ref PrefabGroundingRequest request = ref requests[batchContext.RequestIndices[i]];
                float heightCm = batchContext.HeightsCm[i];
                if (float.IsNaN(heightCm) || float.IsInfinity(heightCm))
                {
                    throw new InvalidOperationException(
                        $"Prefab part meshAssetId={request.MeshAssetId} stableId={request.StableId} requests visual grounding, but the visual heightmap sample is unavailable at ({batchContext.XsCm[i]}, {batchContext.YsCm[i]}) cm.");
                }

                request.Position.Y = heightCm * CmToMeters + request.Grounding.VerticalOffsetMeters;
                request.Grounding = PrefabPartGrounding.None;
            }
        }

        private static void ResolveNormalAlignedGroup(
            PrefabGroundingBatchBuffer requests,
            PrefabGroundingBatchContext batchContext,
            IVisualHeightmap heightmap,
            int layerIndex,
            int startIndex)
        {
            ref readonly PrefabGroundingRequest start = ref requests[startIndex];
            if (batchContext.Processed[startIndex] ||
                !start.Grounding.RequiresVisualHeightmap ||
                !start.Grounding.AlignToGroundNormal ||
                start.Grounding.LayerIndex != layerIndex)
            {
                return;
            }

            int count = 0;
            for (int i = startIndex; i < requests.Count; i++)
            {
                ref readonly PrefabGroundingRequest request = ref requests[i];
                if (batchContext.Processed[i] ||
                    !request.Grounding.RequiresVisualHeightmap ||
                    !request.Grounding.AlignToGroundNormal ||
                    request.Grounding.LayerIndex != layerIndex)
                {
                    continue;
                }

                batchContext.RequestIndices[count] = i;
                batchContext.OriginXMeters[count] = request.Position.X;
                batchContext.OriginYMeters[count] = MathF.Max(request.Position.Y + 1f, GroundRayOriginMeters);
                batchContext.OriginZMeters[count] = request.Position.Z;
                batchContext.DirectionX[count] = 0f;
                batchContext.DirectionY[count] = -1f;
                batchContext.DirectionZ[count] = 0f;
                batchContext.Processed[i] = true;
                count++;
            }

            if (count == 0)
            {
                return;
            }

            if (!heightmap.RaycastGroundBatch(
                    batchContext.OriginXMeters.AsSpan(0, count),
                    batchContext.OriginYMeters.AsSpan(0, count),
                    batchContext.OriginZMeters.AsSpan(0, count),
                    batchContext.DirectionX.AsSpan(0, count),
                    batchContext.DirectionY.AsSpan(0, count),
                    batchContext.DirectionZ.AsSpan(0, count),
                    batchContext.HitWorldXCm.AsSpan(0, count),
                    batchContext.HitWorldYCm.AsSpan(0, count),
                    batchContext.HitHeightCm.AsSpan(0, count),
                    batchContext.HitDistanceMeters.AsSpan(0, count),
                    batchContext.HitNormalX.AsSpan(0, count),
                    batchContext.HitNormalY.AsSpan(0, count),
                    batchContext.HitNormalZ.AsSpan(0, count),
                    batchContext.HitLayerIndex.AsSpan(0, count),
                    batchContext.HitMask.AsSpan(0, count),
                    layerIndex))
            {
                throw new InvalidOperationException(
                    $"Prefab part meshAssetId={start.MeshAssetId} stableId={start.StableId} requested grounded normal alignment, but the visual heightmap layer {layerIndex} is unavailable.");
            }

            for (int i = 0; i < count; i++)
            {
                ref PrefabGroundingRequest request = ref requests[batchContext.RequestIndices[i]];
                if (batchContext.HitMask[i] == 0)
                {
                    throw new InvalidOperationException(
                        $"Prefab part meshAssetId={request.MeshAssetId} stableId={request.StableId} requested grounded normal alignment, but the visual heightmap could not resolve a ground hit.");
                }

                request.Position.Y = batchContext.HitHeightCm[i] * CmToMeters + request.Grounding.VerticalOffsetMeters;
                Vector3 hitNormal = new Vector3(
                    batchContext.HitNormalX[i],
                    batchContext.HitNormalY[i],
                    batchContext.HitNormalZ[i]);
                request.Rotation = AlignUpToNormal(request.Rotation, hitNormal);
                request.Grounding = PrefabPartGrounding.None;
            }
        }

        private static Quaternion AlignUpToNormal(Quaternion rotation, Vector3 targetNormal)
        {
            Vector3 currentUp = Vector3.Transform(Vector3.UnitY, WorldPlane2D.NormalizeOrIdentity(rotation));
            Quaternion alignment = CreateRotationBetween(currentUp, targetNormal);
            return WorldPlane2D.NormalizeOrIdentity(alignment * WorldPlane2D.NormalizeOrIdentity(rotation));
        }

        private static Quaternion CreateRotationBetween(Vector3 from, Vector3 to)
        {
            const float epsilon = 0.000001f;
            Vector3 fromNormalized = NormalizeOrAxis(from, Vector3.UnitY);
            Vector3 toNormalized = NormalizeOrAxis(to, Vector3.UnitY);
            float dot = Math.Clamp(Vector3.Dot(fromNormalized, toNormalized), -1f, 1f);

            if (dot >= 1f - epsilon)
            {
                return Quaternion.Identity;
            }

            if (dot <= -1f + epsilon)
            {
                Vector3 axis = Vector3.Cross(fromNormalized, Vector3.UnitX);
                if (axis.LengthSquared() <= epsilon)
                {
                    axis = Vector3.Cross(fromNormalized, Vector3.UnitZ);
                }

                axis = Vector3.Normalize(axis);
                return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
            }

            Vector3 cross = Vector3.Cross(fromNormalized, toNormalized);
            float scale = MathF.Sqrt((1f + dot) * 2f);
            float invScale = 1f / scale;
            return Quaternion.Normalize(new Quaternion(
                cross.X * invScale,
                cross.Y * invScale,
                cross.Z * invScale,
                scale * 0.5f));
        }

        private static Vector3 NormalizeOrAxis(Vector3 value, Vector3 axis)
        {
            float lengthSquared = value.LengthSquared();
            return lengthSquared > 0.000001f
                ? value / MathF.Sqrt(lengthSquared)
                : axis;
        }
    }
}
