using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace CrowdSimulationTests.Uat;

/// <summary>迷雾类 UAT 的公用读数与命令：视野组、格状态、区域参考格集、面命令、放置和带乐观迷雾开关的下令。</summary>
internal static class CrowdFogUatTools
{
    public static int WorldCm(CrowdSimSession session) => session.Config.NavCellsPerSide * session.Config.NavCellSizeCm;

    public static (int X, int Y) Base(CrowdSimSession session, int player)
    {
        foreach (var b in session.Config.Deploy.Bases)
        {
            if (b.PlayerId == player) return (b.XCm, b.YCm);
        }

        throw new InvalidOperationException($"场景无效：deploy.bases 没有玩家 {player}。");
    }

    public static int Group(CrowdSimSession session, int player)
        => session.Fog!.GroupOf[session.Config.PairRules.IndexByPlayerId[player]];

    public static bool Visible(CrowdFog fog, int group, int cell)
        => fog.CellVisibilityOf(group, cell) == CellVisibility.Visible;

    public static bool Explored(CrowdFog fog, int group, int cell)
        => fog.CellVisibilityOf(group, cell) is CellVisibility.Explored or CellVisibility.Visible;

    public static int CellCount(CrowdFog fog) => fog.FogCellsPerSide * fog.FogCellsPerSide;

    /// <summary>矩形触到的每个迷雾格（双精度参考，向下取整、夹到图内）。</summary>
    public static int[] RectCells(CrowdFog fog, double x0, double y0, double x1, double y1)
    {
        int f = fog.FogCellsPerSide;
        double cs = fog.FogCellSizeCm;
        int C(double v) => Math.Max(0, Math.Min(f - 1, (int)Math.Floor(v / cs)));
        int ax = C(Math.Min(x0, x1)), ay = C(Math.Min(y0, y1)), bx = C(Math.Max(x0, x1)), by = C(Math.Max(y0, y1));
        var cells = new List<int>();
        for (int y = ay; y <= by; y++)
        {
            for (int x = ax; x <= bx; x++) cells.Add(y * f + x);
        }

        return cells.ToArray();
    }

    /// <summary>整块落在矩形迷雾格范围内的导航 tile。</summary>
    public static int[] TilesIn(CrowdFog fog, double x0, double y0, double x1, double y1)
    {
        int f = fog.FogCellsPerSide;
        double cs = fog.FogCellSizeCm;
        int C(double v) => Math.Max(0, Math.Min(f - 1, (int)Math.Floor(v / cs)));
        int cx0 = C(Math.Min(x0, x1)), cy0 = C(Math.Min(y0, y1)), cx1 = C(Math.Max(x0, x1)), cy1 = C(Math.Max(y0, y1));
        int k = fog.NavCellsPerFogCell, t = fog.NavCellsPerTile, n = fog.NavTilesPerSide;
        var tiles = new List<int>();
        for (int i = 0; i < n * n; i++)
        {
            int tx = i % n, ty = i / n;
            if (tx * t >= cx0 * k && (tx + 1) * t <= (cx1 + 1) * k && ty * t >= cy0 * k && (ty + 1) * t <= (cy1 + 1) * k) tiles.Add(i);
        }

        return tiles.ToArray();
    }

    /// <summary>格心满足 <paramref name="inside"/> 的迷雾格（双精度参考，厘米）。</summary>
    public static int[] CentreCells(CrowdFog fog, Func<double, double, bool> inside)
    {
        int f = fog.FogCellsPerSide;
        double cs = fog.FogCellSizeCm;
        var cells = new List<int>();
        for (int c = 0; c < f * f; c++)
        {
            if (inside((c % f + 0.5) * cs, (c / f + 0.5) * cs)) cells.Add(c);
        }

        return cells.ToArray();
    }

    /// <summary>偶奇规则的多边形内点判定（与沙盒参考同一写法）。</summary>
    public static Func<double, double, bool> PnPoly(double[][] pts) => (x, y) =>
    {
        bool k = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            double xi = pts[i][0], yi = pts[i][1], xj = pts[j][0], yj = pts[j][1];
            if ((yi > y) != (yj > y) && x < xi + (y - yi) * (xj - xi) / (yj - yi)) k = !k;
        }

