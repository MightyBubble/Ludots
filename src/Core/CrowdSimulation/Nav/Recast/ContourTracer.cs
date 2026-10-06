using System;
using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Nav.Recast;

/// <summary>
/// 轮廓追踪（buildContours 移植）:沿区域边界走格角点,标签 = 跨过该段的邻域
/// （0 = 墙,-1 - dir = tile 外该侧）。tile 边界段永不简化成墙、tile 角必保留,
/// 跨 tile 拼接的边界因此逐位精确。Douglas-Peucker 用整数叉积 + 坐标并列决胜,
/// 共享边界从两侧简化结果一致。
/// </summary>
public static class ContourTracer
{
    private static readonly int[] OX = { -1, 0, 1, 0 };
    private static readonly int[] OY = { 0, 1, 0, -1 };

    public static List<List<int>>[] BuildContours(int[] reg, int regionCount, int n, double maxError, double maxEdgeLen)
    {
        var flags = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x, r = reg[i];
                if (r == 0) continue;
                int f = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + OX[d], ny = y + OY[d];
                    int q = nx < 0 || ny < 0 || nx >= n || ny >= n ? 0 : reg[ny * n + nx];
                    if (q != r) f |= 1 << d;
                }

                flags[i] = (byte)f;
            }
        }

        var loops = new List<List<int>>[regionCount + 1];
        for (int i = 0; i <= regionCount; i++) loops[i] = new List<List<int>>();
        for (int i = 0; i < n * n; i++)
        {
            while (flags[i] != 0)
            {
                var raw = new List<int>();
                Walk(i % n, i / n, flags, reg, n, raw);
                var pts = Simplify(raw, maxError, maxEdgeLen);
                if (pts.Count >= 6) loops[reg[i]].Add(pts);
            }
        }

        return loops;
    }

    private static void Walk(int sx, int sy, byte[] flags, int[] reg, int n, List<int> output)
    {
        int x = sx, y = sy, i = y * n + x;
        int dir = 0;
        while ((flags[i] & (1 << dir)) == 0) dir++;
        int startDir = dir, startI = i;
        for (int iter = 0; iter < 4 * n * n; iter++)
        {
            if ((flags[i] & (1 << dir)) != 0)
            {
                int px = x, py = y;
                if (dir == 0) py++;
                else if (dir == 1) { px++; py++; }
                else if (dir == 2) px++;
                int nx = x + OX[dir], ny = y + OY[dir];
                output.Add(px);
                output.Add(py);
                output.Add(nx < 0 || ny < 0 || nx >= n || ny >= n ? -1 - dir : reg[ny * n + nx]);
                flags[i] &= (byte)~(1 << dir);
                dir = (dir + 1) & 3;
            }
            else
            {
                x += OX[dir]; y += OY[dir]; i = y * n + x;
                dir = (dir + 3) & 3;
            }

            if (i == startI && dir == startDir) break;
        }
    }

    // 标签变化处先保留,其余段 Douglas-Peucker
    private static List<int> Simplify(List<int> raw, double maxError, double maxEdgeLen)
    {
        int n = raw.Count / 3;
        int X(int i) => raw[i * 3];
        int Y(int i) => raw[i * 3 + 1];
        int L(int i) => raw[i * 3 + 2];

        var keep = new List<int>();
        for (int i = 0; i < n; i++) if (L(i) != L((i + 1) % n)) keep.Add(i);
        if (keep.Count < 2)
        {
            int ll = 0, ur = 0;
            for (int i = 1; i < n; i++)
            {
                if (X(i) < X(ll) || (X(i) == X(ll) && Y(i) < Y(ll))) ll = i;
                if (X(i) > X(ur) || (X(i) == X(ur) && Y(i) > Y(ur))) ur = i;
            }

            keep = new List<int> { Math.Min(ll, ur), Math.Max(ll, ur) };
        }

        var outIdx = new List<int>();
        double e2 = maxError * maxError, m2 = maxEdgeLen * maxEdgeLen;

        void Dp(int a, int b, bool wall)
        {
            int ax = X(a), ay = Y(a), dx = X(b) - ax, dy = Y(b) - ay;
            int len2 = dx * dx + dy * dy;
            int best = -1, bestC = -1, cnt = 0;
            for (int i = (a + 1) % n; i != b; i = (i + 1) % n)
            {
                cnt++;
                int c = len2 != 0
                    ? Math.Abs(dx * (Y(i) - ay) - dy * (X(i) - ax))
                    : (X(i) - ax) * (X(i) - ax) + (Y(i) - ay) * (Y(i) - ay);
                if (c > bestC || (c == bestC && best >= 0 && (X(i) < X(best) || (X(i) == X(best) && Y(i) < Y(best))))) { bestC = c; best = i; }
            }

            if (best < 0) return;
            bool split = len2 == 0 || bestC * (double)bestC > e2 * len2;
            if (!split && wall && len2 > m2) { split = true; best = (a + 1 + (cnt >> 1)) % n; }
            if (!split) return;
            Dp(a, best, wall);
            outIdx.Add(best);
            Dp(best, b, wall);
        }

        for (int k = 0; k < keep.Count; k++)
        {
            int a = keep[k], b = keep[(k + 1) % keep.Count];
            outIdx.Add(a);
            Dp(a, b, L((a + 1) % n) == 0);
        }

        var pts = new List<int>();
        foreach (int i in outIdx)
        {
            int x = X(i), y = Y(i), m = pts.Count;
            if (m > 0 && pts[m - 2] == x && pts[m - 1] == y) continue;
            pts.Add(x);
            pts.Add(y);
        }

        if (pts.Count >= 4 && pts[0] == pts[pts.Count - 2] && pts[1] == pts[pts.Count - 1]) pts.RemoveRange(pts.Count - 2, 2);
        return pts;
    }
}
