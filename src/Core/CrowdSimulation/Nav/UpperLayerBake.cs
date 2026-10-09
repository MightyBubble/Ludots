using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>桥面层地表(每地图一份,与上下文无关):桥面格区域、桥头标记、首写 tile 序。</summary>
public sealed class DeckSurface
{
    public required byte[] AreaIndex { get; init; }
    public required byte[] PortalFlag { get; init; }
    /// <summary>含桥面格的 tile 编号,按首次写入序(与参考实现的栅格化扫描序一致)。</summary>
    public required int[] TileOrder { get; init; }

    public static DeckSurface Empty(int cellCount) => new()
    {
        AreaIndex = new byte[cellCount * cellCount],
        PortalFlag = new byte[cellCount * cellCount],
        TileOrder = Array.Empty<int>(),
    };
}

/// <summary>
/// 桥面层烘焙（网格级移植）：桥实体（路径足迹）栅格化为
/// 桥面格 + 桥头 portal 标记;桥面层逐上下文烘焙 = 区域定价 ∩ 净空腐蚀,
/// 桥面侧边封闭,只有桥头 portal 格与地面层相连。
/// 连通域合并:桥面 4 连通洪泛 + portal 格与地面格双通时合并两域。
/// 路径几何在米制 Fix64 下计算(厘米平方会溢出 Q31.32)。
/// </summary>
public static class UpperLayerBake
{
    /// <summary>桥实体栅格化:逐格桥面区域下标(0 = 无桥面)与桥头 portal 标记,及首写 tile 序。</summary>
    public static DeckSurface RasterizeDecks(
        IReadOnlyList<BridgeDeckRecord> bridges,
        CrowdSimulationRuntimeConfig config)
    {
        int n = config.NavCellCount;
        int cs = config.NavCellSizeCm;
        var deckAreaIndex = new byte[n * n];
        var portalFlag = new byte[n * n];
        var tileSeen = new bool[(n / config.Hpa.ClusterSize) * (n / config.Hpa.ClusterSize)];
        var tileOrder = new List<int>();
        int tileSize = config.Hpa.ClusterSize;
        var portalReach = Fix64.FromInt(config.Structures.PortalCells * cs / 100); // 格 → 米
        foreach (var bridge in bridges)
        {
            int areaIndex = AreaIndexOf(config, bridge.AreaId);
            var span = bridge.Span;
            // 路径包围盒(含宽度)扫格——厘米整数域,与 cellRectOf 同语义
            int halfWCm = span.WidthCm / 2;
            int minX = Math.Max(0, FloorDiv(Math.Min(span.X0Cm, span.X1Cm) - halfWCm, cs));
            int minY = Math.Max(0, FloorDiv(Math.Min(span.Y0Cm, span.Y1Cm) - halfWCm, cs));
            int maxX = Math.Min(n - 1, FloorDiv(Math.Max(span.X0Cm, span.X1Cm) + halfWCm, cs));
            int maxY = Math.Min(n - 1, FloorDiv(Math.Max(span.Y0Cm, span.Y1Cm) + halfWCm, cs));

            var x0 = Cm(span.X0Cm); var y0 = Cm(span.Y0Cm);
            var ax = Cm(span.X1Cm) - x0; var ay = Cm(span.Y1Cm) - y0;
            // 桥 / 路径跨度按生成器约定是轴对齐的:轴对齐时单位向量与长度都取精确值,
            // 不走 VectorLength(定点 Sqrt 在端点格上的误差会吃掉 EPS 容差)。
            // 斜线路径(运行时道路 / 泛洪,S7)届时再按参考实现复核精度口径。
            Fix64 len, ux, uy;
            if (ay == Fix64.Zero && ax != Fix64.Zero) { len = Fix64.Abs(ax); ux = ax > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue; uy = Fix64.Zero; }
            else if (ax == Fix64.Zero && ay != Fix64.Zero) { len = Fix64.Abs(ay); ux = Fix64.Zero; uy = ay > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue; }
            else
            {
                len = Fix64Math.VectorLength(ax, ay);
                ux = len > Fix64.Zero ? ax / len : Fix64.OneValue;
                uy = len > Fix64.Zero ? ay / len : Fix64.Zero;
            }
            var halfW = Fix64.FromInt(span.WidthCm) / 200;
            var eps = Fix64.FromDouble(1e-6);
            var csM = Fix64.FromInt(cs) / 100;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var px = (Fix64.FromInt(x) + Fix64.HalfValue) * csM;
                    var py = (Fix64.FromInt(y) + Fix64.HalfValue) * csM;
                    var dx = px - x0; var dy = py - y0;
                    var t = dx * ux + dy * uy;
                    // covers():点在带内 = 投影在段上且横距 < 半宽
                    if (t < -eps || t > len + eps) continue;
                    var cross = dx * uy - dy * ux;
                    if (Fix64.Abs(cross) >= halfW) continue;

                    int cell = y * n + x;
                    // portal:距任一端点的沿段距离 < portalCells 格
                    var endDist = t < len - t ? t : len - t;
                    deckAreaIndex[cell] = (byte)(areaIndex + 1); // 同格后放者胜( bridges 顺序即实体顺序)
                    if (endDist < portalReach) portalFlag[cell] = 1;
                    int tileId = (y / tileSize) * (n / tileSize) + x / tileSize;
                    if (!tileSeen[tileId]) { tileSeen[tileId] = true; tileOrder.Add(tileId); }
                }
            }
        }

        return new DeckSurface { AreaIndex = deckAreaIndex, PortalFlag = portalFlag, TileOrder = tileOrder.ToArray() };

        static Fix64 Cm(int cm) => Fix64.FromInt(cm) / 100;
        static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            return value < 0 && value % divisor != 0 ? q - 1 : q;
        }
        static int AreaIndexOf(CrowdSimulationRuntimeConfig config, string areaId)
        {
            for (int i = 0; i < config.NavAreas.Count; i++)
            {
                if (config.NavAreas[i].Id == areaId) return i;
            }

            throw new InvalidOperationException($"{CrowdSimulationConfigValidator.FileName}: 桥实体引用了未定义的导航区域 \"{areaId}\"。");
        }
    }

    /// <summary>
    /// 逐上下文桥面烘焙(bakeUpper 移植):桥面格 = 定价区域 ∩ 净空腐蚀;
    /// 桥头 portal 的腐蚀半径内,地面可走的岸边格并入桥面可行走面(桥头落在腐蚀后仍可走的地面)。
    /// </summary>
    public static void BakeDeck(
        CrowdSimulationRuntimeConfig config,
        int agentTypeIndex,
        int clearanceCells,
        byte[] groundWalk,
        byte[] deckAreaIndex,
        byte[] portalFlag,
        byte[] upPass,
        byte[] portalOut)
    {
        int n = config.NavCellCount, n2 = n * n;
        var row = config.AgentTypes[agentTypeIndex].CostByArea;

        // 桥面可走意图:有桥面且该类型给该区域定了价
        var win = new byte[n2];
        var portals = new List<int>();
        for (int i = 0; i < n2; i++)
        {
            int a = deckAreaIndex[i] - 1;
            if (a < 0) continue;
            if (row[a] > Fix64.Zero)
            {
                win[i] = 1;
                if (portalFlag[i] != 0) portals.Add(i);
            }
        }

        if (portals.Count == 0 && !Array.Exists(win, v => v != 0))
        {
            return;
        }

        // 岸边并入:portal 切比雪夫邻域内的地面可走格临时并入腐蚀输入
        int r = clearanceCells - 1;
        if (r > 0)
        {
            foreach (int p in portals)
            {
                int px = p % n, py = p / n;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int gx = px + dx, gy = py + dy;
                        if (gx < 0 || gy < 0 || gx >= n || gy >= n) continue;
                        if (groundWalk[gy * n + gx] != 0) win[gy * n + gx] = 1;
                    }
                }
            }
        }

        var eroded = new byte[n2];
        ErodeFull(win, n, clearanceCells, eroded);
        for (int i = 0; i < n2; i++)
        {
            int a = deckAreaIndex[i] - 1;
            bool ok = a >= 0 && row[a] > Fix64.Zero && eroded[i] != 0;
            upPass[i] = ok ? (byte)1 : (byte)0;
            portalOut[i] = ok && portalFlag[i] != 0 ? (byte)1 : (byte)0;
        }
    }

    /// <summary>
    /// 连通域合并:地面洪泛结果 + 桥面洪泛 + portal 合并。
    /// 返回合并后的地面格连通域栅格与总数(与 mergeTiles 同语义:桥面域附加在地面域之后)。
    /// </summary>
    public static int MergeDeckComponents(
        int[] groundComp,
        int groundCount,
        byte[] groundPassable,
        byte[] upPass,
        byte[] portal,
        int n,
        int[] scratchCompOut)
    {
        // 桥面 4 连通洪泛
        var deckComp = new int[n * n];
        Array.Fill(deckComp, -1);
        int deckCount = 0;
        var queue = new int[n * n];
        for (int s = 0; s < n * n; s++)
        {
            if (upPass[s] == 0 || deckComp[s] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = s;
            deckComp[s] = deckCount;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % n, y = c / n;
                if (x > 0 && upPass[c - 1] != 0 && deckComp[c - 1] < 0) { deckComp[c - 1] = deckCount; queue[tail++] = c - 1; }
                if (x < n - 1 && upPass[c + 1] != 0 && deckComp[c + 1] < 0) { deckComp[c + 1] = deckCount; queue[tail++] = c + 1; }
                if (y > 0 && upPass[c - n] != 0 && deckComp[c - n] < 0) { deckComp[c - n] = deckCount; queue[tail++] = c - n; }
                if (y < n - 1 && upPass[c + n] != 0 && deckComp[c + n] < 0) { deckComp[c + n] = deckCount; queue[tail++] = c + n; }
            }

            deckCount++;
        }

        if (deckCount == 0)
        {
            Array.Copy(groundComp, scratchCompOut, groundComp.Length);
            return groundCount;
        }

        // 并查集:0..groundCount-1 = 地面域,groundCount.. = 桥面域
        var parent = new int[groundCount + deckCount];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        // deck ↔ ground 只在桥头 portal 格合并
        for (int c = 0; c < n * n; c++)
        {
            if (portal[c] != 0 && groundPassable[c] != 0 && upPass[c] != 0)
            {
                Union(groundComp[c], groundCount + deckComp[c]);
            }
        }

        // 稠密化:按地面域号序、再桥面域号序(与 mergeTiles 的槽序同规则)
        var dense = new int[parent.Length];
        Array.Fill(dense, -1);
        int count = 0;
        for (int i = 0; i < parent.Length; i++)
        {
            int root = Find(i);
            if (dense[root] < 0) dense[root] = count++;
        }

        for (int c = 0; c < n * n; c++)
        {
            scratchCompOut[c] = groundComp[c] < 0 ? -1 : dense[Find(groundComp[c])];
        }

        return count;
    }

    private static void ErodeFull(byte[] walk, int n, int clearance, byte[] output)
    {
        // 与 NavContextBaker 同一腐蚀语义(整图)
        int r = clearance - 1;
        if (r <= 0)
        {
            Array.Copy(walk, output, walk.Length);
            return;
        }

        int need = 2 * r + 1;
        var row = new byte[n * n];
        for (int y = 0; y < n; y++)
        {
            int baseIdx = y * n, run = 0;
            for (int c = 0; c < n; c++)
            {
                run = walk[baseIdx + c] != 0 ? run + 1 : 0;
                int x = c - r;
                if (x >= 0) row[baseIdx + x] = run >= need ? (byte)1 : (byte)0;
            }
        }

        for (int x = 0; x < n; x++)
        {
            int run = 0;
            for (int yy = 0; yy < n; yy++)
            {
                run = row[yy * n + x] != 0 ? run + 1 : 0;
                int y = yy - r;
                if (y >= 0) output[y * n + x] = run >= need ? (byte)1 : (byte)0;
            }

            for (int y = Math.Max(0, n - r); y < n; y++) output[y * n + x] = 0;
        }
    }
}
