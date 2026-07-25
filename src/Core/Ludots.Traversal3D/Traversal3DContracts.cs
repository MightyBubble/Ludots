using System;
using System.Numerics;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Traversal3D;

public readonly struct Traversal3DHandle : IEquatable<Traversal3DHandle>
{
    public Traversal3DHandle(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public int Slot { get; }
    public int Generation { get; }
    public bool IsValid => Slot >= 0 && Generation > 0;

    public bool Equals(Traversal3DHandle other) => Slot == other.Slot && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Traversal3DHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);
    public static bool operator ==(Traversal3DHandle left, Traversal3DHandle right) => left.Equals(right);
    public static bool operator !=(Traversal3DHandle left, Traversal3DHandle right) => !left.Equals(right);
    public override string ToString() => $"Traversal3DHandle({Slot}:{Generation})";
}

public enum Traversal3DState : byte
{
    NormalMovement = 1,
    Attached = 2,
    Climbing = 3,
    LedgeHang = 4,
    Mantling = 5,
    Detaching = 6
}

public enum Traversal3DSurfaceKind : byte
{
    Ladder = 1,
    ClimbableWall = 2
}

public readonly struct Traversal3DProfile
{
    public Traversal3DProfile(
        float attachProbeDistanceCm,
        float attachProbeRadiusCm,
        float surfaceClearanceCm,
        float attachSpeedCmPerSecond,
        float climbSpeedCmPerSecond,
        float lateralSpeedCmPerSecond,
        float maximumAccelerationCmPerSecondSquared,
        float ledgeProbeHeightCm,
        float ledgeProbeForwardCm,
        float ledgeProbeDownCm,
        float minimumLedgeHeightCm,
        float handClearanceRadiusCm,
        float mantleForwardCm,
        float mantleSpeedCmPerSecond,
        float mantleCompletionDistanceCm,
        float minimumTopNormalY,
        float detachUpSpeedCmPerSecond,
        float detachOutSpeedCmPerSecond)
    {
        AttachProbeDistanceCm = attachProbeDistanceCm;
        AttachProbeRadiusCm = attachProbeRadiusCm;
        SurfaceClearanceCm = surfaceClearanceCm;
        AttachSpeedCmPerSecond = attachSpeedCmPerSecond;
        ClimbSpeedCmPerSecond = climbSpeedCmPerSecond;
        LateralSpeedCmPerSecond = lateralSpeedCmPerSecond;
        MaximumAccelerationCmPerSecondSquared = maximumAccelerationCmPerSecondSquared;
        LedgeProbeHeightCm = ledgeProbeHeightCm;
        LedgeProbeForwardCm = ledgeProbeForwardCm;
        LedgeProbeDownCm = ledgeProbeDownCm;
        MinimumLedgeHeightCm = minimumLedgeHeightCm;
        HandClearanceRadiusCm = handClearanceRadiusCm;
        MantleForwardCm = mantleForwardCm;
        MantleSpeedCmPerSecond = mantleSpeedCmPerSecond;
        MantleCompletionDistanceCm = mantleCompletionDistanceCm;
        MinimumTopNormalY = minimumTopNormalY;
        DetachUpSpeedCmPerSecond = detachUpSpeedCmPerSecond;
        DetachOutSpeedCmPerSecond = detachOutSpeedCmPerSecond;
    }

    public float AttachProbeDistanceCm { get; }
    public float AttachProbeRadiusCm { get; }
    public float SurfaceClearanceCm { get; }
    public float AttachSpeedCmPerSecond { get; }
    public float ClimbSpeedCmPerSecond { get; }
    public float LateralSpeedCmPerSecond { get; }
    public float MaximumAccelerationCmPerSecondSquared { get; }
    public float LedgeProbeHeightCm { get; }
    public float LedgeProbeForwardCm { get; }
    public float LedgeProbeDownCm { get; }
    public float MinimumLedgeHeightCm { get; }
    public float HandClearanceRadiusCm { get; }
    public float MantleForwardCm { get; }
    public float MantleSpeedCmPerSecond { get; }
    public float MantleCompletionDistanceCm { get; }
    public float MinimumTopNormalY { get; }
    public float DetachUpSpeedCmPerSecond { get; }
    public float DetachOutSpeedCmPerSecond { get; }

    internal void Validate(string parameterName)
    {
        Traversal3DValidation.RequireFinitePositive(AttachProbeDistanceCm, $"{parameterName}.{nameof(AttachProbeDistanceCm)}");
        Traversal3DValidation.RequireFinitePositive(AttachProbeRadiusCm, $"{parameterName}.{nameof(AttachProbeRadiusCm)}");
        Traversal3DValidation.RequireFinitePositive(SurfaceClearanceCm, $"{parameterName}.{nameof(SurfaceClearanceCm)}");
        Traversal3DValidation.RequireFinitePositive(AttachSpeedCmPerSecond, $"{parameterName}.{nameof(AttachSpeedCmPerSecond)}");
        Traversal3DValidation.RequireFinitePositive(ClimbSpeedCmPerSecond, $"{parameterName}.{nameof(ClimbSpeedCmPerSecond)}");
        Traversal3DValidation.RequireFinitePositive(LateralSpeedCmPerSecond, $"{parameterName}.{nameof(LateralSpeedCmPerSecond)}");
        Traversal3DValidation.RequireFinitePositive(MaximumAccelerationCmPerSecondSquared, $"{parameterName}.{nameof(MaximumAccelerationCmPerSecondSquared)}");
        Traversal3DValidation.RequireFinitePositive(LedgeProbeHeightCm, $"{parameterName}.{nameof(LedgeProbeHeightCm)}");
        Traversal3DValidation.RequireFinitePositive(LedgeProbeForwardCm, $"{parameterName}.{nameof(LedgeProbeForwardCm)}");
        Traversal3DValidation.RequireFinitePositive(LedgeProbeDownCm, $"{parameterName}.{nameof(LedgeProbeDownCm)}");
        Traversal3DValidation.RequireFinitePositive(MinimumLedgeHeightCm, $"{parameterName}.{nameof(MinimumLedgeHeightCm)}");
        Traversal3DValidation.RequireFinitePositive(HandClearanceRadiusCm, $"{parameterName}.{nameof(HandClearanceRadiusCm)}");
        Traversal3DValidation.RequireFinitePositive(MantleForwardCm, $"{parameterName}.{nameof(MantleForwardCm)}");
        Traversal3DValidation.RequireFinitePositive(MantleSpeedCmPerSecond, $"{parameterName}.{nameof(MantleSpeedCmPerSecond)}");
        Traversal3DValidation.RequireFinitePositive(MantleCompletionDistanceCm, $"{parameterName}.{nameof(MantleCompletionDistanceCm)}");
        if (!float.IsFinite(MinimumTopNormalY) || MinimumTopNormalY <= 0f || MinimumTopNormalY > 1f)
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(MinimumTopNormalY)}",
                MinimumTopNormalY,
                "Minimum top normal Y must be in the range (0, 1].");
        }

        Traversal3DValidation.RequireFinitePositive(DetachUpSpeedCmPerSecond, $"{parameterName}.{nameof(DetachUpSpeedCmPerSecond)}");
        Traversal3DValidation.RequireFinitePositive(DetachOutSpeedCmPerSecond, $"{parameterName}.{nameof(DetachOutSpeedCmPerSecond)}");
    }
}

