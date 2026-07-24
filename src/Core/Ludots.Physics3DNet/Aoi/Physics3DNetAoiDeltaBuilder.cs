using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetAoiDeltaResultKind : byte
{
    Built = 1,
    BaselineMissing = 2
}

public readonly struct Physics3DNetAoiInterest
{
    public Physics3DNetAoiInterest(
        int networkEntityId,
        int generation,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        Physics3DBodyKind bodyKind,
        Physics3DNetReplicationMode replicationMode)
    {
        Physics3DNetValidation.RequireNonNegativeId(networkEntityId, nameof(networkEntityId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));
        Physics3DNetValidation.RequireFinite(positionCm, nameof(positionCm));
        Physics3DNetValidation.RequireUnitQuaternion(orientation, nameof(orientation));
        Physics3DNetValidation.RequireFinite(linearVelocityCmPerSecond, nameof(linearVelocityCmPerSecond));
        Physics3DNetValidation.RequireFinite(angularVelocityRadiansPerSecond, nameof(angularVelocityRadiansPerSecond));
        Physics3DNetValidation.RequireValidBodyKind(bodyKind, nameof(bodyKind));
        Physics3DNetValidation.RequireValidReplicationMode(replicationMode, nameof(replicationMode));

        NetworkEntityId = networkEntityId;
        Generation = generation;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
        BodyKind = bodyKind;
        ReplicationMode = replicationMode;
    }

    public int NetworkEntityId { get; }
    public int Generation { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
    public Physics3DBodyKind BodyKind { get; }
    public Physics3DNetReplicationMode ReplicationMode { get; }
}

public readonly struct Physics3DNetAoiDeltaBuildResult
{
    public Physics3DNetAoiDeltaBuildResult(Physics3DNetAoiDeltaResultKind kind, int writtenCount, long baselineId)
    {
        Kind = kind;
        WrittenCount = writtenCount;
        BaselineId = baselineId;
    }

    public Physics3DNetAoiDeltaResultKind Kind { get; }
    public int WrittenCount { get; }
    public long BaselineId { get; }
    public bool RequiresFullSnapshot => Kind == Physics3DNetAoiDeltaResultKind.BaselineMissing;
}

/// <summary>
/// Per-client AOI delta builder. AOI filters replication only and never mutates authoritative simulation.
/// Distinct clients may BuildDelta concurrently via disjoint fixed-array lanes; same-client concurrency is unsupported.
/// </summary>
public sealed class Physics3DNetAoiDeltaBuilder
{
    private readonly int _clientCapacity;
    private readonly int _entityCapacityPerClient;
    // Each client lane is a packed, network-entity-id-sorted prefix of the fixed arrays.
    private readonly int[] _trackedEntityId;
    private readonly int[] _trackedGeneration;
    private readonly int[] _trackedCount;
    private readonly long[] _clientBaselineId;
    private readonly bool[] _clientHasBaseline;
    private readonly int[] _lastBuildEntityIdComparisonCount;

    public Physics3DNetAoiDeltaBuilder(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _clientCapacity = config.ClientCapacity;
        _entityCapacityPerClient = config.AoiEntityCapacityPerClient;
        int total = checked(_clientCapacity * _entityCapacityPerClient);
        _trackedEntityId = new int[total];
        _trackedGeneration = new int[total];
        _trackedCount = new int[_clientCapacity];
        _clientBaselineId = new long[_clientCapacity];
        _clientHasBaseline = new bool[_clientCapacity];
        _lastBuildEntityIdComparisonCount = new int[_clientCapacity];
    }

    public int ClientCapacity => _clientCapacity;
    public int EntityCapacityPerClient => _entityCapacityPerClient;

    public void AcknowledgeBaseline(int clientSlot, long baselineId)
    {
        ValidateClientSlot(clientSlot);
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));

        _clientBaselineId[clientSlot] = baselineId;
        _clientHasBaseline[clientSlot] = true;
    }

    public void InvalidateBaseline(int clientSlot)
    {
        ValidateClientSlot(clientSlot);
        _clientHasBaseline[clientSlot] = false;
        _clientBaselineId[clientSlot] = 0;
    }

    public bool TryGetBaseline(int clientSlot, out long baselineId)
    {
        ValidateClientSlot(clientSlot);
        baselineId = _clientBaselineId[clientSlot];
        return _clientHasBaseline[clientSlot];
    }

    /// <summary>
    /// Builds AOI delta ops into <paramref name="destination"/>.
    /// Prevalidates interest and destination capacity before any destination or tracked-state mutation.
    /// Generation change emits Despawn(old) then Spawn(new). Failures are atomic.
    /// </summary>
    public Physics3DNetAoiDeltaBuildResult BuildDelta(
        int clientSlot,
        long snapshotTick,
        long requiredBaselineId,
        ReadOnlySpan<Physics3DNetAoiInterest> currentInterest,
        Span<Physics3DNetSnapshotEntityWrite> destination)
    {
        ValidateClientSlot(clientSlot);
        Physics3DNetValidation.RequirePositiveTick(snapshotTick, nameof(snapshotTick));
        Physics3DNetValidation.RequireNonNegativeBaselineId(requiredBaselineId, nameof(requiredBaselineId));

        if (!_clientHasBaseline[clientSlot] || _clientBaselineId[clientSlot] != requiredBaselineId)
        {
            return new Physics3DNetAoiDeltaBuildResult(
                Physics3DNetAoiDeltaResultKind.BaselineMissing,
                writtenCount: 0,
                baselineId: requiredBaselineId);
        }

        PrevalidateInterest(currentInterest, snapshotTick);

        int baseIndex = clientSlot * _entityCapacityPerClient;
        int trackedCount = _trackedCount[clientSlot];
        int comparisonCount = 0;
        int writeCount = CountDeltaOperations(
            baseIndex,
            trackedCount,
            currentInterest,
            out int removalCount,
            ref comparisonCount);

        if (writeCount > destination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "aoi delta destination",
                destination.Length,
                snapshotTick);
        }

        // Destination and tracked state remain untouched until all validation and capacity checks pass.
        WriteDeltaOperations(
            baseIndex,
            trackedCount,
            currentInterest,
            destination,
            removalCount,
            requiredBaselineId,
            ref comparisonCount);

        ReplaceTrackedState(baseIndex, trackedCount, currentInterest);
        _trackedCount[clientSlot] = currentInterest.Length;
        _lastBuildEntityIdComparisonCount[clientSlot] = comparisonCount;

        return new Physics3DNetAoiDeltaBuildResult(
            Physics3DNetAoiDeltaResultKind.Built,
            writeCount,
            requiredBaselineId);
    }

    public bool IsTracked(int clientSlot, int networkEntityId, out int generation)
    {
        ValidateClientSlot(clientSlot);
        int baseIndex = clientSlot * _entityCapacityPerClient;
        int trackedIndex = FindTrackedIndex(baseIndex, _trackedCount[clientSlot], networkEntityId);
        if (trackedIndex < 0)
        {
            generation = 0;
            return false;
        }

        generation = _trackedGeneration[trackedIndex];
        return true;
    }

    internal int GetLastBuildEntityIdComparisonCount(int clientSlot)
    {
        ValidateClientSlot(clientSlot);
        return _lastBuildEntityIdComparisonCount[clientSlot];
    }

    private void PrevalidateInterest(ReadOnlySpan<Physics3DNetAoiInterest> currentInterest, long snapshotTick)
    {
        if (currentInterest.Length > _entityCapacityPerClient)
        {
            throw new Physics3DNetCapacityExceededException(
                "aoi interest set",
                _entityCapacityPerClient,
                snapshotTick);
        }

        int previousId = -1;
        for (int i = 0; i < currentInterest.Length; i++)
        {
            int id = currentInterest[i].NetworkEntityId;
            if (id <= previousId)
            {
                throw new ArgumentException(
                    "AOI currentInterest network entity ids must be deterministic, unique, and strictly increasing.",
                    nameof(currentInterest));
            }

            previousId = id;
        }
    }

    private static Physics3DNetSnapshotEntityWrite CreateDespawn(int networkEntityId, int generation, long baselineId) =>
        new(
            networkEntityId,
            generation,
            Physics3DNetReplicationOp.Despawn,
            baselineId,
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Physics3DBodyKind.Dynamic,
            Physics3DNetReplicationMode.RigidBody);

    private static Physics3DNetSnapshotEntityWrite CreateSpawnOrUpdate(
        in Physics3DNetAoiInterest interest,
        Physics3DNetReplicationOp op,
        long baselineId) =>
        new(
            interest.NetworkEntityId,
            interest.Generation,
            op,
            baselineId,
            interest.PositionCm,
            interest.Orientation,
            interest.LinearVelocityCmPerSecond,
            interest.AngularVelocityRadiansPerSecond,
            interest.BodyKind,
            interest.ReplicationMode);

    private int CountDeltaOperations(
        int baseIndex,
        int trackedCount,
        ReadOnlySpan<Physics3DNetAoiInterest> currentInterest,
        out int removalCount,
        ref int comparisonCount)
    {
        int trackedIndex = 0;
        int interestIndex = 0;
        int writeCount = 0;
        removalCount = 0;

        while (trackedIndex < trackedCount && interestIndex < currentInterest.Length)
        {
            int trackedId = _trackedEntityId[baseIndex + trackedIndex];
            int interestId = currentInterest[interestIndex].NetworkEntityId;
            comparisonCount++;

            if (trackedId < interestId)
            {
                trackedIndex++;
                removalCount++;
                writeCount++;
            }
            else if (trackedId > interestId)
            {
                interestIndex++;
                writeCount++;
            }
            else
            {
                writeCount += _trackedGeneration[baseIndex + trackedIndex] == currentInterest[interestIndex].Generation
                    ? 1
                    : 2;
                trackedIndex++;
                interestIndex++;
            }
        }

        int trailingRemovals = trackedCount - trackedIndex;
        removalCount += trailingRemovals;
        writeCount += trailingRemovals + currentInterest.Length - interestIndex;
        return writeCount;
    }

    private void WriteDeltaOperations(
        int baseIndex,
        int trackedCount,
        ReadOnlySpan<Physics3DNetAoiInterest> currentInterest,
        Span<Physics3DNetSnapshotEntityWrite> destination,
        int removalCount,
        long baselineId,
        ref int comparisonCount)
    {
        int trackedIndex = 0;
        int interestIndex = 0;
        int removalWriteIndex = 0;
        int interestWriteIndex = removalCount;

        while (trackedIndex < trackedCount && interestIndex < currentInterest.Length)
        {
            int absoluteTrackedIndex = baseIndex + trackedIndex;
            int trackedId = _trackedEntityId[absoluteTrackedIndex];
            Physics3DNetAoiInterest interest = currentInterest[interestIndex];
            comparisonCount++;

            if (trackedId < interest.NetworkEntityId)
            {
                destination[removalWriteIndex++] = CreateDespawn(
                    trackedId,
                    _trackedGeneration[absoluteTrackedIndex],
                    baselineId);
                trackedIndex++;
            }
            else if (trackedId > interest.NetworkEntityId)
            {
                destination[interestWriteIndex++] = CreateSpawnOrUpdate(
                    interest,
                    Physics3DNetReplicationOp.Spawn,
                    baselineId);
                interestIndex++;
            }
            else
            {
                int trackedGeneration = _trackedGeneration[absoluteTrackedIndex];
                if (trackedGeneration != interest.Generation)
                {
                    destination[interestWriteIndex++] = CreateDespawn(trackedId, trackedGeneration, baselineId);
                    destination[interestWriteIndex++] = CreateSpawnOrUpdate(
                        interest,
                        Physics3DNetReplicationOp.Spawn,
                        baselineId);
                }
                else
                {
                    destination[interestWriteIndex++] = CreateSpawnOrUpdate(
                        interest,
                        Physics3DNetReplicationOp.Update,
                        baselineId);
                }

                trackedIndex++;
                interestIndex++;
            }
        }

        while (trackedIndex < trackedCount)
        {
            int absoluteTrackedIndex = baseIndex + trackedIndex++;
            destination[removalWriteIndex++] = CreateDespawn(
                _trackedEntityId[absoluteTrackedIndex],
                _trackedGeneration[absoluteTrackedIndex],
                baselineId);
        }

        while (interestIndex < currentInterest.Length)
        {
            destination[interestWriteIndex++] = CreateSpawnOrUpdate(
                currentInterest[interestIndex++],
                Physics3DNetReplicationOp.Spawn,
                baselineId);
        }
    }

    private void ReplaceTrackedState(
        int baseIndex,
        int previousTrackedCount,
        ReadOnlySpan<Physics3DNetAoiInterest> currentInterest)
    {
        for (int i = 0; i < currentInterest.Length; i++)
        {
            _trackedEntityId[baseIndex + i] = currentInterest[i].NetworkEntityId;
            _trackedGeneration[baseIndex + i] = currentInterest[i].Generation;
        }

        int clearCount = previousTrackedCount - currentInterest.Length;
        if (clearCount > 0)
        {
            _trackedEntityId.AsSpan(baseIndex + currentInterest.Length, clearCount).Clear();
            _trackedGeneration.AsSpan(baseIndex + currentInterest.Length, clearCount).Clear();
        }
    }

    private int FindTrackedIndex(int baseIndex, int trackedCount, int networkEntityId)
    {
        int low = 0;
        int high = trackedCount - 1;
        while (low <= high)
        {
            int local = low + ((high - low) >> 1);
            int trackedId = _trackedEntityId[baseIndex + local];
            if (trackedId < networkEntityId)
            {
                low = local + 1;
            }
            else if (trackedId > networkEntityId)
            {
                high = local - 1;
            }
            else
            {
                return baseIndex + local;
            }
        }

        return -1;
    }

    private void ValidateClientSlot(int clientSlot)
    {
        if ((uint)clientSlot >= (uint)_clientCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSlot), clientSlot, "Client slot out of range.");
        }
    }
}
