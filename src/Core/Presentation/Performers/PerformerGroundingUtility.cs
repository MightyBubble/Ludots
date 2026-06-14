using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
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
            in PerformerTransformSnapshot performer,
            in PerformerTransformSnapshot parent,
            bool hasParent,
            in VisualTransform ownerTransform,
            bool hasOwnerTransform,
            in AssetBindingConfig assetBinding)
        {
            Vector3 basePosition;
            Quaternion baseRotation;
            Vector3 baseScale;
            PerformerWorldFacing baseFacing;

            switch (performer.TransformSource)
            {
                case TransformSource.InheritParent:
                    basePosition = hasParent ? parent.WorldPosition : performer.WorldPosition;
                    baseRotation = hasParent ? WorldPlane2D.NormalizeOrIdentity(parent.WorldRotation) : WorldPlane2D.NormalizeOrIdentity(performer.WorldRotation);
                    baseScale = hasParent ? WorldPlane2D.NormalizeScale(parent.WorldScale) : WorldPlane2D.NormalizeScale(performer.WorldScale);
                    baseFacing = hasParent ? parent.WorldFacing : performer.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        inheritScale: true,
                        performer.TransformSource);

                case TransformSource.EntityTransform:
                    basePosition = hasOwnerTransform ? ownerTransform.Position : performer.WorldPosition;
                    baseRotation = hasOwnerTransform ? WorldPlane2D.NormalizeOrIdentity(ownerTransform.Rotation) : WorldPlane2D.NormalizeOrIdentity(performer.WorldRotation);
                    baseScale = hasOwnerTransform ? WorldPlane2D.NormalizeScale(ownerTransform.Scale) : WorldPlane2D.NormalizeScale(performer.WorldScale);
                    baseFacing = performer.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        inheritScale: true,
                        performer.TransformSource);

                case TransformSource.SplineDriven:
                    basePosition = performer.WorldPosition;
                    baseRotation = WorldPlane2D.NormalizeOrIdentity(performer.WorldRotation);
                    baseScale = WorldPlane2D.NormalizeScale(assetBinding.LocalScale);
                    baseFacing = performer.WorldFacing;
                    return CreateResolvedTransform(
                        WorldPlane2D.TransformVisualLocal(basePosition, baseRotation, Vector3.One, in assetBinding.LocalOffset),
                        WorldPlane2D.ComposeVisualRotation(baseRotation, assetBinding.LocalRotation),
                        baseScale,
                        baseFacing);

                case TransformSource.BoneAttached:
                case TransformSource.AttachedToParent:
                    basePosition = performer.WorldPosition;
                    baseRotation = WorldPlane2D.NormalizeOrIdentity(performer.WorldRotation);
                    baseScale = WorldPlane2D.NormalizeScale(performer.WorldScale);
                    baseFacing = performer.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        inheritScale: true,
                        performer.TransformSource);

                case TransformSource.WorldFixed:
                    return CreateResolvedTransform(
                        performer.WorldPosition,
                        WorldPlane2D.NormalizeOrIdentity(performer.WorldRotation),
                        WorldPlane2D.NormalizeScale(assetBinding.LocalScale),
                        performer.WorldFacing);

                default:
                    throw new ArgumentOutOfRangeException(nameof(performer.TransformSource), performer.TransformSource, "Unsupported performer transform source.");
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

        public static void ResolveBatch(
            Span<Vector3> positions,
            Span<Quaternion> rotations,
            Span<GroundingMode> modes,
            Span<float> offsets,
            IVisualHeightmap heightmap)
        {
            if (heightmap == null)
            {
                throw new ArgumentNullException(nameof(heightmap));
            }

            if (positions.Length != rotations.Length ||
                positions.Length != modes.Length ||
                positions.Length != offsets.Length)
            {
                throw new ArgumentException("Performer grounding batch spans must have the same length.");
            }

            for (int i = 0; i < positions.Length; i++)
            {
                if (modes[i] == GroundingMode.None)
                {
                    continue;
                }

                if (modes[i] == GroundingMode.AlignToSurface)
                {
                    positions[i] = ResolveAlignedPosition(positions[i], offsets[i], heightmap, out Vector3 normal);
                    rotations[i] = AlignUpToNormal(rotations[i], normal);
                }
                else
                {
                    positions[i] = ResolveSnappedPosition(positions[i], offsets[i], heightmap);
                }
            }
        }

        public static Quaternion AlignUpToNormal(Quaternion rotation, Vector3 targetNormal)
        {
            Vector3 currentUp = Vector3.Transform(Vector3.UnitY, WorldPlane2D.NormalizeOrIdentity(rotation));
            Quaternion alignment = CreateRotationBetween(currentUp, targetNormal);
            return WorldPlane2D.NormalizeOrIdentity(alignment * WorldPlane2D.NormalizeOrIdentity(rotation));
        }

        private static PerformerResolvedTransform ApplyLocalAndGrounding(
            in Vector3 basePosition,
            in Quaternion baseRotation,
            in Vector3 baseScale,
            in PerformerWorldFacing baseFacing,
            in AssetBindingConfig assetBinding,
            bool inheritScale,
            TransformSource transformSource)
        {
            Vector3 localScale = WorldPlane2D.NormalizeScale(assetBinding.LocalScale);
            Vector3 resolvedScale = inheritScale ? baseScale * localScale : localScale;
            Vector3 parentScale = inheritScale ? baseScale : Vector3.One;
            Vector3 position = WorldPlane2D.TransformVisualLocal(basePosition, baseRotation, parentScale, in assetBinding.LocalOffset);
            Quaternion rotation = WorldPlane2D.ComposeVisualRotation(baseRotation, assetBinding.LocalRotation);

            if (transformSource == TransformSource.BoneAttached ||
                transformSource == TransformSource.AttachedToParent)
            {
                resolvedScale = WorldPlane2D.NormalizeScale(baseScale);
            }

            return CreateResolvedTransform(position, rotation, resolvedScale, baseFacing);
        }

        private static PerformerResolvedTransform CreateResolvedTransform(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            in PerformerWorldFacing facing)
        {
            return new PerformerResolvedTransform
            {
                Position = position,
                Rotation = rotation,
                Scale = scale,
                Facing = facing,
            };
        }

        private static Vector3 ResolveSnappedPosition(Vector3 position, float offsetMeters, IVisualHeightmap heightmap)
        {
            if (!heightmap.TrySampleHeightCm(position.X * MetersToCm, position.Z * MetersToCm, out float heightCm))
            {
                position.Y = offsetMeters;
                return position;
            }

            if (float.IsNaN(heightCm) || float.IsInfinity(heightCm))
            {
                position.Y = offsetMeters;
                return position;
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
                position.Y = offsetMeters;
                normal = Vector3.UnitY;
                return position;
            }

            if (hitMask[0] == 0)
            {
                position.Y = offsetMeters;
                normal = Vector3.UnitY;
                return position;
            }

            position.Y = hitHeight[0] * CmToMeters + offsetMeters;
            normal = NormalizeOrAxis(new Vector3(normalX[0], normalY[0], normalZ[0]), Vector3.UnitY);
            return position;
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

                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }

            Vector3 cross = Vector3.Cross(fromNormalized, toNormalized);
            float scale = MathF.Sqrt((1f + dot) * 2f);
            float invScale = 1f / scale;
            return WorldPlane2D.NormalizeOrIdentity(new Quaternion(
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

    public struct PerformerResolvedTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public PerformerWorldFacing Facing;
    }

    public struct PerformerTransformSnapshot
    {
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Vector3 WorldScale;
        public PerformerWorldFacing WorldFacing;
        public TransformSource TransformSource;
    }
}
