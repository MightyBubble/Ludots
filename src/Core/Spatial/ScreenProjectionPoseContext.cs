using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    public readonly struct ScreenProjectionPoseContext
    {
        public ScreenProjectionPoseContext(float interpolationAlpha, IContinuousHeightmap? groundHeightmap)
        {
            if (!float.IsFinite(interpolationAlpha) || interpolationAlpha < 0f || interpolationAlpha > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(interpolationAlpha), "SPATIAL.ERR.InvalidInterpolationAlpha");
            }

            InterpolationAlpha = interpolationAlpha;
            GroundHeightmap = groundHeightmap;
        }

        public float InterpolationAlpha { get; }

        public IContinuousHeightmap? GroundHeightmap { get; }

        public static ScreenProjectionPoseContext CurrentSimulation => new(1f, null);
    }
}