        return k;
    };

    public static int[] AreaCells(CrowdFog fog, CrowdFogShape shape)
        => CrowdFogArea.AreaCells(fog.FogCellsPerSide, fog.FogCellSizeCm, shape).Cells;

    /// <summary>足迹包围盒触到的迷雾格，与迷雾索引同一取格法。</summary>
    public static int[] FootprintCells(CrowdSimSession session, int id)
    {
        var fog = session.Fog!;
        var (a, b, c, d) = session.Structures!.FootprintOf(id).Bbox();
        Fix64 cs = Fix64.FromInt(fog.FogCellSizeCm);
        int f = fog.FogCellsPerSide;
        int x0 = Math.Max(0, (int)(a / cs).ToLong()), y0 = Math.Max(0, (int)(b / cs).ToLong());
        int x1 = Math.Min(f - 1, (int)(c / cs).ToLong()), y1 = Math.Min(f - 1, (int)(d / cs).ToLong());
        var cells = new List<int>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++) cells.Add(y * f + x);
        }

        return cells.ToArray();
    }

    public static JsonArray Rect(double x0, double y0, double x1, double y1) => new() { x0, y0, x1, y1 };

    public static JsonArray Circle(double x, double y, double r) => new() { x, y, r };

    public static JsonArray Poly(params double[][] pts)
    {
        var a = new JsonArray();
        foreach (var p in pts) a.Add(new JsonArray { p[0], p[1] });
        return a;
    }

    /// <summary>reveal / obscure / forget / fogShare，走 JSON 边界，当前 tick 执行。</summary>
    public static object? Fog(CrowdSimSession session, JsonObject cmd)
        => session.Commands.Submit(session, cmd, CrowdSimCommands.Exec);

    public static void Reveal(CrowdSimSession session, int player, string shape, JsonArray value)
        => Fog(session, new JsonObject { ["type"] = "reveal", ["player"] = player, [shape] = value });

    public static void Forget(CrowdSimSession session, int player, string shape, JsonArray value)
        => Fog(session, new JsonObject { ["type"] = "forget", ["player"] = player, [shape] = value });

    public static void Obscure(CrowdSimSession session, int player, string shape, JsonArray value, int ticks)
        => Fog(session, new JsonObject { ["type"] = "obscure", ["player"] = player, [shape] = value, ["ticks"] = ticks });

    public static int FogShare(CrowdSimSession session, int player, int with)
        => (int)Fog(session, new JsonObject { ["type"] = "fogShare", ["player"] = player, ["with"] = with })!;

    /// <summary>放一个结构实体并返回新 id；被放置裁定拒绝时抛错。</summary>
    public static int Place(CrowdSimSession session, string template, int xCm, int yCm, int sizeCm)
    {
        var ids = session.Structures!.EntityIds;
        int before = ids.Count;
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.PlaceStructure,
            TemplateId = template,
            XCm = xCm,
            YCm = yCm,
            SizeCm = sizeCm,
        }, CrowdSimCommands.Exec);
        if (ids.Count == before)
            throw new InvalidOperationException($"场景无效：在 ({xCm}, {yCm}) 放 {template} 被拒（{session.LastPlacementReject}）。");
        return ids.Max();
    }

    /// <summary>玩家全体的 box 令，带乐观迷雾开关。当前 tick 执行。</summary>
    public static void Order(CrowdSimSession session, int player, int xCm, int yCm, bool fogTerrain)
    {
        var members = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(player, members);
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = player,
            XCm = xCm,
            YCm = yCm,
            ShapeId = "box",
            HasAuto = true,
            Auto = false,
            HasFogTerrain = true,
            FogTerrain = fogTerrain,
            Members = members.AsSpan(0, count).ToArray(),
        }, CrowdSimCommands.Exec);
    }

    public static bool SameSet(IEnumerable<int> a, IEnumerable<int> b)
        => a.Distinct().OrderBy(v => v).SequenceEqual(b.Distinct().OrderBy(v => v));
}
