using System;
using System.Buffers.Binary;
using System.Text;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Runtime/build compatibility fingerprint covering build/config/kernel/SIMD/worker/scenario.
/// Exact replay takeover rejects mismatch.
/// </summary>
public readonly struct Physics3DNetCompatibilityFingerprint : IEquatable<Physics3DNetCompatibilityFingerprint>
{
    public Physics3DNetCompatibilityFingerprint(
        string buildId,
        ulong configHash,
        string kernelId,
        string simdProfile,
        int workerCount,
        string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(buildId))
        {
            throw new ArgumentException("Build id is required.", nameof(buildId));
        }

        if (string.IsNullOrWhiteSpace(kernelId))
        {
            throw new ArgumentException("Kernel id is required.", nameof(kernelId));
        }

        if (string.IsNullOrWhiteSpace(simdProfile))
        {
            throw new ArgumentException("SIMD profile is required.", nameof(simdProfile));
        }

        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new ArgumentException("Scenario id is required.", nameof(scenarioId));
        }

        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "Worker count must be positive.");
        }

        BuildId = buildId;
        ConfigHash = configHash;
        KernelId = kernelId;
        SimdProfile = simdProfile;
        WorkerCount = workerCount;
        ScenarioId = scenarioId;
    }

    public string BuildId { get; }
    public ulong ConfigHash { get; }
    public string KernelId { get; }
    public string SimdProfile { get; }
    public int WorkerCount { get; }
    public string ScenarioId { get; }

    public bool Equals(Physics3DNetCompatibilityFingerprint other) =>
        BuildId == other.BuildId
        && ConfigHash == other.ConfigHash
        && KernelId == other.KernelId
        && SimdProfile == other.SimdProfile
        && WorkerCount == other.WorkerCount
        && ScenarioId == other.ScenarioId;

    public override bool Equals(object? obj) =>
        obj is Physics3DNetCompatibilityFingerprint other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(BuildId, ConfigHash, KernelId, SimdProfile, WorkerCount, ScenarioId);

    public static bool operator ==(Physics3DNetCompatibilityFingerprint left, Physics3DNetCompatibilityFingerprint right) =>
        left.Equals(right);

    public static bool operator !=(Physics3DNetCompatibilityFingerprint left, Physics3DNetCompatibilityFingerprint right) =>
        !left.Equals(right);

    public override string ToString() =>
        $"build={BuildId};config={ConfigHash:X16};kernel={KernelId};simd={SimdProfile};workers={WorkerCount};scenario={ScenarioId}";

    public static ulong HashConfig(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        Span<byte> buffer = stackalloc byte[44];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(0, 4), config.AuthoritativeHz);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), config.SnapshotHz);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8, 4), config.PlayerCapacity);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12, 4), config.InputHistoryTicksPerPlayer);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16, 4), config.MaxFutureInputTicks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20, 4), config.SnapshotEntityCapacity);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24, 4), config.AoiEntityCapacityPerClient);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(28, 4), config.LocalPredictionHistoryTicks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(32, 4), config.RemoteInterpolationHistoryTicks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(36, 4), config.ReplayEventCapacity);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(40, 4), config.ClientCapacity);
        return Fnv1a64(buffer);
    }

    public static ulong HashUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, buffer);
        return Fnv1a64(buffer);
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= 1099511628211UL;
        }

        return hash;
    }
}

public sealed class Physics3DNetCompatibilityMismatchException : InvalidOperationException
{
    public Physics3DNetCompatibilityMismatchException(
        Physics3DNetCompatibilityFingerprint expected,
        Physics3DNetCompatibilityFingerprint actual)
        : base(
            $"Exact replay takeover rejected: compatibility fingerprint mismatch. Expected [{expected}] Actual [{actual}].")
    {
        Expected = expected;
        Actual = actual;
    }

    public Physics3DNetCompatibilityFingerprint Expected { get; }
    public Physics3DNetCompatibilityFingerprint Actual { get; }
}

/// <summary>
/// Gate for exact replay takeover. Mismatch throws; no silent continue.
/// </summary>
public sealed class Physics3DNetCompatibilityGate
{
    public Physics3DNetCompatibilityGate(Physics3DNetCompatibilityFingerprint required)
    {
        Required = required;
    }

    public Physics3DNetCompatibilityFingerprint Required { get; }

    public void RequireMatch(in Physics3DNetCompatibilityFingerprint candidate)
    {
        if (!Required.Equals(candidate))
        {
            throw new Physics3DNetCompatibilityMismatchException(Required, candidate);
        }
    }
}
