using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav.GridBake;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>桥面层地表(每地图一份,与上下文无关):桥面格区域、桥头标记、首写 tile 序。</summary>
public sealed class DeckSurface
{
    public required byte[] AreaIndex { get; init; }
    public required byte[] PortalFlag { get; init; }
    /// <summary>含桥面格的 tile 编号,按首次写入序。</summary>
    public required int[] TileOrder { get; init; }

    public static DeckSurface Empty(int cellCount) => new()
    {
        AreaIndex = new byte[cellCount * cellCount],
        PortalFlag = new byte[cellCount * cellCount],
        TileOrder = Array.Empty<int>(),
    };
}

/// <summary>
/// 桥面层烘焙：桥实体（路径足迹）栅格化为
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
        int navCellsPerSide = config.NavCellsPerSide;
        int navCellSizeCm = config.NavCellSizeCm;
        var deckAreaIndex = new byte[navCellsPerSide * navCellsPerSide];
        var portalFlag = new byte[navCellsPerSide * navCellsPerSide];
        var tileSeen = new bool[(navCellsPerSide / config.Hpa.NavCellsPerTile) * (navCellsPerSide / config.Hpa.NavCellsPerTile)];
        var tileOrder = new List<int>();
        int tileSize = config.Hpa.NavCellsPerTile;
        var portalReachM = CrowdReferenceMeters.TruncatedCmToM(config.Structures.PortalReachNavCells * navCellSizeCm);
        foreach (var bridge in bridges)
        {
            int areaIndex = AreaIndexOf(config, bridge.AreaId);
            var span = bridge.Span;
            // 路径包围盒(含宽度)扫格,厘米整数域
            int halfWCm = span.WidthCm / 2;
            int minX = Math.Max(0, FloorDiv(Math.Min(span.X0Cm, span.X1Cm) - halfWCm, navCellSizeCm));
            int minY = Math.Max(0, FloorDiv(Math.Min(span.Y0Cm, span.Y1Cm) - halfWCm, navCellSizeCm));
            int maxX = Math.Min(navCellsPerSide - 1, FloorDiv(Math.Max(span.X0Cm, span.X1Cm) + halfWCm, navCellSizeCm));
            int maxY = Math.Min(navCellsPerSide - 1, FloorDiv(Math.Max(span.Y0Cm, span.Y1Cm) + halfWCm, navCellSizeCm));

            var x0 = CrowdReferenceMeters.CmToMRaw(span.X0Cm); var y0 = CrowdReferenceMeters.CmToMRaw(span.Y0Cm);
            var ax = CrowdReferenceMeters.CmToMRaw(span.X1Cm) - x0; var ay = CrowdReferenceMeters.CmToMRaw(span.Y1Cm) - y0;
            // 本方法只栅格化桥。非轴对齐跨度走 VectorLength。轴对齐时故意不用 sqrt，避免端点格吃掉 1e-6 容差。
            Fix64 len, ux, uy;
            if (ay == Fix64.Zero && ax != Fix64.Zero) { len = Fix64.Abs(ax); ux = ax > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue; uy = Fix64.Zero; }
            else if (ax == Fix64.Zero && ay != Fix64.Zero) { len = Fix64.Abs(ay); ux = Fix64.Zero; uy = ay > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue; }
            else
            {
                len = Fix64Math.VectorLength(ax, ay);
                ux = len > Fix64.Zero ? ax / len : Fix64.OneValue;
                uy = len > Fix64.Zero ? ay / len : Fix64.Zero;
            }
            var halfW = CrowdReferenceMeters.HalfWidthM(span.WidthCm);
            var eps = Fix64.FromDouble(1e-6);
            var navCellSizeM = CrowdReferenceMeters.CmToMRaw(navCellSizeCm);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var px = (Fix64.FromInt(x) + Fix64.HalfValue) * navCellSizeM;
                    var py = (Fix64.FromInt(y) + Fix64.HalfValue) * navCellSizeM;
                    var dx = px - x0; var dy = py - y0;
                    var t = dx * ux + dy * uy;
                    // 沿段参数 t 允许超出端点 1e-6 米；横距严格小于半宽。
                    if (t < -eps || t > len + eps) continue;
                    var cross = dx * uy - dy * ux;
                    if (Fix64.Abs(cross) >= halfW) continue;

                    int cell = y * navCellsPerSide + x;
                    // 桥头:沿段到较近端点的距离小于 PortalReachNavCells 格换算的米长
                    var endDist = t < len - t ? t : len - t;
                    deckAreaIndex[cell] = (byte)(areaIndex + 1); // 同格后放者胜( bridges 顺序即实体顺序)
                    if (endDist < portalReachM) portalFlag[cell] = 1;
                    int tileId = (y / tileSize) * (navCellsPerSide / tileSize) + x / tileSize;
                    if (!tileSeen[tileId]) { tileSeen[tileId] = true; tileOrder.Add(tileId); }
                }
            }
        }

        return new DeckSurface { AreaIndex = deckAreaIndex, PortalFlag = portalFlag, TileOrder = tileOrder.ToArray() };

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
    /// 逐上下文桥面烘焙:桥面格 = 定价区域 ∩ 净空腐蚀。
    /// 岸边格只临时并入腐蚀输入，让桥头在腐蚀后仍能留下；upPass 仍要求该格自己有定价桥面区域。
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
        int navCellsPerSide = config.NavCellsPerSide, n2 = navCellsPerSide * navCellsPerSide;
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
                int px = p % navCellsPerSide, py = p / navCellsPerSide;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int gx = px + dx, gy = py + dy;
                        if (gx < 0 || gy < 0 || gx >= navCellsPerSide || gy >= navCellsPerSide) continue;
                        if (groundWalk[gy * navCellsPerSide + gx] != 0) win[gy * navCellsPerSide + gx] = 1;
                    }
                }
            }
        }

        var eroded = new byte[n2];
        ClearanceErosion.Erode(win, navCellsPerSide, clearanceCells, eroded);
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
    /// 返回合并后的地面格连通域栅格与总数(桥面域附加在地面域之后)。
    /// </summary>
    public static int MergeDeckComponents(
        int[] groundComp,
        int groundCount,
        byte[] groundPassable,
        byte[] upPass,
        byte[] portal,
        int navCellsPerSide,
        int[] scratchCompOut,
        int[]? deckComp = null,
        int[]? queue = null,
        int[]? parent = null,
        int[]? dense = null)
    {
        int n2 = navCellsPerSide * navCellsPerSide;
        // 桥面 4 连通洪泛。缓冲由增量重烘传入;全量烘焙路径仍在这里分配。
        if (deckComp == null || deckComp.Length < n2) deckComp = new int[n2];
        Array.Fill(deckComp, -1);
        int deckCount = 0;
        if (queue == null || queue.Length < n2) queue = new int[n2];
        for (int s = 0; s < n2; s++)
        {
            if (upPass[s] == 0 || deckComp[s] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = s;
            deckComp[s] = deckCount;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % navCellsPerSide, y = c / navCellsPerSide;
                if (x > 0 && upPass[c - 1] != 0 && deckComp[c - 1] < 0) { deckComp[c - 1] = deckCount; queue[tail++] = c - 1; }
                if (x < navCellsPerSide - 1 && upPass[c + 1] != 0 && deckComp[c + 1] < 0) { deckComp[c + 1] = deckCount; queue[tail++] = c + 1; }
                if (y > 0 && upPass[c - navCellsPerSide] != 0 && deckComp[c - navCellsPerSide] < 0) { deckComp[c - navCellsPerSide] = deckCount; queue[tail++] = c - navCellsPerSide; }
                if (y < navCellsPerSide - 1 && upPass[c + navCellsPerSide] != 0 && deckComp[c + navCellsPerSide] < 0) { deckComp[c + navCellsPerSide] = deckCount; queue[tail++] = c + navCellsPerSide; }
            }

            deckCount++;
        }

        if (deckCount == 0)
        {
            Array.Copy(groundComp, scratchCompOut, groundComp.Length);
            return groundCount;
        }

        // 并查集:0..groundCount-1 = 地面域,groundCount.. = 桥面域
        int unionCount = groundCount + deckCount;
        if (parent == null || parent.Length < unionCount) parent = new int[unionCount];
        for (int i = 0; i < unionCount; i++) parent[i] = i;
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
        for (int c = 0; c < n2; c++)
        {
            if (portal[c] != 0 && groundPassable[c] != 0 && upPass[c] != 0)
            {
                Union(groundComp[c], groundCount + deckComp[c]);
            }
        }

        // 稠密化:按地面域号序、再桥面域号序
        if (dense == null || dense.Length < unionCount) dense = new int[unionCount];
        Array.Fill(dense, -1, 0, unionCount);
        int count = 0;
        for (int i = 0; i < unionCount; i++)
        {
            int root = Find(i);
            if (dense[root] < 0) dense[root] = count++;
        }

        for (int c = 0; c < n2; c++)
        {
            scratchCompOut[c] = groundComp[c] < 0 ? -1 : dense[Find(groundComp[c])];
        }

        return count;
    }
}
