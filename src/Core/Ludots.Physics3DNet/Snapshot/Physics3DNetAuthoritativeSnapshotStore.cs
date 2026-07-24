using System;
using System.Numerics;
using Ludots.Core.Physics3D;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetReplicationOp : byte
{
    Spawn = 1,
    Update = 2,
    Despawn = 3
}

public enum Physics3DNetReplicationMode : byte
{
    RigidBody = 1,
    Character = 2,
    Vehicle = 3,
    Ragdoll = 4
}

public readonly struct Physics3DNetSnapshotEntityWrite
{
    public Physics3DNetSnapshotEntityWrite(
        int networkEntityId,
        int generation,
        Physics3DNetReplicationOp op,
        long baselineId,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        Physics3DBodyKind bodyKind,
        Physics3DNetReplicationMode replicationMode)
    {
        Physics3DNetValidation.RequireNonNegativeId(networkEntityId, nameof(networkEntityId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));
        Physics3DNetValidation.RequireValidReplicationOp(op, nameof(op));
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));
        Physics3DNetValidation.RequireFinite(positionCm, nameof(positionCm));
        Physics3DNetValidation.RequireUnitQuaternion(orientation, nameof(orientation));
        Physics3DNetValidation.RequireFinite(linearVelocityCmPerSecond, nameof(linearVelocityCmPerSecond));
        Physics3DNetValidation.RequireFinite(angularVelocityRadiansPerSecond, nameof(angularVelocityRadiansPerSecond));
        Physics3DNetValidation.RequireValidBodyKind(bodyKind, nameof(bodyKind));
        Physics3DNetValidation.RequireValidReplicationMode(replicationMode, nameof(replicationMode));

        NetworkEntityId = networkEntityId;
        Generation = generation;
        Op = op;
        BaselineId = baselineId;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
        BodyKind = bodyKind;
        ReplicationMode = replicationMode;
    }

    public int NetworkEntityId { get; }
    public int Generation { get; }
    public Physics3DNetReplicationOp Op { get; }
    public long BaselineId { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
    public Physics3DBodyKind BodyKind { get; }
    public Physics3DNetReplicationMode ReplicationMode { get; }
}

