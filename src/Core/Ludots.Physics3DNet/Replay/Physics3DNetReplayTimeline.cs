using System;

namespace Ludots.Core.Physics3DNet;

public enum Physics3DNetReplayEventKind : byte
{
    InputAccepted = 1,
    SnapshotPublished = 2,
    StateHashCompared = 3
}

public readonly struct Physics3DNetReplayEvent
{
    public Physics3DNetReplayEvent(
        Physics3DNetReplayEventKind kind,
        long tick,
        ulong leftHash,
        ulong rightHash,
        bool hashesMatch)
    {
        Kind = kind;
        Tick = tick;
        LeftHash = leftHash;
        RightHash = rightHash;
        HashesMatch = hashesMatch;
    }

    public Physics3DNetReplayEventKind Kind { get; }
    public long Tick { get; }
    public ulong LeftHash { get; }
    public ulong RightHash { get; }
    public bool HashesMatch { get; }
}

public readonly struct Physics3DNetReplayDivergence
{
    public Physics3DNetReplayDivergence(bool found, long firstDivergentTick, ulong leftHash, ulong rightHash)
    {
        Found = found;
        FirstDivergentTick = firstDivergentTick;
        LeftHash = leftHash;
        RightHash = rightHash;
    }

    public bool Found { get; }
    public long FirstDivergentTick { get; }
    public ulong LeftHash { get; }
    public ulong RightHash { get; }
}

/// <summary>
/// Replay/determinism checking timeline.
/// Records input/snapshot/hash comparison and reports the first divergent tick.
/// This is not full world rollback.
/// </summary>
public sealed class Physics3DNetReplayTimeline
{
    private readonly Physics3DNetReplayEventKind[] _kind;
    private readonly long[] _tick;
    private readonly ulong[] _leftHash;
    private readonly ulong[] _rightHash;
    private readonly bool[] _hashesMatch;
    private int _count;

    public Physics3DNetReplayTimeline(Physics3DNetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        Capacity = config.ReplayEventCapacity;
        _kind = new Physics3DNetReplayEventKind[Capacity];
        _tick = new long[Capacity];
        _leftHash = new ulong[Capacity];
        _rightHash = new ulong[Capacity];
        _hashesMatch = new bool[Capacity];
    }

    public int Capacity { get; }
    public int Count => _count;

    public void RecordInputAccepted(long tick)
    {
        Append(new Physics3DNetReplayEvent(
            Physics3DNetReplayEventKind.InputAccepted,
            tick,
            leftHash: 0,
            rightHash: 0,
            hashesMatch: true));
    }

    public void RecordSnapshotPublished(long tick)
    {
        Append(new Physics3DNetReplayEvent(
            Physics3DNetReplayEventKind.SnapshotPublished,
            tick,
            leftHash: 0,
            rightHash: 0,
            hashesMatch: true));
    }

    public void RecordHashComparison(long tick, ulong leftHash, ulong rightHash)
    {
        Append(new Physics3DNetReplayEvent(
            Physics3DNetReplayEventKind.StateHashCompared,
            tick,
            leftHash,
            rightHash,
            leftHash == rightHash));
    }

    public Physics3DNetReplayEvent Get(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new Physics3DNetReplayEvent(
            _kind[index],
            _tick[index],
            _leftHash[index],
            _rightHash[index],
            _hashesMatch[index]);
    }

    public Physics3DNetReplayDivergence FindFirstDivergence()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_kind[i] == Physics3DNetReplayEventKind.StateHashCompared && !_hashesMatch[i])
            {
                return new Physics3DNetReplayDivergence(
                    found: true,
                    _tick[i],
                    _leftHash[i],
                    _rightHash[i]);
            }
        }

        return new Physics3DNetReplayDivergence(found: false, firstDivergentTick: 0, leftHash: 0, rightHash: 0);
    }

    private void Append(in Physics3DNetReplayEvent evt)
    {
        if (_count >= Capacity)
        {
            throw new Physics3DNetCapacityExceededException("replay timeline events", Capacity, evt.Tick);
        }

        int index = _count;
        _kind[index] = evt.Kind;
        _tick[index] = evt.Tick;
        _leftHash[index] = evt.LeftHash;
        _rightHash[index] = evt.RightHash;
        _hashesMatch[index] = evt.HashesMatch;
        _count = index + 1;
    }
}
