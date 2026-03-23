using System;
using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Generic distance-to-pitch sample for host-managed orbit cameras.
    /// </summary>
    public readonly record struct PlatformManagedCameraDistancePitchSample(float DistanceCm, float PitchDeg);

    /// <summary>
    /// Host snapshot for the active platform-managed camera rig.
    /// </summary>
    public sealed class PlatformManagedCameraControlSnapshot
    {
        public bool IsAvailable { get; set; }
        public bool IsLocalGameState { get; set; }
        public float ViewportWidth { get; set; }
        public float ViewportHeight { get; set; }
        public bool HasPointerPosition { get; set; }
        public float PointerX { get; set; }
        public float PointerY { get; set; }
        public float PointerWheelDelta { get; set; }
        public Vector2 TargetCm { get; set; }
        public float YawDeg { get; set; }
        public float PitchDeg { get; set; }
        public float DistanceCm { get; set; }
        public bool IsPitchLocked { get; set; }
        public bool UiCaptured { get; set; }

        public PlatformManagedCameraControlSnapshot Clone()
        {
            return new PlatformManagedCameraControlSnapshot
            {
                IsAvailable = IsAvailable,
                IsLocalGameState = IsLocalGameState,
                ViewportWidth = ViewportWidth,
                ViewportHeight = ViewportHeight,
                HasPointerPosition = HasPointerPosition,
                PointerX = PointerX,
                PointerY = PointerY,
                PointerWheelDelta = PointerWheelDelta,
                TargetCm = TargetCm,
                YawDeg = YawDeg,
                PitchDeg = PitchDeg,
                DistanceCm = DistanceCm,
                IsPitchLocked = IsPitchLocked,
                UiCaptured = UiCaptured,
            };
        }
    }

    /// <summary>
    /// Host tuning block for platform-managed cameras.
    /// </summary>
    public sealed class PlatformManagedCameraTuningSnapshot
    {
        public bool IsAvailable { get; set; }
        public float MinDistanceCm { get; set; }
        public float MaxDistanceCm { get; set; }
        public float DefaultDistanceCm { get; set; }
        public float PanSpeedDistanceRatio { get; set; }
        public float MinPanSpeedCmPerSecond { get; set; }
        public float ZoomInDistanceMultiplier { get; set; }
        public float RotationDegreesPerPixel { get; set; }
        public PlatformManagedCameraDistancePitchSample[] LockedPitchSamples { get; set; } = Array.Empty<PlatformManagedCameraDistancePitchSample>();

        public PlatformManagedCameraTuningSnapshot Clone()
        {
            return new PlatformManagedCameraTuningSnapshot
            {
                IsAvailable = IsAvailable,
                MinDistanceCm = MinDistanceCm,
                MaxDistanceCm = MaxDistanceCm,
                DefaultDistanceCm = DefaultDistanceCm,
                PanSpeedDistanceRatio = PanSpeedDistanceRatio,
                MinPanSpeedCmPerSecond = MinPanSpeedCmPerSecond,
                ZoomInDistanceMultiplier = ZoomInDistanceMultiplier,
                RotationDegreesPerPixel = RotationDegreesPerPixel,
                LockedPitchSamples = LockedPitchSamples.Length == 0
                    ? Array.Empty<PlatformManagedCameraDistancePitchSample>()
                    : (PlatformManagedCameraDistancePitchSample[])LockedPitchSamples.Clone(),
            };
        }
    }

    public readonly record struct PlatformManagedCameraHostRequest(bool SuppressesHostInput);

    public readonly record struct PlatformManagedCameraHostResult(
        bool Success,
        bool SuppressesHostInput,
        bool WorldAvailable,
        string ErrorMessage)
    {
        public static PlatformManagedCameraHostResult Ok(bool suppressesHostInput, bool worldAvailable)
            => new(true, suppressesHostInput, worldAvailable, string.Empty);

        public static PlatformManagedCameraHostResult Fail(bool suppressesHostInput, bool worldAvailable, string errorMessage)
            => new(false, suppressesHostInput, worldAvailable, errorMessage ?? string.Empty);
    }

    /// <summary>
    /// Host service that exposes platform-owned camera state to platform-managed drivers.
    /// </summary>
    public interface IPlatformManagedCameraHostService
    {
        PlatformManagedCameraControlSnapshot ReadControlSnapshot();

        PlatformManagedCameraTuningSnapshot ReadTuningSnapshot();

        PlatformManagedCameraHostResult SubmitRequest(PlatformManagedCameraHostRequest request);
    }
}