public readonly struct Physics3DNetSnapshotEntityView
{
    public Physics3DNetSnapshotEntityView(
        int networkEntityId,
        int generation,
        Physics3DNetReplicationOp op,
        long baselineId,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond,
        Physics3DBodyKind bodyKind,
        Physics3DNetReplicationMode replicationMode)
    {
        NetworkEntityId = networkEntityId;
        Generation = generation;
        Op = op;
        BaselineId = baselineId;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
        BodyKind = bodyKind;
        ReplicationMode = replicationMode;
    }

    public int NetworkEntityId { get; }
    public int Generation { get; }
    public Physics3DNetReplicationOp Op { get; }
    public long BaselineId { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
    public Physics3DBodyKind BodyKind { get; }
    public Physics3DNetReplicationMode ReplicationMode { get; }
}

/// <summary>
/// Fixed-capacity authoritative snapshot SoA with staged streaming writes.
/// Published state is only replaced on successful EndWrite / ReplaceAll.
/// </summary>
public sealed class Physics3DNetAuthoritativeSnapshotStore
{
    private readonly int[] _networkEntityId;
    private readonly int[] _generation;
    private readonly Physics3DNetReplicationOp[] _op;
    private readonly long[] _baselineId;
    private readonly float[] _posX;
    private readonly float[] _posY;
    private readonly float[] _posZ;
    private readonly float[] _orientX;
    private readonly float[] _orientY;
    private readonly float[] _orientZ;
    private readonly float[] _orientW;
    private readonly float[] _linVelX;
    private readonly float[] _linVelY;
    private readonly float[] _linVelZ;
    private readonly float[] _angVelX;
    private readonly float[] _angVelY;
    private readonly float[] _angVelZ;
    private readonly Physics3DBodyKind[] _bodyKind;
    private readonly Physics3DNetReplicationMode[] _replicationMode;

    private readonly int[] _stagingNetworkEntityId;
    private readonly int[] _stagingGeneration;
    private readonly Physics3DNetReplicationOp[] _stagingOp;
    private readonly long[] _stagingBaselineId;
    private readonly float[] _stagingPosX;
    private readonly float[] _stagingPosY;
    private readonly float[] _stagingPosZ;
    private readonly float[] _stagingOrientX;
    private readonly float[] _stagingOrientY;
    private readonly float[] _stagingOrientZ;
    private readonly float[] _stagingOrientW;
    private readonly float[] _stagingLinVelX;
    private readonly float[] _stagingLinVelY;
    private readonly float[] _stagingLinVelZ;
    private readonly float[] _stagingAngVelX;
    private readonly float[] _stagingAngVelY;
    private readonly float[] _stagingAngVelZ;
    private readonly Physics3DBodyKind[] _stagingBodyKind;
    private readonly Physics3DNetReplicationMode[] _stagingReplicationMode;

    private int _count;
    private long _snapshotTick;
    private long _baselineIdValue;

    private bool _writing;
    private int _expectedWriteCount;
    private int _stagingCount;
    private long _stagingTick;
    private long _stagingBaselineIdValue;

    public Physics3DNetAuthoritativeSnapshotStore(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        Capacity = config.SnapshotEntityCapacity;
        _networkEntityId = new int[Capacity];
        _generation = new int[Capacity];
        _op = new Physics3DNetReplicationOp[Capacity];
        _baselineId = new long[Capacity];
        _posX = new float[Capacity];
        _posY = new float[Capacity];
        _posZ = new float[Capacity];
        _orientX = new float[Capacity];
        _orientY = new float[Capacity];
        _orientZ = new float[Capacity];
        _orientW = new float[Capacity];
        _linVelX = new float[Capacity];
        _linVelY = new float[Capacity];
        _linVelZ = new float[Capacity];
        _angVelX = new float[Capacity];
        _angVelY = new float[Capacity];
        _angVelZ = new float[Capacity];
        _bodyKind = new Physics3DBodyKind[Capacity];
        _replicationMode = new Physics3DNetReplicationMode[Capacity];

        _stagingNetworkEntityId = new int[Capacity];
        _stagingGeneration = new int[Capacity];
        _stagingOp = new Physics3DNetReplicationOp[Capacity];
        _stagingBaselineId = new long[Capacity];
        _stagingPosX = new float[Capacity];
        _stagingPosY = new float[Capacity];
        _stagingPosZ = new float[Capacity];
        _stagingOrientX = new float[Capacity];
        _stagingOrientY = new float[Capacity];
        _stagingOrientZ = new float[Capacity];
        _stagingOrientW = new float[Capacity];
        _stagingLinVelX = new float[Capacity];
        _stagingLinVelY = new float[Capacity];
        _stagingLinVelZ = new float[Capacity];
        _stagingAngVelX = new float[Capacity];
        _stagingAngVelY = new float[Capacity];
        _stagingAngVelZ = new float[Capacity];
        _stagingBodyKind = new Physics3DBodyKind[Capacity];
        _stagingReplicationMode = new Physics3DNetReplicationMode[Capacity];
    }

    public int Capacity { get; }
    public int Count => _count;
    public long SnapshotTick => _snapshotTick;
    public long BaselineId => _baselineIdValue;
    public bool IsWriting => _writing;

    public void BeginWrite(long snapshotTick, long baselineId, int expectedEntityCount)
    {
        if (_writing)
        {
            throw new InvalidOperationException("A snapshot write is already in progress. Call EndWrite or AbortWrite first.");
        }

        Physics3DNetValidation.RequirePositiveTick(snapshotTick, nameof(snapshotTick));
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));
        if (expectedEntityCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedEntityCount));
        }

        if (expectedEntityCount > Capacity)
        {
            throw new Physics3DNetCapacityExceededException("authoritative snapshot entities", Capacity, snapshotTick);
        }

        _writing = true;
        _expectedWriteCount = expectedEntityCount;
        _stagingCount = 0;
        _stagingTick = snapshotTick;
        _stagingBaselineIdValue = baselineId;
    }

    public void Write(in Physics3DNetSnapshotEntityWrite entity)
    {
        if (!_writing)
        {
            throw new InvalidOperationException("BeginWrite must be called before Write.");
        }

        if (_stagingCount >= _expectedWriteCount)
        {
            AbortWrite();
            throw new InvalidOperationException(
                $"Too many snapshot writes: expected {_expectedWriteCount}, attempted {_stagingCount + 1}. Published snapshot left intact.");
        }

        if (entity.BaselineId != _stagingBaselineIdValue)
        {
            AbortWrite();
            throw new ArgumentException(
                $"Entity baseline id {entity.BaselineId} does not match write baseline {_stagingBaselineIdValue}.",
                nameof(entity));
        }

        if (_stagingCount > 0 && entity.NetworkEntityId <= _stagingNetworkEntityId[_stagingCount - 1])
        {
            AbortWrite();
            throw new ArgumentException(
                "Snapshot entity network ids must be deterministic and strictly increasing (no duplicates).",
                nameof(entity));
        }

        int index = _stagingCount;
        WriteStaging(index, entity);
        _stagingCount = index + 1;
    }

    /// <summary>
    /// Commits the staged write only when the predeclared expected count was met exactly.
    /// Too few writes abort without mutating the published snapshot.
    /// </summary>
    public void EndWrite()
    {
        if (!_writing)
        {
            throw new InvalidOperationException("EndWrite requires an active BeginWrite.");
        }

        if (_stagingCount != _expectedWriteCount)
        {
            int actual = _stagingCount;
            int expected = _expectedWriteCount;
            AbortWrite();
            throw new InvalidOperationException(
                $"Snapshot EndWrite expected {expected} entities but received {actual}. Published snapshot left intact.");
        }

        PublishStaging();
        _writing = false;
        _expectedWriteCount = 0;
        _stagingCount = 0;
    }

    public void AbortWrite()
    {
        _writing = false;
        _expectedWriteCount = 0;
        _stagingCount = 0;
        _stagingTick = 0;
        _stagingBaselineIdValue = 0;
    }

    /// <summary>
    /// Atomically replaces snapshot contents. Prevalidates all inputs before any mutation.
    /// On overflow/validation failure, previous published contents remain unchanged.
    /// </summary>
    public void ReplaceAll(long snapshotTick, long baselineId, ReadOnlySpan<Physics3DNetSnapshotEntityWrite> entities)
    {
        if (_writing)
        {
            throw new InvalidOperationException("Cannot ReplaceAll while a streaming write is in progress.");
        }

        PrevalidateReplaceAll(snapshotTick, baselineId, entities);

        BeginWrite(snapshotTick, baselineId, entities.Length);
        for (int i = 0; i < entities.Length; i++)
        {
            WriteStaging(i, entities[i]);
        }

        _stagingCount = entities.Length;
        PublishStaging();
        _writing = false;
        _expectedWriteCount = 0;
        _stagingCount = 0;
    }

    public Physics3DNetSnapshotEntityView Get(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new Physics3DNetSnapshotEntityView(
            _networkEntityId[index],
            _generation[index],
            _op[index],
            _baselineId[index],
            new Vector3(_posX[index], _posY[index], _posZ[index]),
            new Quaternion(_orientX[index], _orientY[index], _orientZ[index], _orientW[index]),
            new Vector3(_linVelX[index], _linVelY[index], _linVelZ[index]),
            new Vector3(_angVelX[index], _angVelY[index], _angVelZ[index]),
            _bodyKind[index],
            _replicationMode[index]);
    }

    public int CopyTo(Span<Physics3DNetSnapshotEntityView> destination)
    {
        if (destination.Length < _count)
        {
            throw new Physics3DNetCapacityExceededException("snapshot copy destination", destination.Length, _snapshotTick);
        }

        for (int i = 0; i < _count; i++)
        {
            destination[i] = Get(i);
        }

        return _count;
    }

    private void PrevalidateReplaceAll(
        long snapshotTick,
        long baselineId,
        ReadOnlySpan<Physics3DNetSnapshotEntityWrite> entities)
    {
        Physics3DNetValidation.RequirePositiveTick(snapshotTick, nameof(snapshotTick));
        Physics3DNetValidation.RequireNonNegativeBaselineId(baselineId, nameof(baselineId));

        if (entities.Length > Capacity)
        {
            throw new Physics3DNetCapacityExceededException("authoritative snapshot entities", Capacity, snapshotTick);
        }

        int previousId = -1;
        for (int i = 0; i < entities.Length; i++)
        {
            Physics3DNetSnapshotEntityWrite entity = entities[i];
            if (entity.BaselineId != baselineId)
            {
                throw new ArgumentException(
                    $"Entity baseline id {entity.BaselineId} does not match replace baseline {baselineId}.",
                    nameof(entities));
            }

            // Strictly increasing ids give O(n) uniqueness; no O(n²) scan.
            if (entity.NetworkEntityId <= previousId)
            {
                throw new ArgumentException(
                    "Snapshot entity network ids must be deterministic and strictly increasing (no duplicates).",
                    nameof(entities));
            }

            previousId = entity.NetworkEntityId;
        }
    }

    private void WriteStaging(int index, in Physics3DNetSnapshotEntityWrite entity)
    {
        _stagingNetworkEntityId[index] = entity.NetworkEntityId;
        _stagingGeneration[index] = entity.Generation;
        _stagingOp[index] = entity.Op;
        _stagingBaselineId[index] = entity.BaselineId;
        _stagingPosX[index] = entity.PositionCm.X;
        _stagingPosY[index] = entity.PositionCm.Y;
        _stagingPosZ[index] = entity.PositionCm.Z;
        _stagingOrientX[index] = entity.Orientation.X;
        _stagingOrientY[index] = entity.Orientation.Y;
        _stagingOrientZ[index] = entity.Orientation.Z;
        _stagingOrientW[index] = entity.Orientation.W;
        _stagingLinVelX[index] = entity.LinearVelocityCmPerSecond.X;
        _stagingLinVelY[index] = entity.LinearVelocityCmPerSecond.Y;
        _stagingLinVelZ[index] = entity.LinearVelocityCmPerSecond.Z;
        _stagingAngVelX[index] = entity.AngularVelocityRadiansPerSecond.X;
        _stagingAngVelY[index] = entity.AngularVelocityRadiansPerSecond.Y;
        _stagingAngVelZ[index] = entity.AngularVelocityRadiansPerSecond.Z;
        _stagingBodyKind[index] = entity.BodyKind;
        _stagingReplicationMode[index] = entity.ReplicationMode;
    }

    private void PublishStaging()
    {
        for (int i = 0; i < _stagingCount; i++)
        {
            _networkEntityId[i] = _stagingNetworkEntityId[i];
            _generation[i] = _stagingGeneration[i];
            _op[i] = _stagingOp[i];
            _baselineId[i] = _stagingBaselineId[i];
            _posX[i] = _stagingPosX[i];
            _posY[i] = _stagingPosY[i];
            _posZ[i] = _stagingPosZ[i];
            _orientX[i] = _stagingOrientX[i];
            _orientY[i] = _stagingOrientY[i];
            _orientZ[i] = _stagingOrientZ[i];
            _orientW[i] = _stagingOrientW[i];
            _linVelX[i] = _stagingLinVelX[i];
            _linVelY[i] = _stagingLinVelY[i];
            _linVelZ[i] = _stagingLinVelZ[i];
            _angVelX[i] = _stagingAngVelX[i];
            _angVelY[i] = _stagingAngVelY[i];
            _angVelZ[i] = _stagingAngVelZ[i];
            _bodyKind[i] = _stagingBodyKind[i];
            _replicationMode[i] = _stagingReplicationMode[i];
        }

        _count = _stagingCount;
        _snapshotTick = _stagingTick;
        _baselineIdValue = _stagingBaselineIdValue;
    }
}
