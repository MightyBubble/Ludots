using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

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
        public Vector3 RigPivotOffsetCm;
        public Vector3 RigCameraOffsetCm;
        public Vector3 ImpulsePositionOffsetCm;
        public float ImpulseYawOffsetDeg;
        public float ImpulsePitchOffsetDeg;
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
                RigPivotOffsetCm = state.RigPivotOffsetCm,
                RigCameraOffsetCm = state.RigCameraOffsetCm,
                ImpulsePositionOffsetCm = state.ImpulsePositionOffsetCm,
                ImpulseYawOffsetDeg = state.ImpulseYawOffsetDeg,
                ImpulsePitchOffsetDeg = state.ImpulsePitchOffsetDeg,
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
            state.RigPivotOffsetCm = RigPivotOffsetCm;
            state.RigCameraOffsetCm = RigCameraOffsetCm;
            state.ImpulsePositionOffsetCm = ImpulsePositionOffsetCm;
            state.ImpulseYawOffsetDeg = ImpulseYawOffsetDeg;
            state.ImpulsePitchOffsetDeg = ImpulsePitchOffsetDeg;
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
                RigPivotOffsetCm = Vector3.Lerp(from.RigPivotOffsetCm, to.RigPivotOffsetCm, t),
                RigCameraOffsetCm = Vector3.Lerp(from.RigCameraOffsetCm, to.RigCameraOffsetCm, t),
                ImpulsePositionOffsetCm = Vector3.Lerp(from.ImpulsePositionOffsetCm, to.ImpulsePositionOffsetCm, t),
                ImpulseYawOffsetDeg = LerpScalar(from.ImpulseYawOffsetDeg, to.ImpulseYawOffsetDeg, t),
                ImpulsePitchOffsetDeg = LerpScalar(from.ImpulsePitchOffsetDeg, to.ImpulsePitchOffsetDeg, t),
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
