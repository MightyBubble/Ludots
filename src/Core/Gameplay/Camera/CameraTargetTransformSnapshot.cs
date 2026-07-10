using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Camera
{
    public readonly struct CameraTargetTransformSnapshot
    {
        public CameraTargetTransformSnapshot(
            Vector2 positionCm,
            bool hasFacingYawRad = false,
            float facingYawRad = 0f,
            bool hasHeightCm = false,
            float heightCm = 0f)
        {
            if (!float.IsFinite(positionCm.X) || !float.IsFinite(positionCm.Y))
            {
                throw new ArgumentException("Camera target transform position must be finite.", nameof(positionCm));
            }

            if (hasFacingYawRad && !float.IsFinite(facingYawRad))
            {
                throw new ArgumentException("Camera target transform facing yaw must be finite.", nameof(facingYawRad));
            }

            if (hasHeightCm && !float.IsFinite(heightCm))
            {
                throw new ArgumentException("Camera target transform height must be finite.", nameof(heightCm));
            }

            PositionCm = positionCm;
            HasFacingYawRad = hasFacingYawRad;
            FacingYawRad = hasFacingYawRad ? WorldPlane2D.NormalizePositiveRad(facingYawRad) : 0f;
            HasHeightCm = hasHeightCm;
            HeightCm = hasHeightCm ? heightCm : 0f;
        }

        public Vector2 PositionCm { get; }
        public bool HasFacingYawRad { get; }
        public float FacingYawRad { get; }
        public bool HasHeightCm { get; }
        public float HeightCm { get; }
    }
}
