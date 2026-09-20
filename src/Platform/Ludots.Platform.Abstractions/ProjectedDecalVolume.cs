using System;
using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public readonly struct ProjectedDecalVolume : IEquatable<ProjectedDecalVolume>
    {
        public ProjectedDecalVolume(float stampWidthMeters, float stampDepthMeters, float projectionThicknessMeters)
        {
            StampWidthMeters = stampWidthMeters;
            StampDepthMeters = stampDepthMeters;
            ProjectionThicknessMeters = projectionThicknessMeters;
        }

        public float StampWidthMeters { get; }

        public float StampDepthMeters { get; }

        public Vector2 StampSizeMeters => new Vector2(StampWidthMeters, StampDepthMeters);

        public float ProjectionThicknessMeters { get; }

        public static ProjectedDecalVolume FromVisualScale(in Vector3 scale)
        {
            if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z))
            {
                throw new InvalidOperationException(
                    "Decal VisualProxy.Scale must be finite: X/Z are stamp meters, Y is projection thickness meters.");
            }

            float stampWidth = MathF.Abs(scale.X);
            float stampDepth = MathF.Abs(scale.Z);
            float thickness = MathF.Abs(scale.Y);
            if (stampWidth <= 0f || stampDepth <= 0f || thickness <= 0f)
            {
                throw new InvalidOperationException(
                    "Decal VisualProxy.Scale must be non-zero: X/Z are stamp meters, Y is projection thickness meters.");
            }

            return new ProjectedDecalVolume(stampWidth, stampDepth, thickness);
        }

        // Raylib stamps are a world-up box spun only about Y. Pitch/roll must fail at the adapter, not be silently dropped here.
        public bool TryBuildWorldToLocal(
            in Vector3 projectorCenter,
            float yawRad,
            out Matrix4x4 worldToDecal,
            out float minX,
            out float minY,
            out float minZ,
            out float maxX,
            out float maxY,
            out float maxZ)
        {
            Matrix4x4 decalToWorld =
                Matrix4x4.CreateScale(StampWidthMeters, ProjectionThicknessMeters, StampDepthMeters) *
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yawRad) *
                Matrix4x4.CreateTranslation(projectorCenter);
            if (!Matrix4x4.Invert(decalToWorld, out worldToDecal))
            {
                minX = minY = minZ = maxX = maxY = maxZ = 0f;
                return false;
            }

            minX = float.PositiveInfinity;
            minY = float.PositiveInfinity;
            minZ = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            maxY = float.NegativeInfinity;
            maxZ = float.NegativeInfinity;
            Span<float> signs = stackalloc float[2] { -0.5f, 0.5f };
            for (int ix = 0; ix < 2; ix++)
            {
                for (int iy = 0; iy < 2; iy++)
                {
                    for (int iz = 0; iz < 2; iz++)
                    {
                        Vector3 corner = Vector3.Transform(
                            new Vector3(signs[ix], signs[iy], signs[iz]),
                            decalToWorld);
                        minX = MathF.Min(minX, corner.X);
                        minY = MathF.Min(minY, corner.Y);
                        minZ = MathF.Min(minZ, corner.Z);
                        maxX = MathF.Max(maxX, corner.X);
                        maxY = MathF.Max(maxY, corner.Y);
                        maxZ = MathF.Max(maxZ, corner.Z);
                    }
                }
            }

            return true;
        }

        public readonly bool Equals(ProjectedDecalVolume other)
        {
            return StampWidthMeters.Equals(other.StampWidthMeters) &&
                   StampDepthMeters.Equals(other.StampDepthMeters) &&
                   ProjectionThicknessMeters.Equals(other.ProjectionThicknessMeters);
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is ProjectedDecalVolume other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(StampWidthMeters, StampDepthMeters, ProjectionThicknessMeters);
        }
    }
}
