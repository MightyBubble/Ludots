using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Deterministic validation for public Physics3DNet state values.
/// Quaternions that are materially non-unit are rejected; they are never silently normalized.
/// </summary>
public static class Physics3DNetValidation
{
    /// <summary>
    /// Maximum allowed |length² − 1| for an accepted unit quaternion.
    /// Values outside this band are rejected rather than normalized.
    /// </summary>
    public const float MaxUnitQuaternionLengthSquaredDeviation = 1e-3f;

    public static void RequirePositiveTick(long tick, string paramName)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, tick, "Tick must be positive.");
        }
    }

    public static void RequireNonNegativeId(int id, string paramName)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, id, "Id must be non-negative.");
        }
    }

    public static void RequirePositiveGeneration(int generation, string paramName)
    {
        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, generation, "Generation must be positive.");
        }
    }

    public static void RequireNonNegativeBaselineId(long baselineId, string paramName)
    {
        if (baselineId < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, baselineId, "Baseline id must be non-negative.");
        }
    }

    public static void RequireFinite(Vector3 value, string paramName)
    {
        if (!IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Vector components must be finite.");
        }
    }

    public static void RequireUnitQuaternion(Quaternion orientation, string paramName)
    {
        if (!IsFinite(orientation))
        {
            throw new ArgumentOutOfRangeException(paramName, orientation, "Quaternion components must be finite.");
        }

        float lengthSquared =
            (orientation.X * orientation.X)
            + (orientation.Y * orientation.Y)
            + (orientation.Z * orientation.Z)
            + (orientation.W * orientation.W);

        if (lengthSquared <= 0f
            || MathF.Abs(lengthSquared - 1f) > MaxUnitQuaternionLengthSquaredDeviation)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                orientation,
                $"Quaternion must be a unit quaternion (|length²-1| <= {MaxUnitQuaternionLengthSquaredDeviation}); silent normalization is forbidden.");
        }
    }

    public static void RequireValidReplicationOp(Physics3DNetReplicationOp op, string paramName)
    {
        if (op is not (Physics3DNetReplicationOp.Spawn
            or Physics3DNetReplicationOp.Update
            or Physics3DNetReplicationOp.Despawn))
        {
            throw new ArgumentOutOfRangeException(paramName, op, "Invalid replication op.");
        }
    }

    public static void RequireValidBodyKind(Physics3DBodyKind bodyKind, string paramName)
    {
        if (bodyKind is not (Physics3DBodyKind.Dynamic
            or Physics3DBodyKind.Kinematic
            or Physics3DBodyKind.Static))
        {
            throw new ArgumentOutOfRangeException(paramName, bodyKind, "Invalid body kind.");
        }
    }

    public static void RequireValidReplicationMode(Physics3DNetReplicationMode replicationMode, string paramName)
    {
        if (replicationMode is not (Physics3DNetReplicationMode.RigidBody
            or Physics3DNetReplicationMode.Character
            or Physics3DNetReplicationMode.Vehicle
            or Physics3DNetReplicationMode.Ragdoll))
        {
            throw new ArgumentOutOfRangeException(paramName, replicationMode, "Invalid replication mode.");
        }
    }

    public static void RequireValidLocalDrivenKind(Physics3DNetLocalDrivenKind kind, string paramName)
    {
        if (kind is not (Physics3DNetLocalDrivenKind.Character or Physics3DNetLocalDrivenKind.Vehicle))
        {
            throw new ArgumentOutOfRangeException(paramName, kind, "Only Character or Vehicle may be locally predicted.");
        }
    }

    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z)
        && float.IsFinite(value.W);
}
