using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 流场结果缓存。键 = 导航号 × N² + 目标格。
/// 命中还要求已有掩码覆盖这次要的走廊。
/// 容量按条目计。引用（跟随的组与在途请求）和缓存标记都放开才回池。
/// </summary>
public sealed class FlowFieldCache
{
    private readonly int _n2;
    private readonly int _capacity;
    private readonly FlowField?[] _fields;
    private readonly long[] _keys;
    private readonly int[] _prev;
    private readonly int[] _next;
    private readonly int[] _hash;
    private readonly int[] _free;
    private readonly byte[] _seen;
    private int _freeTop;
    private int _count;
    private int _head = -1;
    private int _tail = -1;

    public FlowFieldCache(int capacity, int cellCountSquared)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (cellCountSquared < 1) throw new ArgumentOutOfRangeException(nameof(cellCountSquared));
        _capacity = capacity;
        _n2 = cellCountSquared;
        _fields = new FlowField?[capacity];
        _keys = new long[capacity];
        _prev = new int[capacity];
        _next = new int[capacity];
        _seen = new byte[capacity];
        int slots = 1;
        int need = capacity << 1;
        while (slots < need) slots <<= 1;
        _hash = new int[slots];
        Array.Fill(_hash, -1);
        _free = new int[capacity];
        for (int i = 0; i < capacity; i++) _free[i] = i;
        _freeTop = capacity;
    }

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Evicts { get; private set; }
    public int Count => _count;
    public int Capacity => _capacity;

    public long KeyOf(int navId, int goal) => (long)navId * _n2 + goal;

    public bool TryHit(long key, byte[] needed, out FlowField field)
    {
        int slot = Find(key);
        if (slot < 0)
        {
            Misses++;
            field = null!;
            return false;
        }

        var found = _fields[slot];
        if (found?.Mask == null || found.Mask.Length != needed.Length || !FlowFieldBuilder.MaskSubset(needed, found.Mask))
        {
            Misses++;
            field = null!;
            return false;
        }

        Hits++;
        Touch(slot);
        field = found;
        return true;
    }

    /// <summary>只按键取，不看掩码覆盖。同样计命中 / 未命中，并把条目挪到最新。</summary>
    public FlowField? Get(long key)
    {
        int slot = Find(key);
        if (slot < 0)
        {
            Misses++;
            return null;
        }

        Hits++;
        Touch(slot);
        return _fields[slot];
    }

    public void Admit(FlowField field)
    {
        if (field.Mask == null) return;
        long key = KeyOf(field.NavId, field.Goal);
        int existing = Find(key);
        if (existing >= 0 && ReferenceEquals(_fields[existing], field))
        {
            field.Cached = true;
            Touch(existing);
            return;
        }

        if (existing >= 0)
        {
            var previous = _fields[existing]!;
            previous.Cached = false;
            _fields[existing] = field;
            _keys[existing] = key;
            field.Cached = true;
            Touch(existing);
            Release(previous);
            return;
        }

        if (_count == _capacity) EvictAt(_head);
        int slot = _free[--_freeTop];
        _fields[slot] = field;
        _keys[slot] = key;
        field.Cached = true;
        LinkNewest(slot);
        InsertHash(slot);
        _count++;
    }

    public void Release(FlowField? field)
    {
        if (field == null || field.Refs > 0 || field.Cached || field.Pooled) return;
        field.OriginPool?.Give(field);
    }

    public void DropAll()
    {
        while (_head >= 0) EvictAt(_head);
    }

    public void EvictTouching(int navId, IReadOnlyList<int> tiles)
    {
        int slot = _head;
        while (slot >= 0)
        {
            int next = _next[slot];
            if ((int)(_keys[slot] / _n2) == navId && Touches(_fields[slot], tiles)) EvictAt(slot);
            slot = next;
        }
    }

    public void EvictSlot(int beliefSlot, int stride)
    {
        int slot = _head;
        while (slot >= 0)
        {
            int next = _next[slot];
            int navId = (int)(_keys[slot] / _n2);
            if (navId >= stride && navId / stride == beliefSlot) EvictAt(slot);
            slot = next;
        }
    }

    public bool Contains(FlowField field)
    {
        int slot = Find(KeyOf(field.NavId, field.Goal));
        return slot >= 0 && ReferenceEquals(_fields[slot], field);
    }

    internal int CopyOldestFirst(long[] keys)
    {
        int n = 0;
        for (int i = _head; i >= 0 && n < keys.Length; i = _next[i]) keys[n++] = _keys[i];
        return n;
    }

    internal string? InvalidReason()
    {
        Array.Clear(_seen, 0, _seen.Length);
        int seen = 0;
        int slot = _head;
        while (slot >= 0)
        {
            if ((uint)slot >= (uint)_capacity || _seen[slot] != 0) return "流场缓存 LRU 成环";
            _seen[slot] = 1;
            seen++;
            var field = _fields[slot];
            if (field == null || !field.Cached) return "流场缓存条目未标记";
            if (field.Pooled || (field.OriginPool != null && field.OriginPool.Holds(field))) return "流场缓存条目已回池";
            if (KeyOf(field.NavId, field.Goal) != _keys[slot]) return "流场缓存键与导航目标不一致";
            if (Find(_keys[slot]) != slot) return "流场缓存哈希找不到条目";
            slot = _next[slot];
            if (seen > _count) return "流场缓存 LRU 成环";
        }

        if (seen != _count) return "流场缓存 LRU 长度与计数不一致";
        return null;
    }

    private static bool Touches(FlowField? field, IReadOnlyList<int> tiles)
    {
        var mask = field?.Mask;
        if (mask == null) return false;
        for (int i = 0; i < tiles.Count; i++)
        {
            int tile = tiles[i];
            if ((uint)tile < (uint)mask.Length && mask[tile] != 0) return true;
        }

        return false;
    }

    private void EvictAt(int slot)
    {
        var field = _fields[slot];
        Unlink(slot);
        _fields[slot] = null;
        _free[_freeTop++] = slot;
        _count--;
        Evicts++;
        Rehash();
        if (field == null) return;
        field.Cached = false;
        Release(field);
    }

    private void Rehash()
    {
        Array.Fill(_hash, -1);
        for (int i = _head; i >= 0; i = _next[i]) InsertHash(i);
    }

    private void InsertHash(int slot)
    {
        int mask = _hash.Length - 1;
        int index = MixHash(_keys[slot]) & mask;
        while (_hash[index] >= 0) index = (index + 1) & mask;
        _hash[index] = slot;
    }

    private int Find(long key)
    {
        int mask = _hash.Length - 1;
        int index = MixHash(key) & mask;
        for (int n = 0; n < _hash.Length; n++)
        {
            int slot = _hash[index];
            if (slot < 0) return -1;
            if (_keys[slot] == key) return slot;
            index = (index + 1) & mask;
        }

        return -1;
    }

    private void Touch(int slot)
    {
        if (slot == _tail) return;
        Unlink(slot);
        LinkNewest(slot);
    }

    private void LinkNewest(int slot)
    {
        _prev[slot] = _tail;
        _next[slot] = -1;
        if (_tail >= 0) _next[_tail] = slot;
        else _head = slot;
        _tail = slot;
    }

    private void Unlink(int slot)
    {
        int previous = _prev[slot];
        int next = _next[slot];
        if (previous >= 0) _next[previous] = next;
        else _head = next;
        if (next >= 0) _prev[next] = previous;
        else _tail = previous;
    }

    private static int MixHash(long key)
    {
        ulong x = (ulong)key;
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return (int)x;
    }
}