public readonly struct Traversal3DIntent
{
    public Traversal3DIntent(Vector2 move, Vector3 facingDirection, bool engageRequested, bool jumpRequested)
    {
        Move = move;
        FacingDirection = facingDirection;
        EngageRequested = engageRequested;
        JumpRequested = jumpRequested;
    }

    public Vector2 Move { get; }
    public Vector3 FacingDirection { get; }
    public bool EngageRequested { get; }
    public bool JumpRequested { get; }

    internal void Validate(string parameterName)
    {
        if (!float.IsFinite(Move.X) || !float.IsFinite(Move.Y))
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(Move)}",
                Move,
                "Movement must be finite.");
        }

        if (Move.LengthSquared() > 1.0001f)
        {
            throw new ArgumentOutOfRangeException($"{parameterName}.{nameof(Move)}", Move, "Movement magnitude cannot exceed one.");
        }

        if (!float.IsFinite(FacingDirection.X) ||
            !float.IsFinite(FacingDirection.Y) ||
            !float.IsFinite(FacingDirection.Z))
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(FacingDirection)}",
                FacingDirection,
                "Facing direction must be finite.");
        }

        Vector3 planar = new(FacingDirection.X, 0f, FacingDirection.Z);
        float lengthSquared = planar.LengthSquared();
        if (!(lengthSquared > 0.999f && lengthSquared < 1.001f))
        {
            throw new ArgumentOutOfRangeException(
                $"{parameterName}.{nameof(FacingDirection)}",
                FacingDirection,
                "Facing direction must be a normalized horizontal vector.");
        }
    }
}

public readonly struct Traversal3DStatus
{
    public Traversal3DStatus(
        Traversal3DState state,
        Physics3DBodyId surfaceBody,
        Traversal3DSurfaceKind surfaceKind,
        Vector3 surfaceNormal,
        Vector3 targetPositionCm,
        bool clearanceValid,
        int stateTicks)
    {
        State = state;
        SurfaceBody = surfaceBody;
        SurfaceKind = surfaceKind;
        SurfaceNormal = surfaceNormal;
        TargetPositionCm = targetPositionCm;
        ClearanceValid = clearanceValid;
        StateTicks = stateTicks;
    }

    public Traversal3DState State { get; }
    public Physics3DBodyId SurfaceBody { get; }
    public Traversal3DSurfaceKind SurfaceKind { get; }
    public Vector3 SurfaceNormal { get; }
    public Vector3 TargetPositionCm { get; }
    public bool ClearanceValid { get; }
    public int StateTicks { get; }
}

public sealed class Traversal3DCapacityExceededException : InvalidOperationException
{
    public Traversal3DCapacityExceededException(string resource, int capacity)
        : base($"Traversal3D capacity exceeded for '{resource}' (configured capacity: {capacity}).")
    {
        Resource = resource;
        Capacity = capacity;
    }

    public string Resource { get; }
    public int Capacity { get; }
}

internal static class Traversal3DValidation
{
    public static void RequireFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector must be finite.");
        }
    }

    public static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Vector must be finite.");
        }
    }

    public static void RequireFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and greater than zero.");
        }
    }
}
