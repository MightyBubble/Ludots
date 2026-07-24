using System;
using System.Numerics;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetInterpolationResultKind : byte
{
    Sampled = 1,
    Underflow = 2,
    Overflow = 3
}

public readonly struct Physics3DNetRemoteSample
{
    public Physics3DNetRemoteSample(
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

public readonly struct Physics3DNetInterpolationSample
{
    public Physics3DNetInterpolationSample(
        Physics3DNetInterpolationResultKind kind,
        long lowerTick,
        long upperTick,
        float alpha,
        Vector3 positionCm,
        Quaternion orientation)
    {
        Kind = kind;
        LowerTick = lowerTick;
        UpperTick = upperTick;
        Alpha = alpha;
        PositionCm = positionCm;
        Orientation = orientation;
    }

    public Physics3DNetInterpolationResultKind Kind { get; }
    public long LowerTick { get; }
    public long UpperTick { get; }
    public float Alpha { get; }
    public Vector3 PositionCm { get; }
    public Quaternion Orientation { get; }
}

/// <summary>
/// Bounded remote-body interpolation buffer with explicit underflow/overflow behavior.
/// Tick jumps/wraps purge all samples older than the retained bounded window.
/// </summary>
public sealed class Physics3DNetRemoteInterpolationBuffer
{
    private readonly int _historyCapacity;
    private readonly int _entityCapacity;
    private readonly int[] _entityId;
    private readonly int[] _generation;
    private readonly bool[] _entityActive;
    private readonly long[] _sampleTick;
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
    private readonly bool[] _sampleOccupied;
    private readonly int[] _sampleCount;

    public Physics3DNetRemoteInterpolationBuffer(Physics3DNetConfig config, int remoteEntityCapacity)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        if (remoteEntityCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteEntityCapacity));
        }

        _historyCapacity = config.RemoteInterpolationHistoryTicks;
        _entityCapacity = remoteEntityCapacity;
        _entityId = new int[_entityCapacity];
        _generation = new int[_entityCapacity];
        _entityActive = new bool[_entityCapacity];
        _sampleCount = new int[_entityCapacity];

        int total = checked(_entityCapacity * _historyCapacity);
        _sampleTick = new long[total];
        _posX = new float[total];
        _posY = new float[total];
        _posZ = new float[total];
        _orientX = new float[total];
        _orientY = new float[total];
        _orientZ = new float[total];
        _orientW = new float[total];
        _linVelX = new float[total];
        _linVelY = new float[total];
        _linVelZ = new float[total];
        _angVelX = new float[total];
        _angVelY = new float[total];
        _angVelZ = new float[total];
        _sampleOccupied = new bool[total];
    }

    public int EntityCapacity => _entityCapacity;
    public int HistoryCapacity => _historyCapacity;

    public void Track(int networkEntityId, int generation)
    {
        Physics3DNetValidation.RequireNonNegativeId(networkEntityId, nameof(networkEntityId));
        Physics3DNetValidation.RequirePositiveGeneration(generation, nameof(generation));

        int slot = FindEntitySlot(networkEntityId);
        if (slot >= 0)
        {
            if (_generation[slot] != generation)
            {
                ClearEntitySamples(slot);
                _generation[slot] = generation;
            }

            _entityActive[slot] = true;
            return;
        }

        slot = AllocateEntitySlot(tick: 0);
        _entityId[slot] = networkEntityId;
        _generation[slot] = generation;
        _entityActive[slot] = true;
        _sampleCount[slot] = 0;
    }

    public void Untrack(int networkEntityId)
    {
        int slot = FindEntitySlot(networkEntityId);
        if (slot < 0)
        {
            return;
        }

        ClearEntitySamples(slot);
        _entityActive[slot] = false;
        _entityId[slot] = 0;
        _generation[slot] = 0;
    }

    public void Push(int networkEntityId, int generation, in Physics3DNetRemoteSample sample)
    {
        int slot = FindEntitySlot(networkEntityId);
        if (slot < 0 || !_entityActive[slot])
        {
            throw new InvalidOperationException(
                $"Remote entity {networkEntityId} is not tracked for interpolation.");
        }

        if (_generation[slot] != generation)
        {
            throw new InvalidOperationException(
                $"Remote entity {networkEntityId} generation mismatch. Tracked {_generation[slot]}, got {generation}.");
        }

        long newest = FindNewestTick(slot);
        if (newest > 0 && sample.Tick <= newest)
        {
            throw new Physics3DNetTemporalOrderException(networkEntityId, newest, sample.Tick);
        }

        // Purge samples older than the retained bounded window relative to the new newest tick.
        long retainFloor = sample.Tick - _historyCapacity + 1;
        if (retainFloor < 1)
        {
            retainFloor = 1;
        }

        PurgeOlderThan(slot, retainFloor);

        int index = SampleIndex(slot, sample.Tick);
        bool wasOccupied = _sampleOccupied[index] && _sampleTick[index] >= retainFloor;
        _sampleTick[index] = sample.Tick;
        _posX[index] = sample.PositionCm.X;
        _posY[index] = sample.PositionCm.Y;
        _posZ[index] = sample.PositionCm.Z;
        _orientX[index] = sample.Orientation.X;
        _orientY[index] = sample.Orientation.Y;
        _orientZ[index] = sample.Orientation.Z;
        _orientW[index] = sample.Orientation.W;
        _linVelX[index] = sample.LinearVelocityCmPerSecond.X;
        _linVelY[index] = sample.LinearVelocityCmPerSecond.Y;
        _linVelZ[index] = sample.LinearVelocityCmPerSecond.Z;
        _angVelX[index] = sample.AngularVelocityRadiansPerSecond.X;
        _angVelY[index] = sample.AngularVelocityRadiansPerSecond.Y;
        _angVelZ[index] = sample.AngularVelocityRadiansPerSecond.Z;
        _sampleOccupied[index] = true;
        if (!wasOccupied)
        {
            _sampleCount[slot]++;
        }
    }

    /// <summary>
    /// Samples at a render tick. Underflow = before oldest sample. Overflow = after newest sample.
    /// </summary>
    public Physics3DNetInterpolationSample Sample(int networkEntityId, float renderTick)
    {
        int slot = FindEntitySlot(networkEntityId);
        if (slot < 0 || !_entityActive[slot] || _sampleCount[slot] == 0)
        {
            return new Physics3DNetInterpolationSample(
                Physics3DNetInterpolationResultKind.Underflow,
                lowerTick: 0,
                upperTick: 0,
                alpha: 0f,
                Vector3.Zero,
                Quaternion.Identity);
        }

        long oldest = FindOldestTick(slot);
        long newest = FindNewestTick(slot);
        if (renderTick < oldest)
        {
            return BuildCorner(Physics3DNetInterpolationResultKind.Underflow, slot, oldest);
        }

        if (renderTick > newest)
        {
            return BuildCorner(Physics3DNetInterpolationResultKind.Overflow, slot, newest);
        }

        long lowerTick = long.MinValue;
        long upperTick = long.MaxValue;
        int baseIndex = slot * _historyCapacity;
        for (int local = 0; local < _historyCapacity; local++)
        {
            int index = baseIndex + local;
            if (!_sampleOccupied[index])
            {
                continue;
            }

            long tick = _sampleTick[index];
            if (tick <= renderTick && tick > lowerTick)
            {
                lowerTick = tick;
            }

            if (tick >= renderTick && tick < upperTick)
            {
                upperTick = tick;
            }
        }

        if (lowerTick == long.MinValue || upperTick == long.MaxValue)
        {
            return new Physics3DNetInterpolationSample(
                Physics3DNetInterpolationResultKind.Underflow,
                0,
                0,
                0f,
                Vector3.Zero,
                Quaternion.Identity);
        }

        int lowerIndex = SampleIndex(slot, lowerTick);
        int upperIndex = SampleIndex(slot, upperTick);
        float alpha = lowerTick == upperTick
            ? 0f
            : (float)((renderTick - lowerTick) / (upperTick - lowerTick));

        Vector3 lowerPos = new(_posX[lowerIndex], _posY[lowerIndex], _posZ[lowerIndex]);
        Vector3 upperPos = new(_posX[upperIndex], _posY[upperIndex], _posZ[upperIndex]);
        Quaternion lowerOrient = new(_orientX[lowerIndex], _orientY[lowerIndex], _orientZ[lowerIndex], _orientW[lowerIndex]);
        Quaternion upperOrient = new(_orientX[upperIndex], _orientY[upperIndex], _orientZ[upperIndex], _orientW[upperIndex]);

        return new Physics3DNetInterpolationSample(
            Physics3DNetInterpolationResultKind.Sampled,
            lowerTick,
            upperTick,
            alpha,
            Vector3.Lerp(lowerPos, upperPos, alpha),
            Quaternion.Slerp(lowerOrient, upperOrient, alpha));
    }

    public int GetSampleCount(int networkEntityId)
    {
        int slot = FindEntitySlot(networkEntityId);
        return slot < 0 ? 0 : _sampleCount[slot];
    }

    public bool TryGetSampleTick(int networkEntityId, long tick, out Physics3DNetRemoteSample sample)
    {
        sample = default;
        int slot = FindEntitySlot(networkEntityId);
        if (slot < 0)
        {
            return false;
        }

        int index = SampleIndex(slot, tick);
        if (!_sampleOccupied[index] || _sampleTick[index] != tick)
        {
            return false;
        }

        sample = new Physics3DNetRemoteSample(
            _sampleTick[index],
            new Vector3(_posX[index], _posY[index], _posZ[index]),
            new Quaternion(_orientX[index], _orientY[index], _orientZ[index], _orientW[index]),
            new Vector3(_linVelX[index], _linVelY[index], _linVelZ[index]),
            new Vector3(_angVelX[index], _angVelY[index], _angVelZ[index]));
        return true;
    }

    private Physics3DNetInterpolationSample BuildCorner(
        Physics3DNetInterpolationResultKind kind,
        int slot,
        long tick)
    {
        int index = SampleIndex(slot, tick);
        return new Physics3DNetInterpolationSample(
            kind,
            tick,
            tick,
            0f,
            new Vector3(_posX[index], _posY[index], _posZ[index]),
            new Quaternion(_orientX[index], _orientY[index], _orientZ[index], _orientW[index]));
    }

    private void PurgeOlderThan(int slot, long retainFloor)
    {
        int baseIndex = slot * _historyCapacity;
        int remaining = 0;
        for (int local = 0; local < _historyCapacity; local++)
        {
            int index = baseIndex + local;
            if (!_sampleOccupied[index])
            {
                continue;
            }

            if (_sampleTick[index] < retainFloor)
            {
                _sampleOccupied[index] = false;
                _sampleTick[index] = 0;
            }
            else
            {
                remaining++;
            }
        }

        _sampleCount[slot] = remaining;
    }

    private int FindEntitySlot(int networkEntityId)
    {
        for (int i = 0; i < _entityCapacity; i++)
        {
            if (_entityActive[i] && _entityId[i] == networkEntityId)
            {
                return i;
            }
        }

        return -1;
    }

    private int AllocateEntitySlot(long tick)
    {
        for (int i = 0; i < _entityCapacity; i++)
        {
            if (!_entityActive[i])
            {
                return i;
            }
        }

        throw new Physics3DNetCapacityExceededException("remote interpolation entities", _entityCapacity, tick);
    }

    private void ClearEntitySamples(int slot)
    {
        int baseIndex = slot * _historyCapacity;
        for (int local = 0; local < _historyCapacity; local++)
        {
            _sampleOccupied[baseIndex + local] = false;
            _sampleTick[baseIndex + local] = 0;
        }

        _sampleCount[slot] = 0;
    }

    private int SampleIndex(int slot, long tick)
    {
        int local = (int)(tick % _historyCapacity);
        if (local < 0)
        {
            local += _historyCapacity;
        }

        return (slot * _historyCapacity) + local;
    }

    private long FindOldestTick(int slot)
    {
        long oldest = long.MaxValue;
        int baseIndex = slot * _historyCapacity;
        for (int local = 0; local < _historyCapacity; local++)
        {
            int index = baseIndex + local;
            if (_sampleOccupied[index] && _sampleTick[index] < oldest)
            {
                oldest = _sampleTick[index];
            }
        }

        return oldest == long.MaxValue ? 0 : oldest;
    }

    private long FindNewestTick(int slot)
    {
        long newest = 0;
        int baseIndex = slot * _historyCapacity;
        for (int local = 0; local < _historyCapacity; local++)
        {
            int index = baseIndex + local;
            if (_sampleOccupied[index] && _sampleTick[index] > newest)
            {
                newest = _sampleTick[index];
            }
        }

        return newest;
    }
}
