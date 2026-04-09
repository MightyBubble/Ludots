using System;
using System.Numerics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    internal static class PrefabGroundingUtility
    {
        private const float MetersToCm = 100f;
        private const float CmToMeters = 0.01f;
        private const float GroundRayOriginMeters = 10000f;
        private const float AlignmentEpsilon = 0.000001f;

        public static void Resolve(
            in PrefabPart part,
            in PrefabFinalizationContext context,
            ref Vector3 position,
            ref Quaternion rotation,
            int stableId)
        {
            if (!part.Grounding.RequiresVisualHeightmap)
            {
                return;
            }

            IVisualHeightmap? heightmap = context.VisualHeightmap;
            if (heightmap == null)
            {
                throw new InvalidOperationException(
                    $"Prefab part meshAssetId={part.MeshAssetId} stableId={stableId} requests visual grounding but '{nameof(IVisualHeightmap)}' is unavailable.");
            }

            float worldXCm = position.X * MetersToCm;
            float worldYCm = position.Z * MetersToCm;

            if (part.Grounding.AlignToGroundNormal)
            {
                var ray = new ScreenRay(
                    new Vector3(position.X, MathF.Max(position.Y + 1f, GroundRayOriginMeters), position.Z),
                    -Vector3.UnitY);
                if (!heightmap.TryRaycastGround(in ray, out VisualGroundHit hit, part.Grounding.LayerIndex))
                {
                    throw new InvalidOperationException(
                        $"Prefab part meshAssetId={part.MeshAssetId} stableId={stableId} requested grounded normal alignment, but the visual heightmap could not resolve a ground hit.");
                }

                position.Y = hit.HeightCm * CmToMeters + part.Grounding.VerticalOffsetMeters;
                rotation = AlignUpToNormal(rotation, hit.Normal);
                return;
            }

            if (!heightmap.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm, part.Grounding.LayerIndex))
            {
                throw new InvalidOperationException(
                    $"Prefab part meshAssetId={part.MeshAssetId} stableId={stableId} requests visual grounding, but the visual heightmap sample is unavailable at ({worldXCm}, {worldYCm}) cm.");
            }

            position.Y = heightCm * CmToMeters + part.Grounding.VerticalOffsetMeters;
        }

        private static Quaternion AlignUpToNormal(Quaternion rotation, Vector3 targetNormal)
        {
            Vector3 currentUp = Vector3.Transform(Vector3.UnitY, PrefabTransformUtility.NormalizeOrIdentity(rotation));
            Quaternion alignment = CreateRotationBetween(currentUp, targetNormal);
            return Quaternion.Normalize(alignment * PrefabTransformUtility.NormalizeOrIdentity(rotation));
        }

        private static Quaternion CreateRotationBetween(Vector3 from, Vector3 to)
        {
            Vector3 fromNormalized = NormalizeOrDefault(from, Vector3.UnitY);
            Vector3 toNormalized = NormalizeOrDefault(to, Vector3.UnitY);
            float dot = Math.Clamp(Vector3.Dot(fromNormalized, toNormalized), -1f, 1f);

            if (dot >= 1f - AlignmentEpsilon)
            {
                return Quaternion.Identity;
            }

            if (dot <= -1f + AlignmentEpsilon)
            {
                Vector3 axis = Vector3.Cross(fromNormalized, Vector3.UnitX);
                if (axis.LengthSquared() <= AlignmentEpsilon)
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

        private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
        {
            float lengthSquared = value.LengthSquared();
            return lengthSquared > AlignmentEpsilon
                ? value / MathF.Sqrt(lengthSquared)
                : fallback;
        }
    }
}
