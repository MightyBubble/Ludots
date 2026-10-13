using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Nav.GridBake;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// Tile 缓存:以 tile 的精确输入为键(逐格 0 = 不可走,
/// 否则区域编号 + 1;不做哈希,无碰撞)。内容相同的 tile 跨位置 / 跨上下文共享一份烘焙;
/// 拆建筑恢复原键即命中。LRU 容量来自 navtile.cacheCapacity。
/// 这份缓存归烘焙/重烘。路径服务不持第二份导航态,worker 只持搜索暂存,计算时拿 PathQueryService 的导航锁。
/// 缓存自己的烘焙暂存不可重入。
/// 槽位在构造时备好。未命中领取空槽并写入,不分配;已发布的条目对象不被原地覆盖,
/// 因为导航上下文会长期持有它。只有缓存满后的淘汰才换一块新缓冲。
/// </summary>
public sealed class NavTileCache
{
    private readonly int _capacity;
    private readonly int _navCellsPerTile;
    private readonly double _minRegionArea;
    private readonly double _maxSimplificationError;
    private readonly double _maxEdgeLen;
    private readonly int _maxVertsPerPoly;
    private readonly TileBakeScratch _bakeScratch;
    private readonly Slot[] _slots;
    private readonly int[] _free;
    private int _freeCount;
    private int _lruHead = -1;
    private int _lruTail = -1;
    private int _live;
    private readonly System.Diagnostics.Stopwatch _bakeWatch = new();
    private int _uid;

    private struct Slot
    {
        public ushort[] Key;
        public NavTileEntry Entry;
        public int Prev;
        public int Next;
    }

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Evictions { get; private set; }
    public int Bakes { get; private set; }
    /// <summary>累计烘焙耗时(遥测,不参与任何状态与摘要)。</summary>
    public double BakeMs { get; private set; }
    public int Count => _live;
    public int Capacity => _capacity;

    /// <summary>测试 / 对拍用:逐条目的内容键与条目。</summary>
    public IEnumerable<(string Key, NavTileEntry Entry)> Entries
    {
        get
        {
            for (int i = _lruHead; i >= 0; i = _slots[i].Next)
            {
                var key = _slots[i].Key;
                var chars = new char[key.Length];
                for (int c = 0; c < key.Length; c++) chars[c] = (char)key[c];
                yield return (new string(chars), _slots[i].Entry);
            }
        }
    }

    public NavTileCache(int capacity, int navCellsPerTile, double minRegionArea, double maxSimplificationError, double maxEdgeLen, int maxVertsPerPoly)
    {
        _capacity = capacity;
        _navCellsPerTile = navCellsPerTile;
        _minRegionArea = minRegionArea;
        _maxSimplificationError = maxSimplificationError;
        _maxEdgeLen = maxEdgeLen;
        _maxVertsPerPoly = maxVertsPerPoly;
        _bakeScratch = new TileBakeScratch(navCellsPerTile, maxVertsPerPoly);
        int nn = navCellsPerTile * navCellsPerTile;
        _slots = new Slot[capacity];
        _free = new int[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _slots[i].Key = new ushort[nn];
            _slots[i].Entry = NavTileEntry.CreateBuffer(navCellsPerTile, maxVertsPerPoly);
            _slots[i].Prev = -1;
            _slots[i].Next = -1;
            _free[i] = i;
        }

        _freeCount = capacity;
    }

    public NavTileEntry Acquire(byte[] passable, byte[] area, int navCellsPerSide, int tx, int ty)
    {
        int navCellsPerTile = _navCellsPerTile, ox = tx * navCellsPerTile, oy = ty * navCellsPerTile;
        long tScan = CrowdRebakeProbe.T();
        for (int i = _lruHead; i >= 0; i = _slots[i].Next)
        {
            if (!SameContent(_slots[i].Key, passable, area, navCellsPerSide, ox, oy, navCellsPerTile)) continue;
            MoveToTail(i);
            Hits++;
            if (CrowdRebakeProbe.Armed) CrowdRebakeProbe.CacheHits++;
            CrowdRebakeProbe.Acc(ref CrowdRebakeProbe.CacheScan, tScan);
            return _slots[i].Entry;
        }

        CrowdRebakeProbe.Acc(ref CrowdRebakeProbe.CacheScan, tScan);

        int slot;
        if (_live == _capacity)
        {
            slot = _lruHead;
            Unlink(slot);
            _live--;
            Evictions++;
            _slots[slot].Entry = NavTileEntry.CreateBuffer(navCellsPerTile, _maxVertsPerPoly);
        }
        else
        {
            slot = _free[--_freeCount];
        }

        var key = _slots[slot].Key;
        for (int y = 0; y < navCellsPerTile; y++)
        {
            int row = (oy + y) * navCellsPerSide + ox;
            int dst = y * navCellsPerTile;
            for (int x = 0; x < navCellsPerTile; x++)
            {
                int c = row + x;
                key[dst + x] = passable[c] != 0 ? (ushort)(area[c] + 1) : (ushort)0;
            }
        }

        Misses++;
        Bakes++;
        if (CrowdRebakeProbe.Armed) CrowdRebakeProbe.CacheMisses++;
        _bakeWatch.Restart();
        long tBake = CrowdRebakeProbe.T();
        var entry = _slots[slot].Entry;
        BakeInto(entry, key);
        CrowdRebakeProbe.Acc(ref CrowdRebakeProbe.CacheBake, tBake);
        BakeMs += _bakeWatch.Elapsed.TotalMilliseconds;
        entry.Uid = _uid++;
        LinkLast(slot);
        _live++;
        return entry;
    }

    private void BakeInto(NavTileEntry entry, ushort[] key)
    {
        NavTileBaker.BakeInto(entry, key, _navCellsPerTile, _minRegionArea, _maxSimplificationError, _maxEdgeLen, _maxVertsPerPoly, _bakeScratch);
    }

    private static bool SameContent(ushort[] key, byte[] passable, byte[] area, int navCellsPerSide, int ox, int oy, int navCellsPerTile)
    {
        for (int y = 0; y < navCellsPerTile; y++)
        {
            int row = (oy + y) * navCellsPerSide + ox;
            int dst = y * navCellsPerTile;
            for (int x = 0; x < navCellsPerTile; x++)
            {
                int c = row + x;
                ushort expect = passable[c] != 0 ? (ushort)(area[c] + 1) : (ushort)0;
                if (key[dst + x] != expect) return false;
            }
        }

        return true;
    }

    private void MoveToTail(int i)
    {
        if (i == _lruTail) return;
        Unlink(i);
        LinkLast(i);
    }

    private void LinkLast(int i)
    {
        _slots[i].Prev = _lruTail;
        _slots[i].Next = -1;
        if (_lruTail >= 0) _slots[_lruTail].Next = i;
        else _lruHead = i;
        _lruTail = i;
    }

    private void Unlink(int i)
    {
        int prev = _slots[i].Prev, next = _slots[i].Next;
        if (prev >= 0) _slots[prev].Next = next;
        else _lruHead = next;
        if (next >= 0) _slots[next].Prev = prev;
        else _lruTail = prev;
        _slots[i].Prev = -1;
        _slots[i].Next = -1;
    }
}
