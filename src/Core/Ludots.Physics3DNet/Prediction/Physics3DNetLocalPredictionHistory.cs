using System;
using System.Numerics;
using Ludots.Core.Physics3DNet.Input;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetLocalDrivenKind : byte
{
    Character = 1,
    Vehicle = 2
}

public readonly struct Physics3DNetPredictedPose
{
    public Physics3DNetPredictedPose(
        long tick,
        Vector3 positionCm,
        Quaternion orientation,
        Vector3 linearVelocityCmPerSecond,
        Vector3 angularVelocityRadiansPerSecond)
    {
        Physics3DNetValidation.RequirePositiveTick(tick, nameof(tick));
        Physics3DNetValidation.RequireFinite(positionCm, nameof(positionCm));
        Physics3DNetValidation.RequireUnitQuaternion(orientation, nameof(orientation));
        Physics3DNetValidation.RequireFinite(linearVelocityCmPerSecond, nameof(linearVelocityCmPerSecond));
        Physics3DNetValidation.RequireFinite(angularVelocityRadiansPerSecond, nameof(angularVelocityRadiansPerSecond));

        Tick = tick;
        PositionCm = positionCm;
        Orientation = orientation;
        LinearVelocityCmPerSecond = linearVelocityCmPerSecond;
        AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
    }

    public long Tick { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
    public Vector3 LinearVelocityCmPerSecond { get; }
    public Vector3 AngularVelocityRadiansPerSecond { get; }
}

public readonly struct Physics3DNetCorrectionReplayRange
{
    public Physics3DNetCorrectionReplayRange(long fromTickInclusive, long toTickInclusive, int frameCount)
    {
        FromTickInclusive = fromTickInclusive;
        ToTickInclusive = toTickInclusive;
        FrameCount = frameCount;
    }

    public long FromTickInclusive { get; }
    public long ToTickInclusive { get; }
    public int FrameCount { get; }
}

/// <summary>
/// Local prediction history for only the local Character3D or locally driven Vehicle3D.
/// Stores exact <see cref="Physics3DFixedInputFrameCodec.PayloadBytes"/> input payloads in fixed SoA lanes.
/// Rejects attempts to roll back arbitrary remote/world entities.
/// </summary>
public sealed class Physics3DNetLocalPredictionHistory
{
    private readonly int _capacity;
    private readonly long[] _tick;
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
    private readonly byte[] _inputPayloads;
    private readonly bool[] _occupied;

    private bool _bound;
    private int _boundNetworkEntityId;
    private int _boundGeneration;
    private Physics3DNetLocalDrivenKind _boundKind;
    private long _confirmedTick;
    private int _count;

    public Physics3DNetLocalPredictionHistory(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _capacity = config.LocalPredictionHistoryTicks;
        _tick = new long[_capacity];
        _posX = new float[_capacity];
        _posY = new float[_capacity];
        _posZ = new float[_capacity];
        _orientX = new float[_capacity];
        _orientY = new float[_capacity];
        _orientZ = new float[_capacity];
        _orientW = new float[_capacity];
        _linVelX = new float[_capacity];
        _linVelY = new float[_capacity];
        _linVelZ = new float[_capacity];
        _angVelX = new float[_capacity];
        _angVelY = new float[_capacity];
        _angVelZ = new float[_capacity];
        _inputPayloads = new byte[checked(_capacity * Physics3DFixedInputFrameCodec.PayloadBytes)];
        _occupied = new bool[_capacity];
    }

    public int Capacity => _capacity;
    public int InputPayloadBytes => Physics3DFixedInputFrameCodec.PayloadBytes;
    public bool IsBound => _bound;
    public int BoundNetworkEntityId => _boundNetworkEntityId;
    public int BoundGeneration => _boundGeneration;
    public Physics3DNetLocalDrivenKind BoundKind => _boundKind;
    public long ConfirmedTick => _confirmedTick;
    public int Count => _count;

    public void BindLocalDriven(int networkEntityId, int generation, Physics3DNetLocalDrivenKind kind)
    {
        if (networkEntityId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(networkEntityId));
        }

        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        Physics3DNetValidation.RequireValidLocalDrivenKind(kind, nameof(kind));

        if (_bound
            && (_boundNetworkEntityId != networkEntityId
                || _boundGeneration != generation
                || _boundKind != kind))
        {
            throw new InvalidOperationException(
                $"Local prediction already bound to entity {_boundNetworkEntityId}:{_boundGeneration} ({_boundKind}).");
        }

        _bound = true;
        _boundNetworkEntityId = networkEntityId;
        _boundGeneration = generation;
        _boundKind = kind;
    }

    public void Record(in Physics3DNetPredictedPose pose, ReadOnlySpan<byte> inputPayload)
    {
        EnsureBound();
        if (pose.Tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pose), pose.Tick, "Pose tick must be positive.");
        }

        if (inputPayload.Length != Physics3DFixedInputFrameCodec.PayloadBytes)
        {
            throw new ArgumentException(
                $"Input payload must be exactly {Physics3DFixedInputFrameCodec.PayloadBytes} bytes.",
                nameof(inputPayload));
        }

        if (!Physics3DFixedInputFrameCodec.TryDecode(inputPayload, out _))
        {
            throw new ArgumentException(
                "Input payload must match Physics3DFixedInputFrameCodec semantics.",
                nameof(inputPayload));
        }

        int index = IndexForTick(pose.Tick);
        if (_occupied[index] && _tick[index] == pose.Tick)
        {
            throw new InvalidOperationException($"Prediction history already contains tick {pose.Tick}.");
        }

        bool wasOccupiedSameCell = _occupied[index];
        _tick[index] = pose.Tick;
        _posX[index] = pose.PositionCm.X;
        _posY[index] = pose.PositionCm.Y;
        _posZ[index] = pose.PositionCm.Z;
        _orientX[index] = pose.Orientation.X;
        _orientY[index] = pose.Orientation.Y;
        _orientZ[index] = pose.Orientation.Z;
        _orientW[index] = pose.Orientation.W;
        _linVelX[index] = pose.LinearVelocityCmPerSecond.X;
        _linVelY[index] = pose.LinearVelocityCmPerSecond.Y;
        _linVelZ[index] = pose.LinearVelocityCmPerSecond.Z;
        _angVelX[index] = pose.AngularVelocityRadiansPerSecond.X;
        _angVelY[index] = pose.AngularVelocityRadiansPerSecond.Y;
        _angVelZ[index] = pose.AngularVelocityRadiansPerSecond.Z;
        inputPayload.CopyTo(PayloadSpan(index));
        _occupied[index] = true;
        if (!wasOccupiedSameCell)
        {
            if (_count < _capacity)
            {
                _count++;
            }
        }
    }

    public void Confirm(int networkEntityId, int generation, long confirmedTick)
    {
        EnsureBoundEntity(networkEntityId, generation);
        if (confirmedTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedTick));
        }

        if (confirmedTick < _confirmedTick)
        {
            throw new InvalidOperationException(
                $"Confirmed tick must be monotonic. Current {_confirmedTick}, requested {confirmedTick}.");
        }

        _confirmedTick = confirmedTick;
    }

    public Physics3DNetCorrectionReplayRange BeginCorrectionReplay(
        int networkEntityId,
        int generation,
        long authoritativeConfirmedTick,
        Span<Physics3DNetPredictedPose> poseDestination,
        Span<byte> inputPayloadDestination)
    {
        EnsureBoundEntity(networkEntityId, generation);
        if (authoritativeConfirmedTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authoritativeConfirmedTick));
        }

        Confirm(networkEntityId, generation, authoritativeConfirmedTick);

        long fromTick = authoritativeConfirmedTick + 1;
        long toTick = FindNewestTick();
        if (toTick < fromTick)
        {
            return new Physics3DNetCorrectionReplayRange(fromTick, authoritativeConfirmedTick, frameCount: 0);
        }

        int needed = checked((int)(toTick - fromTick + 1));
        int neededPayloadBytes = checked(needed * Physics3DFixedInputFrameCodec.PayloadBytes);
        if (needed > poseDestination.Length || neededPayloadBytes > inputPayloadDestination.Length)
        {
            throw new Physics3DNetCapacityExceededException(
                "local correction replay destination",
                Math.Min(poseDestination.Length, inputPayloadDestination.Length / Physics3DFixedInputFrameCodec.PayloadBytes),
                authoritativeConfirmedTick);
        }

        int written = 0;
        for (long tick = fromTick; tick <= toTick; tick++)
        {
            int index = IndexForTick(tick);
            if (!_occupied[index] || _tick[index] != tick)
            {
                throw new InvalidOperationException(
                    $"Local prediction history missing tick {tick} required for correction replay.");
            }

            poseDestination[written] = new Physics3DNetPredictedPose(
                _tick[index],
                new Vector3(_posX[index], _posY[index], _posZ[index]),
                new Quaternion(_orientX[index], _orientY[index], _orientZ[index], _orientW[index]),
                new Vector3(_linVelX[index], _linVelY[index], _linVelZ[index]),
                new Vector3(_angVelX[index], _angVelY[index], _angVelZ[index]));
            PayloadSpan(index).CopyTo(
                inputPayloadDestination.Slice(
                    written * Physics3DFixedInputFrameCodec.PayloadBytes,
                    Physics3DFixedInputFrameCodec.PayloadBytes));
            written++;
        }

        return new Physics3DNetCorrectionReplayRange(fromTick, toTick, written);
    }

    public void RejectRemoteOrWorldRollback(int networkEntityId, int generation)
    {
        if (!_bound
            || networkEntityId != _boundNetworkEntityId
            || generation != _boundGeneration)
        {
            throw new InvalidOperationException(
                $"Rejected rollback of remote/world entity {networkEntityId}:{generation}. Only the bound local Character/Vehicle may be corrected.");
        }
    }

    public bool TryGet(
        long tick,
        out Physics3DNetPredictedPose pose,
        Span<byte> inputPayloadDestination,
        out int bytesWritten)
    {
        EnsureBound();
        bytesWritten = 0;
        int index = IndexForTick(tick);
        if (!_occupied[index] || _tick[index] != tick)
        {
            pose = default;
            return false;
        }

        if (inputPayloadDestination.Length < Physics3DFixedInputFrameCodec.PayloadBytes)
        {
            throw new ArgumentException(
                $"Destination must hold {Physics3DFixedInputFrameCodec.PayloadBytes} payload bytes.",
                nameof(inputPayloadDestination));
        }

        pose = new Physics3DNetPredictedPose(
            _tick[index],
            new Vector3(_posX[index], _posY[index], _posZ[index]),
            new Quaternion(_orientX[index], _orientY[index], _orientZ[index], _orientW[index]),
            new Vector3(_linVelX[index], _linVelY[index], _linVelZ[index]),
            new Vector3(_angVelX[index], _angVelY[index], _angVelZ[index]));
        PayloadSpan(index).CopyTo(inputPayloadDestination[..Physics3DFixedInputFrameCodec.PayloadBytes]);
        bytesWritten = Physics3DFixedInputFrameCodec.PayloadBytes;
        return true;
    }

    private Span<byte> PayloadSpan(int index) =>
        _inputPayloads.AsSpan(index * Physics3DFixedInputFrameCodec.PayloadBytes, Physics3DFixedInputFrameCodec.PayloadBytes);

    private long FindNewestTick()
    {
        long newest = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (_occupied[i] && _tick[i] > newest)
            {
                newest = _tick[i];
            }
        }

        return newest;
    }

    private int IndexForTick(long tick)
    {
        int index = (int)(tick % _capacity);
        if (index < 0)
        {
            index += _capacity;
        }

        return index;
    }

    private void EnsureBound()
    {
        if (!_bound)
        {
            throw new InvalidOperationException("Local prediction history is not bound to a Character/Vehicle.");
        }
    }

    private void EnsureBoundEntity(int networkEntityId, int generation)
    {
        EnsureBound();
        RejectRemoteOrWorldRollback(networkEntityId, generation);
    }
}
