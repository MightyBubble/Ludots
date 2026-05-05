using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Gameplay.Camera
{
    public struct CameraStateSnapshot
    {
        public Vector2 TargetCm;
        public float TargetHeightCm;
        public float Yaw;
        public float Pitch;
        public float DistanceCm;
        public float FovYDeg;
        public CameraRigKind RigKind;
        public int ZoomLevel;
        public bool IsFollowing;

        public static CameraStateSnapshot FromState(CameraState state)
        {
            return new CameraStateSnapshot
            {
                TargetCm = state.TargetCm,
                TargetHeightCm = state.TargetHeightCm,
                Yaw = state.Yaw,
                Pitch = state.Pitch,
                DistanceCm = state.DistanceCm,
                FovYDeg = state.FovYDeg,
                RigKind = state.RigKind,
                ZoomLevel = state.ZoomLevel,
                IsFollowing = state.IsFollowing
            };
        }

        public void ApplyTo(CameraState state)
        {
            state.TargetCm = TargetCm;
            state.TargetHeightCm = TargetHeightCm;
            state.Yaw = Yaw;
            state.Pitch = Pitch;
            state.DistanceCm = DistanceCm;
            state.FovYDeg = FovYDeg;
            state.RigKind = RigKind;
            state.ZoomLevel = ZoomLevel;
            state.IsFollowing = IsFollowing;
        }

        public static CameraStateSnapshot Lerp(in CameraStateSnapshot from, in CameraStateSnapshot to, float t)
        {
            return new CameraStateSnapshot
            {
                TargetCm = Vector2.Lerp(from.TargetCm, to.TargetCm, t),
                TargetHeightCm = LerpScalar(from.TargetHeightCm, to.TargetHeightCm, t),
                Yaw = WorldPlane2D.LerpAngleDegrees(from.Yaw, to.Yaw, t),
                Pitch = LerpScalar(from.Pitch, to.Pitch, t),
                DistanceCm = LerpScalar(from.DistanceCm, to.DistanceCm, t),
                FovYDeg = LerpScalar(from.FovYDeg, to.FovYDeg, t),
                RigKind = to.RigKind,
                ZoomLevel = to.ZoomLevel,
                IsFollowing = to.IsFollowing
            };
        }

        private static float LerpScalar(float from, float to, float t)
        {
            return from + ((to - from) * t);
        }

    }
}
