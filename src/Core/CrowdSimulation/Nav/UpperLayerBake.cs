using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Nav.GridBake;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>桥面层地表(每地图一份,与上下文无关):桥面格区域、桥头标记、首写 tile 序。</summary>
public sealed class UpperLayerSurface
{
    public required byte[] AreaIndex { get; init; }
    public required byte[] PortalFlag { get; init; }
    /// <summary>含桥面格的 tile 编号。全量栅格按首次写入序；增量重画只保证集合覆盖每个仍有桥面的 tile。</summary>
    public required int[] TileOrder { get; set; }

    public static UpperLayerSurface Empty(int cellsPerSide) => new()
    {
        AreaIndex = new byte[cellsPerSide * cellsPerSide],
        PortalFlag = new byte[cellsPerSide * cellsPerSide],
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
    public static UpperLayerSurface RasterizeLayeredPaths(
        IReadOnlyList<LayeredPathRecord> bridges,
        CrowdSimulationRuntimeConfig config)
    {
        int navCellsPerSide = config.NavCellsPerSide;
        int navCellSizeCm = config.NavCellSizeCm;
        var upperAreaIndex = new byte[navCellsPerSide * navCellsPerSide];
        var portalFlag = new byte[navCellsPerSide * navCellsPerSide];
        var tileSeen = new bool[(navCellsPerSide / config.Hpa.NavCellsPerTile) * (navCellsPerSide / config.Hpa.NavCellsPerTile)];
        var tileOrder = new List<int>();
        int tileSize = config.Hpa.NavCellsPerTile;
        var portalReachM = CrowdReferenceMeters.TruncatedCmToM(config.Structures.PortalReachNavCells * navCellSizeCm);
        foreach (var bridge in bridges)
        {
            int areaIndex = AreaIndexOf(config, bridge.AreaId);
            var span = bridge.Span;
            var spanGeom = UpperLayerSpanGeometry.FromCentimeters(
                span.X0Cm, span.Y0Cm, span.X1Cm, span.Y1Cm, span.WidthCm,
                navCellsPerSide, navCellSizeCm, portalReachM);
            for (int y = spanGeom.MinY; y <= spanGeom.MaxY; y++)
            {
                for (int x = spanGeom.MinX; x <= spanGeom.MaxX; x++)
                {
                    if (!spanGeom.Hit(x, y, out Fix64 endDist)) continue;

                    int cell = y * navCellsPerSide + x;
                    // 同格后放者胜（bridges 顺序即实体顺序）。portal 只置位，后写的非桥头不把它清掉。
                    upperAreaIndex[cell] = (byte)(areaIndex + 1);
                    if (spanGeom.IsPortal(endDist)) portalFlag[cell] = 1;
                    int tileId = (y / tileSize) * (navCellsPerSide / tileSize) + x / tileSize;
                    if (!tileSeen[tileId]) { tileSeen[tileId] = true; tileOrder.Add(tileId); }
                }
            }
        }

        return new UpperLayerSurface { AreaIndex = upperAreaIndex, PortalFlag = portalFlag, TileOrder = tileOrder.ToArray() };

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
    public static void BakeUpperLayer(
        CrowdSimulationRuntimeConfig config,
        int agentTypeIndex,
        int clearanceCells,
        byte[] baseWalk,
        byte[] upperAreaIndex,
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
            int a = upperAreaIndex[i] - 1;
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
                        if (baseWalk[gy * navCellsPerSide + gx] != 0) win[gy * navCellsPerSide + gx] = 1;
                    }
                }
            }
        }

        var eroded = new byte[n2];
        ClearanceErosion.Erode(win, navCellsPerSide, clearanceCells, eroded);
        for (int i = 0; i < n2; i++)
        {
            int a = upperAreaIndex[i] - 1;
            bool ok = a >= 0 && row[a] > Fix64.Zero && eroded[i] != 0;
            upPass[i] = ok ? (byte)1 : (byte)0;
            portalOut[i] = ok && portalFlag[i] != 0 ? (byte)1 : (byte)0;
        }
    }

    /// <summary>
    /// 连通域合并:地面洪泛结果 + 桥面 4 连通洪泛 + portal 合并。
    /// 返回合并后的地面格连通域栅格与总数。
    /// upperComp 留下桥面局部号（0 起，格号升序首见；-1 = 无桥面）。UpComp 把这份局部号接在地面编号之后，调用方要留住这块缓冲。
    /// </summary>
    public static int MergeUpperLayerComponents(
        int[] baseComp,
        int baseCount,
        byte[] basePassable,
        byte[] upPass,
        byte[] portal,
        int navCellsPerSide,
        int[] scratchCompOut,
        int[]? upperComp = null,
        int[]? queue = null,
        int[]? parent = null,
        int[]? dense = null)
    {
        int n2 = navCellsPerSide * navCellsPerSide;
        // 桥面 4 连通洪泛。缓冲由增量重烘传入;全量烘焙路径仍在这里分配。
        if (upperComp == null || upperComp.Length < n2) upperComp = new int[n2];
        Array.Fill(upperComp, -1);
        int upperCount = 0;
        if (queue == null || queue.Length < n2) queue = new int[n2];
        for (int s = 0; s < n2; s++)
        {
            if (upPass[s] == 0 || upperComp[s] >= 0) continue;
            int head = 0, tail = 0;
            queue[tail++] = s;
            upperComp[s] = upperCount;
            while (head < tail)
            {
                int c = queue[head++];
                int x = c % navCellsPerSide, y = c / navCellsPerSide;
                if (x > 0 && upPass[c - 1] != 0 && upperComp[c - 1] < 0) { upperComp[c - 1] = upperCount; queue[tail++] = c - 1; }
                if (x < navCellsPerSide - 1 && upPass[c + 1] != 0 && upperComp[c + 1] < 0) { upperComp[c + 1] = upperCount; queue[tail++] = c + 1; }
                if (y > 0 && upPass[c - navCellsPerSide] != 0 && upperComp[c - navCellsPerSide] < 0) { upperComp[c - navCellsPerSide] = upperCount; queue[tail++] = c - navCellsPerSide; }
                if (y < navCellsPerSide - 1 && upPass[c + navCellsPerSide] != 0 && upperComp[c + navCellsPerSide] < 0) { upperComp[c + navCellsPerSide] = upperCount; queue[tail++] = c + navCellsPerSide; }
            }

            upperCount++;
        }

        if (upperCount == 0)
        {
            Array.Copy(baseComp, scratchCompOut, baseComp.Length);
            return baseCount;
        }

        // 并查集:0..baseCount-1 = 地面域,baseCount.. = 桥面域
        int unionCount = baseCount + upperCount;
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

        // upperLayer ↔ baseLayer 只在桥头 portal 格合并
        for (int c = 0; c < n2; c++)
        {
            if (portal[c] != 0 && basePassable[c] != 0 && upPass[c] != 0)
            {
                Union(baseComp[c], baseCount + upperComp[c]);
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
            scratchCompOut[c] = baseComp[c] < 0 ? -1 : dense[Find(baseComp[c])];
        }

        return count;
    }
}

