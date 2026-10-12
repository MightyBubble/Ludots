using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 分水岭分割（buildRegions 移植）:从开阔区"脊线"按距离层级生长区域,
/// 小区域再合并进同区域码的邻居。区域永不跨导航区域边界。
/// 注意逐位的顺序语义:pending 扫描序、expand 的 8 次迭代上限、mergeSmall 的稳定排序
/// 都是结果的一部分,不是实现细节。
/// </summary>
public static class WatershedRegions
{
    private static readonly int[] OX = { -1, 0, 1, 0 };
    private static readonly int[] OY = { 0, 1, 0, -1 };

    internal static int Build(TileBakeScratch scratch, byte[] passable, byte[] dist, int navCellsPerSide, int minArea)
    {
        int nn = navCellsPerSide * navCellsPerSide;
        var reg = scratch.Reg;
        Array.Clear(reg, 0, nn);
        int maxD = 0, total = 0;
        for (int i = 0; i < nn; i++)
        {
            if (passable[i] != 0)
            {
                total++;
                if (dist[i] > maxD) maxD = dist[i];
            }
        }

        if (total == 0) return 0;

        var off = scratch.Off;
        Array.Clear(off, 0, maxD + 2);
        for (int i = 0; i < nn; i++) if (passable[i] != 0) off[dist[i]]++;
        int acc = 0;
        for (int d = maxD; d >= 0; d--) { int c = off[d]; off[d] = acc; acc += c; }
        var sorted = scratch.Sorted;
        for (int i = 0; i < nn; i++) if (passable[i] != 0) sorted[off[dist[i]]++] = i;

        int pn = 0, ptr = 0, next = 1;
        for (int level = maxD; ; level = Math.Max(1, level - 2))
        {
            while (ptr < total && dist[sorted[ptr]] >= level) scratch.Pending[pn++] = sorted[ptr++];
            Expand(scratch, passable, dist, navCellsPerSide, 8, ref pn);
            for (int k = 0; k < pn; k++)
            {
                int c = scratch.Pending[k];
                if (reg[c] == 0 && Flood(scratch, passable, dist, navCellsPerSide, c, level, next)) next++;
            }

            int w = 0;
            for (int k = 0; k < pn; k++) if (reg[scratch.Pending[k]] == 0) scratch.Pending[w++] = scratch.Pending[k];
            pn = w;
            if (level <= 1) break;
        }

        Expand(scratch, passable, dist, navCellsPerSide, -1, ref pn);
        for (int k = 0; k < pn; k++)
        {
            int c = scratch.Pending[k];
            if (reg[c] == 0 && Flood(scratch, passable, dist, navCellsPerSide, c, 0, next)) next++;
        }

        return MergeSmall(scratch, passable, navCellsPerSide, next, minArea);
    }

