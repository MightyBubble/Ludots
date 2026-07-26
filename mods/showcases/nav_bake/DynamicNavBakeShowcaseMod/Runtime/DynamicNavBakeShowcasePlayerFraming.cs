using System;
using System.Numerics;
using Ludots.Core.Gameplay.Camera;

namespace DynamicNavBakeShowcaseMod.Runtime;

/// <summary>
/// Deterministic auto-capture framing from authored anchors (squad / hotspot / path lookahead).
/// Pure math over <see cref="CameraViewportUtil"/> — no allocations, no algorithm branching.
/// </summary>
public readonly struct DynamicNavBakeShowcasePlayerFramingPose
{
    public DynamicNavBakeShowcasePlayerFramingPose(Vector2 targetCm, float distanceCm)
    {
        TargetCm = targetCm;
        DistanceCm = distanceCm;
    }

    public Vector2 TargetCm { get; }
    public float DistanceCm { get; }
}

public readonly struct DynamicNavBakeShowcasePlayerFramingVisibility
{
    public DynamicNavBakeShowcasePlayerFramingVisibility(
        int insideCount,
        int finiteProjectionCount,
        float minScreenX,
        float minScreenY,
        float maxScreenX,
        float maxScreenY)
    {
        InsideCount = insideCount;
        FiniteProjectionCount = finiteProjectionCount;
        MinScreenX = minScreenX;
        MinScreenY = minScreenY;
        MaxScreenX = maxScreenX;
        MaxScreenY = maxScreenY;
    }

    public int InsideCount { get; }
    public int FiniteProjectionCount { get; }
    public float MinScreenX { get; }
    public float MinScreenY { get; }
    public float MaxScreenX { get; }
    public float MaxScreenY { get; }
}

public static class DynamicNavBakeShowcasePlayerFraming
{
    public static DynamicNavBakeShowcasePlayerFramingPose Compute(
        ReadOnlySpan<Vector2> anchorsCm,
        DynamicNavBakeShowcasePlayerFramingConfig framing,
        float pitchDeg,
        float fovYDeg,
        float yawDeg)
    {
        if (framing == null)
        {
            throw new ArgumentNullException(nameof(framing));
        }

        if (anchorsCm.Length <= 0)
        {
            throw new InvalidOperationException(
                "Dynamic NavBake player framing requires at least one world-space anchor.");
        }

        if (!float.IsFinite(pitchDeg) ||
            !float.IsFinite(fovYDeg) ||
            !float.IsFinite(yawDeg) ||
            fovYDeg <= 0f ||
            fovYDeg >= 179f)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake player framing requires finite camera optics; got pitch={pitchDeg}, yaw={yawDeg}, fovYDeg={fovYDeg}.");
        }

        Vector2 screenRight = OrbitCameraDirectionUtil.RightFromYawDegrees(yawDeg);
        Vector2 screenUp = OrbitCameraDirectionUtil.ForwardFromYawDegrees(yawDeg);
        Vector2 first = anchorsCm[0];
        if (!float.IsFinite(first.X) || !float.IsFinite(first.Y))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake player framing anchor[0] is non-finite ({first.X},{first.Y}).");
        }

        float firstRight = Vector2.Dot(first, screenRight);
        float firstUp = Vector2.Dot(first, screenUp);
        float minRight = firstRight;
        float minUp = firstUp;
        float maxRight = firstRight;
        float maxUp = firstUp;
        for (int i = 1; i < anchorsCm.Length; i++)
        {
            Vector2 p = anchorsCm[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
            {
                throw new InvalidOperationException(
                    $"Dynamic NavBake player framing anchor[{i}] is non-finite ({p.X},{p.Y}).");
            }

            float right = Vector2.Dot(p, screenRight);
            float up = Vector2.Dot(p, screenUp);
            if (right < minRight) minRight = right;
            if (up < minUp) minUp = up;
            if (right > maxRight) maxRight = right;
            if (up > maxUp) maxUp = up;
        }

        float margin = framing.MarginCm;
        minRight -= margin;
        minUp -= margin;
        maxRight += margin;
        maxUp += margin;

        float widthCm = maxRight - minRight;
        float heightCm = maxUp - minUp;
        if (widthCm < 0f || heightCm < 0f || !float.IsFinite(widthCm) || !float.IsFinite(heightCm))
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake player framing produced a non-finite AABB ({widthCm}x{heightCm}).");
        }

        // Degenerate single-point clusters still need a usable authored base distance.
        float distanceCm = framing.BaseDistanceCm;
        if (widthCm > 0f || heightCm > 0f)
        {
            float vertical = heightCm > 0f
                ? CameraViewportUtil.DistanceForVerticalExtent(
                    (heightCm / framing.SafeHeightFraction) * framing.CoverageBuffer,
                    fovYDeg,
                    pitchDeg,
                    buffer: 1f)
                : 0f;
            float horizontal = widthCm > 0f
                ? CameraViewportUtil.DistanceForHorizontalExtent(
                    (widthCm / framing.SafeWidthFraction) * framing.CoverageBuffer,
                    fovYDeg,
                    pitchDeg,
                    framing.AspectRatio,
                    buffer: 1f)
                : 0f;
            distanceCm = MathF.Max(distanceCm, MathF.Max(vertical, horizontal));
        }

        if (!float.IsFinite(distanceCm) || distanceCm <= 0f)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake player framing computed a non-finite distance ({distanceCm}).");
        }

        if (distanceCm < framing.MinDistanceCm)
        {
            distanceCm = framing.MinDistanceCm;
        }

        if (distanceCm > framing.MaxDistanceCm)
        {
            throw new InvalidOperationException(
                $"Dynamic NavBake player framing requires distanceCm={distanceCm:F1} to fit anchors, " +
                $"but raylibAutoTimeline.playerFraming.maxDistanceCm={framing.MaxDistanceCm:F1}.");
        }

        (float fullWidthCm, float fullHeightCm) = CameraViewportUtil.ComputeViewportExtent(
            distanceCm,
            fovYDeg,
            pitchDeg,
            framing.AspectRatio,
            buffer: 1f);
        float desiredNdcX = (framing.SafeCenterNormalizedX * 2f) - 1f;
        float desiredNdcY = 1f - (framing.SafeCenterNormalizedY * 2f);
        float targetRight = ((minRight + maxRight) * 0.5f) - (desiredNdcX * fullWidthCm * 0.5f);
        float targetUp = ((minUp + maxUp) * 0.5f) - (desiredNdcY * fullHeightCm * 0.5f);
        Vector2 targetCm = (screenRight * targetRight) + (screenUp * targetUp);

        return new DynamicNavBakeShowcasePlayerFramingPose(targetCm, distanceCm);
    }

}
