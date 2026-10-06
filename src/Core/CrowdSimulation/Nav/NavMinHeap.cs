using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>二叉最小堆(heap.js 移植):Fix64 键 + int 值,无逐次分配;同级比较结构与参考一致。</summary>
public sealed class NavMinHeap
{
    private Fix64[] _keys;
    private int[] _vals;

    public NavMinHeap(int capacity = 1024)
    {
        _keys = new Fix64[capacity];
        _vals = new int[capacity];
    }

    public int Size { get; private set; }

    public void Clear() => Size = 0;

    public void Push(Fix64 key, int val)
    {
        if (Size == _keys.Length)
        {
            Array.Resize(ref _keys, _keys.Length * 2);
            Array.Resize(ref _vals, _vals.Length * 2);
        }

        int i = Size++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_keys[p] <= key) break;
            _keys[i] = _keys[p]; _vals[i] = _vals[p]; i = p;
        }

        _keys[i] = key; _vals[i] = val;
    }

    public Fix64 PeekKey() => _keys[0];

    public int Pop()
    {
        var keys = _keys; var vals = _vals;
        int top = vals[0];
        int n = --Size;
        if (n > 0)
        {
            var key = keys[n]; var val = vals[n];
            int i = 0;
            for (; ; )
            {
                int c = 2 * i + 1;
                if (c >= n) break;
                if (c + 1 < n && keys[c + 1] < keys[c]) c++;
                if (keys[c] >= key) break;
                keys[i] = keys[c]; vals[i] = vals[c]; i = c;
            }

            keys[i] = key; vals[i] = val;
        }

        return top;
    }
}
