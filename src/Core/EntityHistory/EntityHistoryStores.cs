using System;

namespace Ludots.Core.EntityHistory;

public enum EntityHistoryStoreResult : byte
{
    Added = 0,
    Replaced = 1,
    CapacityRejected = 2,
}

public sealed class EntitySnapshotStore
{
    private readonly EntitySnapshot[] _values;
    private readonly bool[] _active;
    private int _count;

    public EntitySnapshotStore(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new EntitySnapshot[capacity];
        _active = new bool[capacity];
    }

    public int Capacity => _values.Length;
    public int Count => _count;

    public EntityHistoryStoreResult Upsert(in EntitySnapshot snapshot)
    {
        int slot = Find(snapshot.Identity);
        if (slot >= 0)
        {
            _values[slot] = snapshot;
            return EntityHistoryStoreResult.Replaced;
        }

        if (_count == _values.Length)
            return EntityHistoryStoreResult.CapacityRejected;

        for (int i = 0; i < _active.Length; i++)
        {
            if (_active[i]) continue;
            _active[i] = true;
            _values[i] = snapshot;
            _count++;
            return EntityHistoryStoreResult.Added;
        }

        return EntityHistoryStoreResult.CapacityRejected;
    }

    public bool TryGet(in EntityRef identity, out EntitySnapshot snapshot)
    {
        int slot = Find(identity);
        if (slot >= 0)
        {
            snapshot = _values[slot];
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool Remove(in EntityRef identity)
    {
        int slot = Find(identity);
        if (slot < 0) return false;
        _active[slot] = false;
        _values[slot] = default;
        _count--;
        return true;
    }

    private int Find(in EntityRef identity)
    {
        for (int i = 0; i < _active.Length; i++)
            if (_active[i] && _values[i].Identity == identity) return i;
        return -1;
    }
}

public sealed class KnowledgeSnapshotStore
{
    private readonly KnowledgeSnapshot[] _values;
    private readonly bool[] _active;
    private int _count;

    public KnowledgeSnapshotStore(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new KnowledgeSnapshot[capacity];
        _active = new bool[capacity];
    }

    public int Capacity => _values.Length;
    public int Count => _count;

    public EntityHistoryStoreResult Upsert(in KnowledgeSnapshot snapshot)
    {
        int slot = Find(snapshot.Viewer, snapshot.Target);
        if (slot >= 0)
        {
            _values[slot] = snapshot;
            return EntityHistoryStoreResult.Replaced;
        }

        if (_count == _values.Length)
            return EntityHistoryStoreResult.CapacityRejected;

        for (int i = 0; i < _active.Length; i++)
        {
            if (_active[i]) continue;
            _active[i] = true;
            _values[i] = snapshot;
            _count++;
            return EntityHistoryStoreResult.Added;
        }

        return EntityHistoryStoreResult.CapacityRejected;
    }

    public bool TryGet(in EntityRef viewer, in EntityRef target, int currentTick, out KnowledgeSnapshot snapshot)
    {
        int slot = Find(viewer, target);
        if (slot >= 0 && !_values[slot].IsExpired(currentTick))
        {
            snapshot = _values[slot];
            return true;
        }

        snapshot = default;
        return false;
    }

    public bool TryGetExpired(in EntityRef viewer, in EntityRef target, out KnowledgeSnapshot snapshot)
    {
        int slot = Find(viewer, target);
        if (slot >= 0)
        {
            snapshot = _values[slot];
            return true;
        }

        snapshot = default;
        return false;
    }

    private int Find(in EntityRef viewer, in EntityRef target)
    {
        for (int i = 0; i < _active.Length; i++)
            if (_active[i] && _values[i].Viewer == viewer && _values[i].Target == target) return i;
        return -1;
    }
}

public sealed class EffectExecutionRecordStore
{
    private readonly EffectExecutionRecord[] _values;
    private int _next;
    private int _count;

    public EffectExecutionRecordStore(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new EffectExecutionRecord[capacity];
    }

    public int Capacity => _values.Length;
    public int Count => _count;

    public bool TryAdd(in EffectExecutionRecord record, out int slot)
    {
        if (_count == _values.Length)
        {
            slot = -1;
            return false;
        }

        slot = _next;
        _values[_next] = record;
        _next = (_next + 1) % _values.Length;
        _count++;
        return true;
    }

    public bool TryGet(int slot, out EffectExecutionRecord record)
    {
        if ((uint)slot >= (uint)_count)
        {
            record = default;
            return false;
        }

        record = _values[slot];
        return true;
    }
}