/// <summary>
/// 桥 span 的米制栅格。轴对齐跨度不用 sqrt，避免端点格吃掉 1e-6 米容差。
/// 全量烘焙和放置判定共用这一份，端点格用朝负无穷取整。
/// </summary>
internal readonly struct UpperLayerSpanGeometry
{
    public int MinX { get; init; }
    public int MinY { get; init; }
    public int MaxX { get; init; }
    public int MaxY { get; init; }
    public Fix64 OriginX { get; init; }
    public Fix64 OriginY { get; init; }
    public Fix64 Length { get; init; }
    public Fix64 Ux { get; init; }
    public Fix64 Uy { get; init; }
    public Fix64 HalfWidth { get; init; }
    public Fix64 Epsilon { get; init; }
    public Fix64 CellSizeM { get; init; }
    public Fix64 PortalReachM { get; init; }
    public int EndX0 { get; init; }
    public int EndY0 { get; init; }
    public int EndX1 { get; init; }
    public int EndY1 { get; init; }

    public static UpperLayerSpanGeometry FromCentimeters(
        int x0Cm, int y0Cm, int x1Cm, int y1Cm, int widthCm,
        int navCellsPerSide, int navCellSizeCm, Fix64 portalReachM)
    {
        int halfWCm = widthCm / 2;
        var originX = CrowdReferenceMeters.CmToMRaw(x0Cm);
        var originY = CrowdReferenceMeters.CmToMRaw(y0Cm);
        var ax = CrowdReferenceMeters.CmToMRaw(x1Cm) - originX;
        var ay = CrowdReferenceMeters.CmToMRaw(y1Cm) - originY;
        Fix64 length, ux, uy;
        if (ay == Fix64.Zero && ax != Fix64.Zero)
        {
            length = Fix64.Abs(ax);
            ux = ax > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            uy = Fix64.Zero;
        }
        else if (ax == Fix64.Zero && ay != Fix64.Zero)
        {
            length = Fix64.Abs(ay);
            ux = Fix64.Zero;
            uy = ay > Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
        }
        else
        {
            length = Fix64Math.VectorLength(ax, ay);
            ux = length > Fix64.Zero ? ax / length : Fix64.OneValue;
            uy = length > Fix64.Zero ? ay / length : Fix64.Zero;
        }

        return new UpperLayerSpanGeometry
        {
            MinX = Math.Max(0, FloorDiv(Math.Min(x0Cm, x1Cm) - halfWCm, navCellSizeCm)),
            MinY = Math.Max(0, FloorDiv(Math.Min(y0Cm, y1Cm) - halfWCm, navCellSizeCm)),
            MaxX = Math.Min(navCellsPerSide - 1, FloorDiv(Math.Max(x0Cm, x1Cm) + halfWCm, navCellSizeCm)),
            MaxY = Math.Min(navCellsPerSide - 1, FloorDiv(Math.Max(y0Cm, y1Cm) + halfWCm, navCellSizeCm)),
            OriginX = originX,
            OriginY = originY,
            Length = length,
            Ux = ux,
            Uy = uy,
            HalfWidth = CrowdReferenceMeters.HalfWidthM(widthCm),
            Epsilon = Fix64.FromDouble(1e-6),
            CellSizeM = CrowdReferenceMeters.CmToMRaw(navCellSizeCm),
            PortalReachM = portalReachM,
            EndX0 = FloorDiv(x0Cm, navCellSizeCm),
            EndY0 = FloorDiv(y0Cm, navCellSizeCm),
            EndX1 = FloorDiv(x1Cm, navCellSizeCm),
            EndY1 = FloorDiv(y1Cm, navCellSizeCm),
        };
    }

    public bool Hit(int x, int y, out Fix64 endDist)
    {
        var px = (Fix64.FromInt(x) + Fix64.HalfValue) * CellSizeM;
        var py = (Fix64.FromInt(y) + Fix64.HalfValue) * CellSizeM;
        var dx = px - OriginX;
        var dy = py - OriginY;
        var t = dx * Ux + dy * Uy;
        if (t < -Epsilon || t > Length + Epsilon)
        {
            endDist = Fix64.Zero;
            return false;
        }

        var cross = dx * Uy - dy * Ux;
        if (Fix64.Abs(cross) >= HalfWidth)
        {
            endDist = Fix64.Zero;
            return false;
        }

        endDist = t < Length - t ? t : Length - t;
        return true;
    }

    public bool IsPortal(Fix64 endDist) => endDist < PortalReachM;

    public bool IsAnchorCell(int x, int y) =>
        (x == EndX0 && y == EndY0) || (x == EndX1 && y == EndY1);

    internal static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return value < 0 && value % divisor != 0 ? q - 1 : q;
    }
}
