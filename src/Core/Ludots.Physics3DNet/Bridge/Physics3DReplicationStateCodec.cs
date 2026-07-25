using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet.Bridge;

public static class Physics3DReplicationStateCodec
{
    private const int PositionBits = 24;
    private const int ScalarBits = 16;
    private const int BodyKindBits = 2;
    private const int TotalBits = (PositionBits * 3) + (ScalarBits * 10) + 1 + BodyKindBits;
    private const int UsedBitsInFinalWord = TotalBits & 63;

    public static bool TryEncode(
        in Physics3DBodyState state,
        Physics3DBodyKind bodyKind,
        Physics3DReplicationQuantizationConfig config,
        out ReplicationStateVector values)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return TryEncodeValidated(in state, bodyKind, config, out values);
    }

    internal static bool TryEncodeValidated(
        in Physics3DBodyState state,
        Physics3DBodyKind bodyKind,
        Physics3DReplicationQuantizationConfig validatedConfig,
        out ReplicationStateVector values)
    {
        values = default;
        if (bodyKind is not (Physics3DBodyKind.Dynamic or Physics3DBodyKind.Kinematic or Physics3DBodyKind.Static) ||
            !IsFinite(state.PositionCm) ||
            !IsFinite(state.LinearVelocityCmPerSecond) ||
            !IsFinite(state.AngularVelocityRadiansPerSecond) ||
            !IsFinite(state.Orientation))
        {
            return false;
        }

        float orientationLengthSquared = state.Orientation.LengthSquared();
        if (!float.IsFinite(orientationLengthSquared) || orientationLengthSquared < 0.999f || orientationLengthSquared > 1.001f)
        {
            return false;
        }

        Quaternion orientation = Quaternion.Normalize(state.Orientation);
        Span<ulong> words = stackalloc ulong[4];
        int offset = 0;
        if (!TryWriteSigned(words, ref offset, state.PositionCm.X, validatedConfig.PositionResolutionCm, PositionBits) ||
            !TryWriteSigned(words, ref offset, state.PositionCm.Y, validatedConfig.PositionResolutionCm, PositionBits) ||
            !TryWriteSigned(words, ref offset, state.PositionCm.Z, validatedConfig.PositionResolutionCm, PositionBits) ||
            !TryWriteSigned(words, ref offset, orientation.X, validatedConfig.QuaternionResolution, ScalarBits) ||
            !TryWriteSigned(words, ref offset, orientation.Y, validatedConfig.QuaternionResolution, ScalarBits) ||
            !TryWriteSigned(words, ref offset, orientation.Z, validatedConfig.QuaternionResolution, ScalarBits) ||
            !TryWriteSigned(words, ref offset, orientation.W, validatedConfig.QuaternionResolution, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.LinearVelocityCmPerSecond.X, validatedConfig.LinearVelocityResolutionCmPerSecond, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.LinearVelocityCmPerSecond.Y, validatedConfig.LinearVelocityResolutionCmPerSecond, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.LinearVelocityCmPerSecond.Z, validatedConfig.LinearVelocityResolutionCmPerSecond, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.AngularVelocityRadiansPerSecond.X, validatedConfig.AngularVelocityResolutionRadiansPerSecond, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.AngularVelocityRadiansPerSecond.Y, validatedConfig.AngularVelocityResolutionRadiansPerSecond, ScalarBits) ||
            !TryWriteSigned(words, ref offset, state.AngularVelocityRadiansPerSecond.Z, validatedConfig.AngularVelocityResolutionRadiansPerSecond, ScalarBits))
        {
            return false;
        }

        WriteBits(words, ref offset, state.Awake ? 1UL : 0UL, 1);
        WriteBits(words, ref offset, (ulong)bodyKind, BodyKindBits);
        if (offset != TotalBits)
        {
            throw new InvalidOperationException("Physics3D replication payload layout is inconsistent.");
        }

        values = new ReplicationStateVector(
            unchecked((long)words[0]),
            unchecked((long)words[1]),
            unchecked((long)words[2]),
            unchecked((long)words[3]));
        return true;
    }

    public static bool TryDecode(
        in ReplicationStateVector values,
        Physics3DReplicationQuantizationConfig config,
        out Physics3DBodyState state,
        out Physics3DBodyKind bodyKind)
    {
        state = default;
        bodyKind = default;
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        Span<ulong> words = stackalloc ulong[4]
        {
            unchecked((ulong)values.Value0),
            unchecked((ulong)values.Value1),
            unchecked((ulong)values.Value2),
            unchecked((ulong)values.Value3),
        };
        if ((words[^1] >> UsedBitsInFinalWord) != 0)
        {
            return false;
        }

        int offset = 0;
        Vector3 position = new(
            ReadSigned(words, ref offset, config.PositionResolutionCm, PositionBits),
            ReadSigned(words, ref offset, config.PositionResolutionCm, PositionBits),
            ReadSigned(words, ref offset, config.PositionResolutionCm, PositionBits));
        Quaternion orientation = new(
            ReadSigned(words, ref offset, config.QuaternionResolution, ScalarBits),
            ReadSigned(words, ref offset, config.QuaternionResolution, ScalarBits),
            ReadSigned(words, ref offset, config.QuaternionResolution, ScalarBits),
            ReadSigned(words, ref offset, config.QuaternionResolution, ScalarBits));
        Vector3 linearVelocity = new(
            ReadSigned(words, ref offset, config.LinearVelocityResolutionCmPerSecond, ScalarBits),
            ReadSigned(words, ref offset, config.LinearVelocityResolutionCmPerSecond, ScalarBits),
            ReadSigned(words, ref offset, config.LinearVelocityResolutionCmPerSecond, ScalarBits));
        Vector3 angularVelocity = new(
            ReadSigned(words, ref offset, config.AngularVelocityResolutionRadiansPerSecond, ScalarBits),
            ReadSigned(words, ref offset, config.AngularVelocityResolutionRadiansPerSecond, ScalarBits),
            ReadSigned(words, ref offset, config.AngularVelocityResolutionRadiansPerSecond, ScalarBits));
        bool awake = ReadBits(words, ref offset, 1) != 0;
        bodyKind = (Physics3DBodyKind)ReadBits(words, ref offset, BodyKindBits);
        if (offset != TotalBits ||
            bodyKind is not (Physics3DBodyKind.Dynamic or Physics3DBodyKind.Kinematic or Physics3DBodyKind.Static) ||
            !IsFinite(position) ||
            !IsFinite(linearVelocity) ||
            !IsFinite(angularVelocity) ||
            !IsFinite(orientation))
        {
            state = default;
            bodyKind = default;
            return false;
        }

        float orientationLengthSquared = orientation.LengthSquared();
        if (!float.IsFinite(orientationLengthSquared) || orientationLengthSquared < 0.25f)
        {
            state = default;
            bodyKind = default;
            return false;
        }

        state = new Physics3DBodyState
        {
            PositionCm = position,
            Orientation = Quaternion.Normalize(orientation),
            LinearVelocityCmPerSecond = linearVelocity,
            AngularVelocityRadiansPerSecond = angularVelocity,
            Awake = awake,
        };
        return true;
    }

    public static uint ComputeRevision(in ReplicationStateVector values, uint disclosureRevision)
    {
        uint hash = 2166136261u;
        Mix(ref hash, unchecked((ulong)values.Value0));
        Mix(ref hash, unchecked((ulong)values.Value1));
        Mix(ref hash, unchecked((ulong)values.Value2));
        Mix(ref hash, unchecked((ulong)values.Value3));
        return (hash ^ disclosureRevision) * 16777619u;
    }

    private static bool TryWriteSigned(Span<ulong> words, ref int offset, float value, float resolution, int bits)
    {
        double scaled = Math.Round(value / resolution, MidpointRounding.AwayFromZero);
        long minimum = -(1L << (bits - 1));
        long maximum = (1L << (bits - 1)) - 1;
        if (!double.IsFinite(scaled) || scaled < minimum || scaled > maximum)
        {
            return false;
        }

        ulong mask = (1UL << bits) - 1UL;
        WriteBits(words, ref offset, unchecked((ulong)(long)scaled) & mask, bits);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ReadSigned(ReadOnlySpan<ulong> words, ref int offset, float resolution, int bits)
    {
        ulong raw = ReadBits(words, ref offset, bits);
        long signed = ((long)(raw << (64 - bits))) >> (64 - bits);
        return signed * resolution;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBits(Span<ulong> words, ref int offset, ulong value, int bits)
    {
        int word = offset >> 6;
        int shift = offset & 63;
        words[word] |= value << shift;
        int overflow = shift + bits - 64;
        if (overflow > 0)
        {
            words[word + 1] |= value >> (bits - overflow);
        }

        offset += bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadBits(ReadOnlySpan<ulong> words, ref int offset, int bits)
    {
        int word = offset >> 6;
        int shift = offset & 63;
        ulong value = words[word] >> shift;
        int overflow = shift + bits - 64;
        if (overflow > 0)
        {
            value |= words[word + 1] << (bits - overflow);
        }

        offset += bits;
        return value & ((1UL << bits) - 1UL);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref uint hash, ulong value)
    {
        hash = (hash ^ (uint)value) * 16777619u;
        hash = (hash ^ (uint)(value >> 32)) * 16777619u;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
