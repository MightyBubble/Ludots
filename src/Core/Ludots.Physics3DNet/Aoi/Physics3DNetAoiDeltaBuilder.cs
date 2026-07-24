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
/// Distinct clients may BuildDelta concurrently via per-client scratch lanes; same-client concurrency is unsupported.
/// </summary>
public sealed class Physics3DNetAoiDeltaBuilder
{
    private readonly int _clientCapacity;
    private readonly int _entityCapacityPerClient;
    private readonly int[] _trackedEntityId;
    private readonly int[] _trackedGeneration;
    private readonly bool[] _trackedActive;
    private readonly long[] _clientBaselineId;
    private readonly bool[] _clientHasBaseline;
    private readonly bool[] _scratchSeen;

    public Physics3DNetAoiDeltaBuilder(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _clientCapacity = config.ClientCapacity;
        _entityCapacityPerClient = config.AoiEntityCapacityPerClient;
        int total = checked(_clientCapacity * _entityCapacityPerClient);
        _trackedEntityId = new int[total];
        _trackedGeneration = new int[total];
        _trackedActive = new bool[total];
        _clientBaselineId = new long[_clientCapacity];
        _clientHasBaseline = new bool[_clientCapacity];
        // Per-client scratch lanes so distinct clients can build concurrently without sharing scratch.
        _scratchSeen = new bool[total];
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
        Span<bool> scratch = _scratchSeen.AsSpan(baseIndex, _entityCapacityPerClient);
        scratch.Clear();

        int writeCount = 0;
        for (int i = 0; i < currentInterest.Length; i++)
        {
            Physics3DNetAoiInterest interest = currentInterest[i];
            int trackedIndex = FindTrackedIndex(baseIndex, interest.NetworkEntityId);
            if (trackedIndex < 0)
            {
                writeCount++; // enter/spawn
                continue;
            }

            int local = trackedIndex - baseIndex;
            scratch[local] = true;
            if (!_trackedActive[trackedIndex])
            {
                writeCount++; // spawn
            }
            else if (_trackedGeneration[trackedIndex] != interest.Generation)
            {
                writeCount += 2; // Despawn(old generation) then Spawn(new generation)
            }
            else
            {
                writeCount++; // update
            }
        }

        for (int local = 0; local < _entityCapacityPerClient; local++)
        {
            int trackedIndex = baseIndex + local;
            if (_trackedActive[trackedIndex] && !scratch[local])
            {
                writeCount++; // leave/despawn
            }
        }

        if (writeCount > destination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "aoi delta destination",
                destination.Length,
                snapshotTick);
        }

        // Mutate destination and tracked state only after capacity + interest validation.
        scratch.Clear();
        for (int i = 0; i < currentInterest.Length; i++)
        {
            Physics3DNetAoiInterest interest = currentInterest[i];
            int trackedIndex = FindTrackedIndex(baseIndex, interest.NetworkEntityId);
            if (trackedIndex >= 0)
            {
                scratch[trackedIndex - baseIndex] = true;
            }
        }

        int writeIndex = 0;
        for (int local = 0; local < _entityCapacityPerClient; local++)
        {
            int trackedIndex = baseIndex + local;
            if (!_trackedActive[trackedIndex] || scratch[local])
            {
                continue;
            }

            destination[writeIndex++] = CreateDespawn(
                _trackedEntityId[trackedIndex],
                _trackedGeneration[trackedIndex],
                requiredBaselineId);

            _trackedActive[trackedIndex] = false;
            _trackedGeneration[trackedIndex] = 0;
            _trackedEntityId[trackedIndex] = 0;
        }

        for (int i = 0; i < currentInterest.Length; i++)
        {
            Physics3DNetAoiInterest interest = currentInterest[i];
            int trackedIndex = FindTrackedIndex(baseIndex, interest.NetworkEntityId);
            if (trackedIndex < 0)
            {
                trackedIndex = AllocateTrackedSlot(baseIndex, snapshotTick);
                destination[writeIndex++] = CreateSpawnOrUpdate(interest, Physics3DNetReplicationOp.Spawn, requiredBaselineId);
            }
            else if (_trackedGeneration[trackedIndex] != interest.Generation)
            {
                destination[writeIndex++] = CreateDespawn(
                    _trackedEntityId[trackedIndex],
                    _trackedGeneration[trackedIndex],
                    requiredBaselineId);
                destination[writeIndex++] = CreateSpawnOrUpdate(interest, Physics3DNetReplicationOp.Spawn, requiredBaselineId);
            }
            else
            {
                destination[writeIndex++] = CreateSpawnOrUpdate(interest, Physics3DNetReplicationOp.Update, requiredBaselineId);
            }

            _trackedEntityId[trackedIndex] = interest.NetworkEntityId;
            _trackedGeneration[trackedIndex] = interest.Generation;
            _trackedActive[trackedIndex] = true;
        }

        return new Physics3DNetAoiDeltaBuildResult(
            Physics3DNetAoiDeltaResultKind.Built,
            writeIndex,
            requiredBaselineId);
    }

    public bool IsTracked(int clientSlot, int networkEntityId, out int generation)
    {
        ValidateClientSlot(clientSlot);
        int baseIndex = clientSlot * _entityCapacityPerClient;
        int trackedIndex = FindTrackedIndex(baseIndex, networkEntityId);
        if (trackedIndex < 0 || !_trackedActive[trackedIndex])
        {
            generation = 0;
            return false;
        }

        generation = _trackedGeneration[trackedIndex];
        return true;
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

    private int FindTrackedIndex(int baseIndex, int networkEntityId)
    {
        for (int local = 0; local < _entityCapacityPerClient; local++)
        {
            int index = baseIndex + local;
            if (_trackedActive[index] && _trackedEntityId[index] == networkEntityId)
            {
                return index;
            }
        }

        return -1;
    }

    private int AllocateTrackedSlot(int baseIndex, long snapshotTick)
    {
        for (int local = 0; local < _entityCapacityPerClient; local++)
        {
            int index = baseIndex + local;
            if (!_trackedActive[index])
            {
                return index;
            }
        }

        throw new Physics3DNetCapacityExceededException(
            "aoi tracked entities",
            _entityCapacityPerClient,
            snapshotTick);
    }

    private void ValidateClientSlot(int clientSlot)
    {
        if ((uint)clientSlot >= (uint)_clientCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSlot), clientSlot, "Client slot out of range.");
        }
    }
}
