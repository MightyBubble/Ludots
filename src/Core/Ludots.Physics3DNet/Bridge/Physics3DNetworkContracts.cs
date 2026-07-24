using System.Numerics;
using System.Text.Json.Serialization;
using Ludots.Core.Layers;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public struct Physics3DNetworkPlayer
{
    public int SeatSlot;
    public uint SeatGeneration;
    public int PlayerId;
}

public struct Physics3DNetworkReplicatedBody
{
    public NetworkEntityHandle Handle;
    public Physics3DBodyKind AuthoritativeKind;
}

public struct Physics3DNetworkClientMirror
{
    public Physics3DBodyKind AuthoritativeKind;
    public ulong SessionEpoch;
    public uint LastCommittedTick;
}

public struct Physics3DHeadlessClientMirror
{
    public NetworkEntityHandle Handle;
    public Physics3DBodyState State;
    public Physics3DBodyKind AuthoritativeKind;
    public ulong SessionEpoch;
    public uint LastCommittedTick;
}

public sealed class Physics3DReplicationSchemaConfig
{
    public int SchemaId { get; init; }
    [JsonRequired]
    public Physics3DReplicationQuantizationConfig Quantization { get; init; } = null!;

    public void Validate(NetworkRuntimeConfig network)
    {
        ArgumentNullException.ThrowIfNull(network);
        network.Validate();
        if (SchemaId <= 0 || SchemaId > network.ReplicationSchemaCapacity)
        {
            throw new InvalidOperationException(
                $"Physics3D replication schema {SchemaId} is outside configured registry capacity {network.ReplicationSchemaCapacity}.");
        }

        ArgumentNullException.ThrowIfNull(Quantization);
        Quantization.Validate();
    }
}

public sealed class Physics3DReplicationQuantizationConfig
{
    [JsonRequired]
    public float PositionResolutionCm { get; init; } = 0.5f;
    [JsonRequired]
    public float QuaternionResolution { get; init; } = 1f / short.MaxValue;
    [JsonRequired]
    public float LinearVelocityResolutionCmPerSecond { get; init; } = 0.5f;
    [JsonRequired]
    public float AngularVelocityResolutionRadiansPerSecond { get; init; } = 0.001f;

    public void Validate()
    {
        RequireFinitePositive(PositionResolutionCm, nameof(PositionResolutionCm));
        RequireFinitePositive(QuaternionResolution, nameof(QuaternionResolution));
        RequireFinitePositive(LinearVelocityResolutionCmPerSecond, nameof(LinearVelocityResolutionCmPerSecond));
        RequireFinitePositive(AngularVelocityResolutionRadiansPerSecond, nameof(AngularVelocityResolutionRadiansPerSecond));
        if (QuaternionResolution * short.MaxValue < 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QuaternionResolution),
                QuaternionResolution,
                "Quaternion quantization must represent the full normalized component range.");
        }
    }

    internal static void RequireFinitePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and positive.");
        }
    }
}

public sealed class Physics3DNetworkPlayerBodyConfig
{
    public float RadiusCm { get; init; } = 30f;
    public float CylinderLengthCm { get; init; } = 100f;
    public float Mass { get; init; } = 80f;
    public LayerMask CollisionLayer { get; init; } = LayerMask.All;
    public Physics3DMaterial Material { get; init; } = new(
        frictionCoefficient: 0.8f,
        maximumRecoveryVelocityCmPerSecond: 200f,
        springAngularFrequency: 30f,
        springTwiceDampingRatio: 1f);
    public Physics3DContinuousDetectionMode ContinuousDetection { get; init; } = Physics3DContinuousDetectionMode.Passive;

    public void Validate()
    {
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(RadiusCm, nameof(RadiusCm));
        if (!float.IsFinite(CylinderLengthCm) || CylinderLengthCm < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(CylinderLengthCm));
        }

        Physics3DReplicationQuantizationConfig.RequireFinitePositive(Mass, nameof(Mass));
        if (CollisionLayer.Category == 0 || CollisionLayer.Mask == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CollisionLayer));
        }

        RequireFiniteNonNegative(Material.FrictionCoefficient, $"{nameof(Material)}.{nameof(Material.FrictionCoefficient)}");
        RequireFiniteNonNegative(Material.MaximumRecoveryVelocityCmPerSecond, $"{nameof(Material)}.{nameof(Material.MaximumRecoveryVelocityCmPerSecond)}");
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(
            Material.SpringAngularFrequency,
            $"{nameof(Material)}.{nameof(Material.SpringAngularFrequency)}");
        RequireFiniteNonNegative(Material.SpringTwiceDampingRatio, $"{nameof(Material)}.{nameof(Material.SpringTwiceDampingRatio)}");
        if (ContinuousDetection is not (Physics3DContinuousDetectionMode.Discrete
            or Physics3DContinuousDetectionMode.Passive
            or Physics3DContinuousDetectionMode.Continuous))
        {
            throw new ArgumentOutOfRangeException(nameof(ContinuousDetection));
        }
    }

    private static void RequireFiniteNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and non-negative.");
        }
    }
}

public sealed class Physics3DNetworkPlayerSpawnConfig
{
    public Vector3 OriginCm { get; init; }
    public float ColumnSpacingCm { get; init; } = 250f;
    public float RowSpacingCm { get; init; } = 250f;
    public int Columns { get; init; } = 16;

    public void Validate()
    {
        RequireFinite(OriginCm, nameof(OriginCm));
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(ColumnSpacingCm, nameof(ColumnSpacingCm));
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(RowSpacingCm, nameof(RowSpacingCm));
        if (Columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Columns));
        }
    }

    public Vector3 Resolve(int seatSlot)
    {
        if (seatSlot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seatSlot));
        }

        int column = seatSlot % Columns;
        int row = seatSlot / Columns;
        return OriginCm + new Vector3(column * ColumnSpacingCm, 0f, row * RowSpacingCm);
    }

    private static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class Physics3DNetworkAoiConfig
{
    public int GlobalEntityCapacity { get; init; }
    public float RadiusCm { get; init; }

    public void Validate()
    {
        if (GlobalEntityCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GlobalEntityCapacity));
        }

        Physics3DReplicationQuantizationConfig.RequireFinitePositive(RadiusCm, nameof(RadiusCm));
    }
}

public sealed class Physics3DNetworkMovementConfig
{
    public ushort SchemaId { get; init; }
    public float MaximumSpeedCmPerSecond { get; init; } = 600f;
    public float MaximumAccelerationCmPerSecondSquared { get; init; } = 1_800f;
    public float VelocityResponsePerSecond { get; init; } = 20f;

    public void Validate()
    {
        if (SchemaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaId));
        }

        Physics3DReplicationQuantizationConfig.RequireFinitePositive(MaximumSpeedCmPerSecond, nameof(MaximumSpeedCmPerSecond));
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(
            MaximumAccelerationCmPerSecondSquared,
            nameof(MaximumAccelerationCmPerSecondSquared));
        Physics3DReplicationQuantizationConfig.RequireFinitePositive(VelocityResponsePerSecond, nameof(VelocityResponsePerSecond));
    }
}
