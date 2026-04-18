using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerGroundingUtility
    {
        private const float MetersToCm = 100f;
        private const float CmToMeters = 0.01f;
        private const float GroundRayOriginMeters = 10000f;

        public static PerformerResolvedTransform ResolveTransform(
            in PerformerInstance instance,
            in PerformerInstance parent,
            bool hasParent,
            in VisualTransform ownerTransform,
            bool hasOwnerTransform,
            in AssetBindingConfig assetBinding,
            IVisualHeightmap? heightmap = null)
        {
            Vector3 basePosition;
            Quaternion baseRotation;
            Vector3 baseScale;

            switch (instance.TransformSource)
            {
                case TransformSource.InheritParent:
                    basePosition = hasParent ? parent.WorldPosition : instance.WorldPosition;
                    baseRotation = hasParent ? NormalizeOrIdentity(parent.WorldRotation) : NormalizeOrIdentity(instance.WorldRotation);
                    baseScale = hasParent ? NormalizeScale(parent.WorldScale) : NormalizeScale(instance.WorldScale);
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        assetBinding,
                        inheritScale: true,
                        instance.TransformSource,
                        heightmap);

                case TransformSource.EntityTransform:
                    basePosition = hasOwnerTransform ? ownerTransform.Position : instance.WorldPosition;
                    baseRotation = hasOwnerTransform ? NormalizeOrIdentity(ownerTransform.Rotation) : NormalizeOrIdentity(instance.WorldRotation);
                    baseScale = NormalizeScale(assetBinding.LocalScale);
                    return ApplyGrounding(
                        basePosition + assetBinding.LocalOffset,
                        NormalizeOrIdentity(baseRotation * NormalizeOrIdentity(assetBinding.LocalRotation)),
                        baseScale,
                        assetBinding,
                        instance.TransformSource,
                        heightmap);

                case TransformSource.SplineDriven:
                    basePosition = instance.WorldPosition;
                    baseRotation = NormalizeOrIdentity(instance.WorldRotation);
                    baseScale = NormalizeScale(assetBinding.LocalScale);
                    return ApplyGrounding(
                        basePosition + assetBinding.LocalOffset,
                        NormalizeOrIdentity(baseRotation * NormalizeOrIdentity(assetBinding.LocalRotation)),
                        baseScale,
                        assetBinding,
                        instance.TransformSource,
                        heightmap);

                case TransformSource.BoneAttached:
                    basePosition = instance.WorldPosition;
                    baseRotation = NormalizeOrIdentity(instance.WorldRotation);
                    baseScale = NormalizeScale(instance.WorldScale);
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        assetBinding,
                        inheritScale: true,
                        instance.TransformSource,
                        heightmap);

                case TransformSource.WorldFixed:
                    return ApplyGrounding(
                        instance.WorldPosition,
                        NormalizeOrIdentity(instance.WorldRotation),
                        NormalizeScale(assetBinding.LocalScale),
                        assetBinding,
                        instance.TransformSource,
                        heightmap);

                default:
                    throw new ArgumentOutOfRangeException(nameof(instance.TransformSource), instance.TransformSource, "Unsupported performer transform source.");
            }
        }

        public static void ResolveBatch(
            Span<Vector3> positions,
            Span<GroundingMode> modes,
            Span<float> offsets,
            IVisualHeightmap heightmap)
        {
            if (heightmap == null)
            {
                throw new ArgumentNullException(nameof(heightmap));
            }

            if (positions.Length != modes.Length || positions.Length != offsets.Length)
            {
                throw new ArgumentException("Performer grounding batch spans must have the same length.");
            }

            for (int i = 0; i < positions.Length; i++)
            {
                if (modes[i] == GroundingMode.None)
                {
                    continue;
                }

                positions[i] = modes[i] == GroundingMode.AlignToSurface
                    ? ResolveAlignedPosition(positions[i], offsets[i], heightmap, out _)
                    : ResolveSnappedPosition(positions[i], offsets[i], heightmap);
            }
        }

        public static Quaternion AlignUpToNormal(Quaternion rotation, Vector3 targetNormal)
        {
            Vector3 currentUp = Vector3.Transform(Vector3.UnitY, NormalizeOrIdentity(rotation));
            Quaternion alignment = CreateRotationBetween(currentUp, targetNormal);
            return NormalizeOrIdentity(alignment * NormalizeOrIdentity(rotation));
        }

        private static PerformerResolvedTransform ApplyLocalAndGrounding(
            in Vector3 basePosition,
            in Quaternion baseRotation,
            in Vector3 baseScale,
            in AssetBindingConfig assetBinding,
            bool inheritScale,
            TransformSource transformSource,
            IVisualHeightmap? heightmap)
        {
            Vector3 localScale = NormalizeScale(assetBinding.LocalScale);
            Vector3 resolvedScale = inheritScale ? baseScale * localScale : localScale;
            Vector3 localOffset = inheritScale ? baseScale * assetBinding.LocalOffset : assetBinding.LocalOffset;
            Vector3 position = basePosition + Vector3.Transform(localOffset, baseRotation);
            Quaternion rotation = NormalizeOrIdentity(baseRotation * NormalizeOrIdentity(assetBinding.LocalRotation));

            return ApplyGrounding(position, rotation, resolvedScale, assetBinding, transformSource, heightmap);
        }

        private static PerformerResolvedTransform ApplyGrounding(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            in AssetBindingConfig assetBinding,
            TransformSource transformSource,
            IVisualHeightmap? heightmap)
        {
            if (assetBinding.Grounding == GroundingMode.None || transformSource == TransformSource.BoneAttached)
            {
                return new PerformerResolvedTransform
                {
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                };
            }

            if (heightmap == null)
            {
                throw new InvalidOperationException("Performer grounding requires IVisualHeightmap.");
            }

            if (assetBinding.Grounding == GroundingMode.SnapToGround)
            {
                position = ResolveSnappedPosition(position, assetBinding.GroundingOffset, heightmap);
            }
            else if (assetBinding.Grounding == GroundingMode.AlignToSurface)
            {
                position = ResolveAlignedPosition(position, assetBinding.GroundingOffset, heightmap, out Vector3 normal);
                rotation = AlignUpToNormal(rotation, normal);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(assetBinding.Grounding), assetBinding.Grounding, "Unsupported performer grounding mode.");
            }

            return new PerformerResolvedTransform
            {
                Position = position,
                Rotation = rotation,
                Scale = scale,
            };
        }

        private static Vector3 ResolveSnappedPosition(Vector3 position, float offsetMeters, IVisualHeightmap heightmap)
        {
            if (!heightmap.TrySampleHeightCm(position.X * MetersToCm, position.Z * MetersToCm, out float heightCm))
            {
                throw new InvalidOperationException($"Performer grounding could not sample visual height at ({position.X}, {position.Z}) meters.");
            }

            if (float.IsNaN(heightCm) || float.IsInfinity(heightCm))
            {
                throw new InvalidOperationException($"Performer grounding received invalid visual height at ({position.X}, {position.Z}) meters.");
            }

            position.Y = heightCm * CmToMeters + offsetMeters;
            return position;
        }

        private static Vector3 ResolveAlignedPosition(Vector3 position, float offsetMeters, IVisualHeightmap heightmap, out Vector3 normal)
        {
            Span<float> originX = stackalloc float[1] { position.X };
            Span<float> originY = stackalloc float[1] { MathF.Max(position.Y + 1f, GroundRayOriginMeters) };
            Span<float> originZ = stackalloc float[1] { position.Z };
            Span<float> dirX = stackalloc float[1] { 0f };
            Span<float> dirY = stackalloc float[1] { -1f };
            Span<float> dirZ = stackalloc float[1] { 0f };
            Span<float> hitX = stackalloc float[1];
            Span<float> hitY = stackalloc float[1];
            Span<float> hitHeight = stackalloc float[1];
            Span<float> hitDistance = stackalloc float[1];
            Span<float> normalX = stackalloc float[1];
            Span<float> normalY = stackalloc float[1];
            Span<float> normalZ = stackalloc float[1];
            Span<int> layerIndex = stackalloc int[1];
            Span<byte> hitMask = stackalloc byte[1];

            if (!heightmap.RaycastGroundBatch(
                    originX,
                    originY,
                    originZ,
                    dirX,
                    dirY,
                    dirZ,
                    hitX,
                    hitY,
                    hitHeight,
                    hitDistance,
                    normalX,
                    normalY,
                    normalZ,
                    layerIndex,
                    hitMask))
            {
                throw new InvalidOperationException("Performer grounding requested normal alignment but the visual heightmap raycast batch failed.");
            }

            if (hitMask[0] == 0)
            {
                throw new InvalidOperationException("Performer grounding requested normal alignment but no ground hit was found.");
            }

            position.Y = hitHeight[0] * CmToMeters + offsetMeters;
            normal = NormalizeOrDefault(new Vector3(normalX[0], normalY[0], normalZ[0]), Vector3.UnitY);
            return position;
        }

        private static Quaternion CreateRotationBetween(Vector3 from, Vector3 to)
        {
            const float epsilon = 0.000001f;
            Vector3 fromNormalized = NormalizeOrDefault(from, Vector3.UnitY);
            Vector3 toNormalized = NormalizeOrDefault(to, Vector3.UnitY);
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

                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }

            Vector3 cross = Vector3.Cross(fromNormalized, toNormalized);
            float scale = MathF.Sqrt((1f + dot) * 2f);
            float invScale = 1f / scale;
            return NormalizeOrIdentity(new Quaternion(
                cross.X * invScale,
                cross.Y * invScale,
                cross.Z * invScale,
                scale * 0.5f));
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(value)
                : Quaternion.Identity;
        }

        private static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }

        private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
        {
            float lengthSquared = value.LengthSquared();
            return lengthSquared > 0.000001f
                ? value / MathF.Sqrt(lengthSquared)
                : fallback;
        }
    }

    public struct PerformerResolvedTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }
}
