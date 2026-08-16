using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Presentation.Presenters
{
    public static class PresenterGroundingUtility
    {
        private const float MetersToCm = 100f;
        private const float CmToMeters = 0.01f;
        private const float GroundRayOriginMeters = 10000f;

        public static PresenterResolvedTransform ResolveTransform(
            in PresenterTransformSnapshot presenter,
            in PresenterTransformSnapshot parent,
            bool hasParent,
            in VisualTransform ownerTransform,
            bool hasOwnerTransform,
            in AssetBindingConfig assetBinding,
            in PresenterInstanceTransformOverride instanceOverride = default)
        {
            Vector3 basePosition;
            Quaternion baseRotation;
            Vector3 baseScale;
            PresenterWorldFacing baseFacing;

            switch (presenter.TransformSource)
            {
                case TransformSource.InheritParent:
                    basePosition = hasParent ? parent.WorldPosition : presenter.WorldPosition;
                    baseRotation = hasParent ? WorldPlane2D.NormalizeOrIdentity(parent.WorldRotation) : WorldPlane2D.NormalizeOrIdentity(presenter.WorldRotation);
                    baseScale = hasParent ? WorldPlane2D.NormalizeScale(parent.WorldScale) : WorldPlane2D.NormalizeScale(presenter.WorldScale);
                    baseFacing = hasParent ? parent.WorldFacing : presenter.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        instanceOverride,
                        inheritScale: true,
                        presenter.TransformSource);

                case TransformSource.EntityTransform:
                    basePosition = hasOwnerTransform ? ownerTransform.Position : presenter.WorldPosition;
                    baseRotation = hasOwnerTransform ? WorldPlane2D.NormalizeOrIdentity(ownerTransform.Rotation) : WorldPlane2D.NormalizeOrIdentity(presenter.WorldRotation);
                    baseScale = hasOwnerTransform ? WorldPlane2D.NormalizeScale(ownerTransform.Scale) : WorldPlane2D.NormalizeScale(presenter.WorldScale);
                    baseFacing = presenter.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        instanceOverride,
                        inheritScale: true,
                        presenter.TransformSource);

                case TransformSource.SplineDriven:
                    ComposeLocals(assetBinding, instanceOverride, out Vector3 splineOffset, out Quaternion splineRotation, out Vector3 splineScale);
                    basePosition = presenter.WorldPosition;
                    baseRotation = WorldPlane2D.NormalizeOrIdentity(presenter.WorldRotation);
                    baseScale = splineScale;
                    baseFacing = presenter.WorldFacing;
                    return CreateResolvedTransform(
                        WorldPlane2D.TransformVisualLocal(basePosition, baseRotation, Vector3.One, in splineOffset),
                        WorldPlane2D.ComposeVisualRotation(baseRotation, splineRotation),
                        baseScale,
                        baseFacing);

                case TransformSource.BoneAttached:
                case TransformSource.AttachedToParent:
                    basePosition = presenter.WorldPosition;
                    baseRotation = WorldPlane2D.NormalizeOrIdentity(presenter.WorldRotation);
                    baseScale = WorldPlane2D.NormalizeScale(presenter.WorldScale);
                    baseFacing = presenter.WorldFacing;
                    return ApplyLocalAndGrounding(
                        basePosition,
                        baseRotation,
                        baseScale,
                        baseFacing,
                        assetBinding,
                        instanceOverride,
                        inheritScale: true,
                        presenter.TransformSource);

                case TransformSource.WorldFixed:
                    ComposeLocals(assetBinding, instanceOverride, out Vector3 fixedOffset, out Quaternion fixedRotation, out Vector3 fixedScale);
                    Quaternion worldRotation = WorldPlane2D.ComposeVisualRotation(
                        WorldPlane2D.NormalizeOrIdentity(presenter.WorldRotation),
                        fixedRotation);
                    return CreateResolvedTransform(
                        WorldPlane2D.TransformVisualLocal(presenter.WorldPosition, worldRotation, Vector3.One, in fixedOffset),
                        worldRotation,
                        fixedScale,
                        presenter.WorldFacing);

                default:
                    throw new ArgumentOutOfRangeException(nameof(presenter.TransformSource), presenter.TransformSource, "Unsupported presenter transform source.");
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
                throw new ArgumentException("Presenter grounding batch spans must have the same length.");
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
                throw new ArgumentException("Presenter grounding batch spans must have the same length.");
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

        private static PresenterResolvedTransform ApplyLocalAndGrounding(
            in Vector3 basePosition,
            in Quaternion baseRotation,
            in Vector3 baseScale,
            in PresenterWorldFacing baseFacing,
            in AssetBindingConfig assetBinding,
            in PresenterInstanceTransformOverride instanceOverride,
            bool inheritScale,
            TransformSource transformSource)
        {
            ComposeLocals(assetBinding, instanceOverride, out Vector3 localOffset, out Quaternion localRotation, out Vector3 localScale);
            Vector3 resolvedScale = inheritScale ? baseScale * localScale : localScale;
            Vector3 parentScale = inheritScale ? baseScale : Vector3.One;
            Vector3 position = WorldPlane2D.TransformVisualLocal(basePosition, baseRotation, parentScale, in localOffset);
            Quaternion rotation = WorldPlane2D.ComposeVisualRotation(baseRotation, localRotation);

            return CreateResolvedTransform(position, rotation, resolvedScale, baseFacing);
        }

        private static void ComposeLocals(
            in AssetBindingConfig assetBinding,
            in PresenterInstanceTransformOverride instanceOverride,
            out Vector3 localOffset,
            out Quaternion localRotation,
            out Vector3 localScale)
        {
            localOffset = assetBinding.LocalOffset;
            localRotation = assetBinding.LocalRotation;
            localScale = WorldPlane2D.NormalizeScale(assetBinding.LocalScale);
            if (!instanceOverride.HasOverride)
            {
                return;
            }

            localOffset += instanceOverride.LocalPosition;
            localRotation = instanceOverride.LocalRotation * localRotation;
            localScale = WorldPlane2D.NormalizeScale(localScale * instanceOverride.LocalScale);
        }

        private static PresenterResolvedTransform CreateResolvedTransform(
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            in PresenterWorldFacing facing)
        {
            return new PresenterResolvedTransform
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

    public struct PresenterResolvedTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public PresenterWorldFacing Facing;
    }

    public struct PresenterTransformSnapshot
    {
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Vector3 WorldScale;
        public PresenterWorldFacing WorldFacing;
        public TransformSource TransformSource;
    }
}
