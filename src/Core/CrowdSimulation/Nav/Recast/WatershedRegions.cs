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

    public static (int[] Reg, int Count) Build(byte[] passable, byte[] dist, int n, int minArea)
    {
        int nn = n * n;
        var reg = new int[nn];
        int maxD = 0, total = 0;
        for (int i = 0; i < nn; i++)
        {
            if (passable[i] != 0)
            {
                total++;
                if (dist[i] > maxD) maxD = dist[i];
            }
        }

        if (total == 0) return (reg, 0);

        // 计数排序,距离降序
        var off = new int[maxD + 2];
        for (int i = 0; i < nn; i++) if (passable[i] != 0) off[dist[i]]++;
        int acc = 0;
        for (int d = maxD; d >= 0; d--) { int c = off[d]; off[d] = acc; acc += c; }
        var sorted = new int[total];
        for (int i = 0; i < nn; i++) if (passable[i] != 0) sorted[off[dist[i]]++] = i;

        var pending = new int[total];
        var stack = new int[total * 4 + 4];
        var assign = new int[total * 2];
        int pn = 0, ptr = 0, next = 1;

        void Expand(int maxIter)
        {
            for (int it = 0; maxIter < 0 || it < maxIter; it++)
            {
                int na = 0;
                for (int k = 0; k < pn; k++)
                {
                    int c = pending[k];
                    if (reg[c] != 0) continue;
                    int x = c % n, y = c / n;
                    int best = 0, bd = -1;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + OX[d], ny = y + OY[d];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int ni = ny * n + nx, r = reg[ni];
                        if (r != 0 && passable[ni] == passable[c] && dist[ni] > bd) { bd = dist[ni]; best = r; }
                    }

                    if (best != 0) { assign[na++] = c; assign[na++] = best; }
                }

                if (na == 0) return;
                for (int k = 0; k < na; k += 2) reg[assign[k]] = assign[k + 1];
            }
        }

        bool Flood(int seed, int level, int id)
        {
            int sp = 0, area = 0;
            int code = passable[seed];
            stack[sp++] = seed;
            reg[seed] = id;
            while (sp > 0)
            {
                int c = stack[--sp];
                int x = c % n, y = c / n;
                bool touch = false;
                for (int d = 0; d < 4 && !touch; d++)
                {
                    int nx = x + OX[d], ny = y + OY[d];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int r = reg[ny * n + nx];
                    if (r != 0 && r != id && passable[ny * n + nx] == code) touch = true;
                }

                if (touch) { reg[c] = 0; continue; }
                area++;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + OX[d], ny = y + OY[d];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int ni = ny * n + nx;
                    if (passable[ni] == code && reg[ni] == 0 && dist[ni] >= level && sp < stack.Length) { reg[ni] = id; stack[sp++] = ni; }
                }
            }

            return area > 0;
        }

        for (int level = maxD; ; level = Math.Max(1, level - 2))
        {
            while (ptr < total && dist[sorted[ptr]] >= level) pending[pn++] = sorted[ptr++];
            Expand(8);
            for (int k = 0; k < pn; k++) { int c = pending[k]; if (reg[c] == 0 && Flood(c, level, next)) next++; }
            int w = 0;
            for (int k = 0; k < pn; k++) if (reg[pending[k]] == 0) pending[w++] = pending[k];
            pn = w;
            if (level <= 1) break;
        }

        Expand(-1);
        for (int k = 0; k < pn; k++) { int c = pending[k]; if (reg[c] == 0 && Flood(c, 0, next)) next++; }

        return MergeSmall(reg, passable, n, next, minArea);
    }

    // 小区域只并入同区域码的邻居
    private static (int[] Reg, int Count) MergeSmall(int[] reg, byte[] code, int n, int r0, int minArea)
    {
        var area = new int[r0];
        for (int i = 0; i < reg.Length; i++) if (reg[i] != 0) area[reg[i]]++;

        // 邻接计数:List 保持扫描序插入序(与 JS Map 的插入序迭代一致)
        var nb = new List<(int Q, int C)>[r0];
        var nbIndex = new Dictionary<int, int>[r0];
        for (int i = 0; i < r0; i++) { nb[i] = new List<(int, int)>(); nbIndex[i] = new Dictionary<int, int>(); }

        void Inc(int r, int q)
        {
            var idx = nbIndex[r];
            if (idx.TryGetValue(q, out int at)) nb[r][at] = (q, nb[r][at].C + 1);
            else { idx[q] = nb[r].Count; nb[r].Add((q, 1)); }
        }

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x, r = reg[i];
                if (r == 0) continue;
                if (x + 1 < n) { int q = reg[i + 1]; if (q != 0 && q != r && code[i + 1] == code[i]) { Inc(r, q); Inc(q, r); } }
                if (y + 1 < n) { int q = reg[i + n]; if (q != 0 && q != r && code[i + n] == code[i]) { Inc(r, q); Inc(q, r); } }
            }
        }

        var parent = new int[r0];
        for (int i = 0; i < r0; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }

        // 稳定排序:JS sort 按 area 升序、同值保持下标序
        var ids = new List<int>(r0 - 1);
        for (int r = 1; r < r0; r++) ids.Add(r);
        ids.Sort((a, b) => area[a] != area[b] ? area[a] - area[b] : a - b);

        foreach (var r in ids)
        {
            if (Find(r) != r || area[r] >= minArea) continue;
            // agg:按 nb[r] 的插入序聚合到根的计数
            var aggOrder = new List<int>();
            var aggCount = new Dictionary<int, int>();
            foreach (var (q, c) in nb[r])
            {
                int t = Find(q);
                if (t == r) continue;
                if (!aggCount.ContainsKey(t)) aggOrder.Add(t);
                aggCount[t] = aggCount.GetValueOrDefault(t) + c;
            }

            int best = -1, bc = 0;
            foreach (var t in aggOrder)
            {
                int c = aggCount[t];
                if (c > bc) { bc = c; best = t; }
            }

            if (best < 0) continue;
            parent[r] = best;
            area[best] += area[r];
            foreach (var t in aggOrder)
            {
                if (t == best) continue;
                int c = aggCount[t];
                var idx = nbIndex[best];
                if (idx.TryGetValue(t, out int at)) nb[best][at] = (t, nb[best][at].C + c);
                else { idx[t] = nb[best].Count; nb[best].Add((t, c)); }
            }
        }

        var remap = new int[r0];
        int count = 0;
        for (int r = 1; r < r0; r++) { int t = Find(r); if (remap[t] == 0) remap[t] = ++count; }
        for (int i = 0; i < reg.Length; i++) if (reg[i] != 0) reg[i] = remap[Find(reg[i])];
        return (reg, count);
    }
}