    private static void Expand(TileBakeScratch scratch, byte[] passable, byte[] dist, int navCellsPerSide, int maxIter, ref int pn)
    {
        var reg = scratch.Reg;
        var pending = scratch.Pending;
        var assign = scratch.Assign;
        for (int it = 0; maxIter < 0 || it < maxIter; it++)
        {
            int na = 0;
            for (int k = 0; k < pn; k++)
            {
                int c = pending[k];
                if (reg[c] != 0) continue;
                int x = c % navCellsPerSide, y = c / navCellsPerSide;
                int best = 0, bd = -1;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + OX[d], ny = y + OY[d];
                    if (nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide) continue;
                    int ni = ny * navCellsPerSide + nx, r = reg[ni];
                    if (r != 0 && passable[ni] == passable[c] && dist[ni] > bd) { bd = dist[ni]; best = r; }
                }

                if (best != 0) { assign[na++] = c; assign[na++] = best; }
            }

            if (na == 0) return;
            for (int k = 0; k < na; k += 2) reg[assign[k]] = assign[k + 1];
        }
    }

    private static bool Flood(TileBakeScratch scratch, byte[] passable, byte[] dist, int navCellsPerSide, int seed, int level, int id)
    {
        var reg = scratch.Reg;
        var stack = scratch.StackBuf;
        int sp = 0, area = 0;
        int code = passable[seed];
        stack[sp++] = seed;
        reg[seed] = id;
        while (sp > 0)
        {
            int c = stack[--sp];
            int x = c % navCellsPerSide, y = c / navCellsPerSide;
            bool touch = false;
            for (int d = 0; d < 4 && !touch; d++)
            {
                int nx = x + OX[d], ny = y + OY[d];
                if (nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide) continue;
                int r = reg[ny * navCellsPerSide + nx];
                if (r != 0 && r != id && passable[ny * navCellsPerSide + nx] == code) touch = true;
            }

            if (touch) { reg[c] = 0; continue; }
            area++;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + OX[d], ny = y + OY[d];
                if (nx < 0 || ny < 0 || nx >= navCellsPerSide || ny >= navCellsPerSide) continue;
                int ni = ny * navCellsPerSide + nx;
                if (passable[ni] == code && reg[ni] == 0 && dist[ni] >= level && sp < stack.Length)
                {
                    reg[ni] = id;
                    stack[sp++] = ni;
                }
            }
        }

        return area > 0;
    }

    // 小区域只并入同区域码的邻居
    private static int MergeSmall(TileBakeScratch scratch, byte[] code, int navCellsPerSide, int r0, int minArea)
    {
        var reg = scratch.Reg;
        var area = scratch.WArea;
        Array.Clear(area, 0, r0);
        for (int i = 0; i < reg.Length; i++) if (reg[i] != 0) area[reg[i]]++;

        for (int i = 0; i < r0; i++)
        {
            scratch.WNb[i].Clear();
            scratch.WNbIndex[i].Clear();
        }

        for (int y = 0; y < navCellsPerSide; y++)
        {
            for (int x = 0; x < navCellsPerSide; x++)
            {
                int i = y * navCellsPerSide + x, r = reg[i];
                if (r == 0) continue;
                if (x + 1 < navCellsPerSide)
                {
                    int q = reg[i + 1];
                    if (q != 0 && q != r && code[i + 1] == code[i]) { Inc(scratch, r, q); Inc(scratch, q, r); }
                }

                if (y + 1 < navCellsPerSide)
                {
                    int q = reg[i + navCellsPerSide];
                    if (q != 0 && q != r && code[i + navCellsPerSide] == code[i]) { Inc(scratch, r, q); Inc(scratch, q, r); }
                }
            }
        }

        var parent = scratch.WParent;
        for (int i = 0; i < r0; i++) parent[i] = i;

        var ids = scratch.WIds;
        ids.Clear();
        for (int r = 1; r < r0; r++) ids.Add(r);
        SortIdsByArea(ids, area);

        for (int ii = 0; ii < ids.Count; ii++)
        {
            int r = ids[ii];
            if (Find(parent, r) != r || area[r] >= minArea) continue;
            var aggOrder = scratch.WAggOrder;
            var aggCount = scratch.WAggCount;
            aggOrder.Clear();
            aggCount.Clear();
            var nbr = scratch.WNb[r];
            for (int k = 0; k < nbr.Count; k++)
            {
                var (q, c) = nbr[k];
                int t = Find(parent, q);
                if (t == r) continue;
                if (!aggCount.ContainsKey(t)) aggOrder.Add(t);
                aggCount[t] = aggCount.GetValueOrDefault(t) + c;
            }

            int best = -1, bc = 0;
            for (int k = 0; k < aggOrder.Count; k++)
            {
                int t = aggOrder[k];
                int c = aggCount[t];
                if (c > bc) { bc = c; best = t; }
            }

            if (best < 0) continue;
            parent[r] = best;
            area[best] += area[r];
            for (int k = 0; k < aggOrder.Count; k++)
            {
                int t = aggOrder[k];
                if (t == best) continue;
                int c = aggCount[t];
                var idx = scratch.WNbIndex[best];
                var nbBest = scratch.WNb[best];
                if (idx.TryGetValue(t, out int at)) nbBest[at] = (t, nbBest[at].C + c);
                else { idx[t] = nbBest.Count; nbBest.Add((t, c)); }
            }
        }

        var remap = scratch.WRemap;
        Array.Clear(remap, 0, r0);
        int count = 0;
        for (int r = 1; r < r0; r++) { int t = Find(parent, r); if (remap[t] == 0) remap[t] = ++count; }
        for (int i = 0; i < reg.Length; i++) if (reg[i] != 0) reg[i] = remap[Find(parent, reg[i])];
        return count;
    }

    // List.Sort(IComparer) 每次分配排序器包装。比较键是区域面积再加区域号，全序，堆排序结果与不稳定内省排序相同。
    private static void SortIdsByArea(List<int> ids, int[] area)
    {
        int n = ids.Count;
        for (int i = n / 2 - 1; i >= 0; i--) SiftIds(ids, area, n, i);
        for (int end = n - 1; end > 0; end--)
        {
            (ids[0], ids[end]) = (ids[end], ids[0]);
            SiftIds(ids, area, end, 0);
        }
    }

    private static void SiftIds(List<int> ids, int[] area, int n, int root)
    {
        int i = root;
        while (true)
        {
            int left = i * 2 + 1, right = left + 1, best = i;
            if (left < n && IdAfter(area, ids[left], ids[best])) best = left;
            if (right < n && IdAfter(area, ids[right], ids[best])) best = right;
            if (best == i) return;
            (ids[i], ids[best]) = (ids[best], ids[i]);
            i = best;
        }
    }

    private static bool IdAfter(int[] area, int a, int b)
    {
        int ca = area[a], cb = area[b];
        if (ca != cb) return ca > cb;
        return a > b;
    }

    private static void Inc(TileBakeScratch scratch, int r, int q)
    {
        var idx = scratch.WNbIndex[r];
        var nb = scratch.WNb[r];
        if (idx.TryGetValue(q, out int at)) nb[at] = (q, nb[at].C + 1);
        else { idx[q] = nb.Count; nb.Add((q, 1)); }
    }

    private static int Find(int[] parent, int a)
    {
        while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
        return a;
    }
}
