using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// Tile 缓存(TileCache 移植):以 tile 的精确输入为键(逐格 0 = 不可走,
/// 否则区域编号 + 1;不做哈希,无碰撞)。内容相同的 tile 跨位置 / 跨上下文共享一份烘焙;
/// 拆建筑恢复原键即命中。LRU 容量来自 navtile.cacheCapacity。
/// </summary>
public sealed class NavTileCache
{
    private readonly int _capacity;
    private readonly int _tileCells;
    private readonly double _minRegionArea;
    private readonly double _maxSimplificationError;
    private readonly double _maxEdgeLen;
    private readonly int _maxVertsPerPoly;

    // 插入序 Map 语义的 LRU;string 键 = 逐格内容(与参考实现 fromCharCode 同义的精确内容键)
    private readonly Dictionary<string, LinkedListNode<(string Key, NavTileEntry Entry)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, NavTileEntry Entry)> _lru = new();
    private readonly ushort[] _win;
    private int _uid;

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Evictions { get; private set; }
    public int Bakes { get; private set; }
    public int Count => _map.Count;

    /// <summary>测试 / 对拍用:逐条目的内容键与条目。</summary>
    public IEnumerable<(string Key, NavTileEntry Entry)> Entries => _lru.Select(e => (e.Key, e.Entry));

    public NavTileCache(int capacity, int tileCells, double minRegionArea, double maxSimplificationError, double maxEdgeLen, int maxVertsPerPoly)
    {
        _capacity = capacity;
        _tileCells = tileCells;
        _minRegionArea = minRegionArea;
        _maxSimplificationError = maxSimplificationError;
        _maxEdgeLen = maxEdgeLen;
        _maxVertsPerPoly = maxVertsPerPoly;
        _win = new ushort[tileCells * tileCells];
    }

    /// <summary>取 tile (tx, ty) 的缓存条目,未命中即烘焙。</summary>
    public NavTileEntry Acquire(byte[] passable, byte[] area, int n, int tx, int ty)
    {
        int t = _tileCells, ox = tx * t, oy = ty * t;
        for (int y = 0; y < t; y++)
        {
            for (int x = 0; x < t; x++)
            {
                int c = (oy + y) * n + ox + x;
                _win[y * t + x] = passable[c] != 0 ? (ushort)(area[c] + 1) : (ushort)0;
            }
        }

        var chars = new char[_win.Length];
        for (int i = 0; i < _win.Length; i++) chars[i] = (char)_win[i];
        string key = new string(chars);

        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddLast(node);
            Hits++;
            return node.Value.Entry;
        }

        Misses++;
        Bakes++;
        var entry = NavTileBaker.Bake(_win, t, _minRegionArea, _maxSimplificationError, _maxEdgeLen, _maxVertsPerPoly);
        entry.Uid = _uid++;
        var newNode = _lru.AddLast((key, entry));
        _map[key] = newNode;
        while (_map.Count > _capacity)
        {
            var first = _lru.First!;
            _lru.RemoveFirst();
            _map.Remove(first.Value.Key);
            Evictions++;
        }

        return entry;
    }
}
