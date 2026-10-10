using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Platform.Abstractions;

namespace CrowdSimulationS4DeployMod.Runtime;

/// <summary>
/// NavMesh + HPA 线框(S3 演示同款)。缓存键是(导航号, 结构重烘序, 变体 Version):
/// 原位揭示只递增 Version,不改 LastRebakeReport.ReportTick。
/// 线段与标记全部写入临时表之后才替换缓存;中途失败保留上一份完整键。
/// 认知变体未物化时本帧不提交、不写键,下帧重试。
/// </summary>
internal sealed class S4DeployNavWireframe
{
    private List<(Vector2[] Pts, float Thick, Vector4 Color)> _lines = new();
    private List<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)> _markers = new();

    public bool HasCache { get; private set; }
    public int CachedNavId { get; private set; }
    public int CachedReportTick { get; private set; }
    public int CachedVersion { get; private set; }
    public IReadOnlyList<(Vector2[] Pts, float Thick, Vector4 Color)> Lines => _lines;
    public IReadOnlyList<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)> Markers => _markers;

    /// <summary>返回本帧是否提交线框。pending = 变体未物化。rebuilt = 本次重算了线段。</summary>
    public bool TryProject(CrowdSimSession session, out bool pending, out bool rebuilt)
    {
        pending = false;
        rebuilt = false;
        var group = S4DeployDemoViewProjector.DominantGroup(session);
        int navId = group?.NavId ?? MinNavId(session);
        if (navId < 0)
            throw new InvalidOperationException("S4 NavMesh 视图没有导航上下文。");
        if (!session.TryGetMaterializedNav(navId, out var nav))
        {
            pending = true;
            return false;
        }

        int reportTick = session.LastRebakeReport?.ReportTick ?? -1;
        if (HasCache && CachedNavId == navId && CachedReportTick == reportTick && CachedVersion == nav.Version)
            return true;

        var lines = new List<(Vector2[] Pts, float Thick, Vector4 Color)>();
        var markers = new List<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)>();
        Build(session, nav, lines, markers);
        _lines = lines;
        _markers = markers;
        CachedNavId = navId;
        CachedReportTick = reportTick;
        CachedVersion = nav.Version;
        HasCache = true;
        rebuilt = true;
        return true;
    }

    private static int MinNavId(CrowdSimSession session)
    {
        int best = -1;
        foreach (var id in session.Navs.Keys) best = best < 0 || id < best ? id : best;
        return best;
    }

    private static void Build(
        CrowdSimSession session,
        NavContext nav,
        List<(Vector2[] Pts, float Thick, Vector4 Color)> lines,
        List<(Vector2 Pos, RouteVisualMarkerShape Shape, float Radius, Vector4 Color)> markers)
    {
        int n = session.Config.NavCellCount;
        float csM = session.Config.NavCellSizeCm / 100f;
        int t = session.Config.Hpa.ClusterSize, c = n / t;
        var navmeshColor = new Vector4(0.9f, 0.9f, 0.9f, 0.55f);
        var deckColor = new Vector4(1f, 0.84f, 0.31f, 0.85f);
        for (int ty = 0; ty < c; ty++)
        {
            for (int tx = 0; tx < c; tx++)
            {
                int tileId = ty * c + tx;
                AddEntryWire(lines, nav.Tiles?[tileId], tx * t, ty * t, csM, navmeshColor);
                if (nav.UpperTiles != null && nav.UpperTiles.TryGetValue(tileId, out var up))
                    AddEntryWire(lines, up.Entry, tx * t, ty * t, csM, deckColor);
            }
        }

        var gridColor = new Vector4(1f, 1f, 1f, 0.22f);
        for (int b = 0; b <= c; b++)
        {
            lines.Add((new[] { new Vector2(b * t * csM, 0f), new Vector2(b * t * csM, n * csM) }, csM * 0.1f, gridColor));
            lines.Add((new[] { new Vector2(0f, b * t * csM), new Vector2(n * csM, b * t * csM) }, csM * 0.1f, gridColor));
        }

        if (nav.Hpa != null)
        {
            int n2 = n * n;
            var entranceColor = new Vector4(0.25f, 0.77f, 1f, 0.9f);
            foreach (var block in nav.Hpa.Blocks)
            {
                foreach (int cell in block.Cells)
                {
                    bool deck = cell >= n2;
                    int cc = deck ? cell - n2 : cell;
                    markers.Add((new Vector2((cc % n + 0.5f) * csM, (cc / n + 0.5f) * csM), RouteVisualMarkerShape.Ring, csM * 0.4f, deck ? deckColor : entranceColor));
                }
            }
        }

        if (nav.Links != null)
        {
            for (int e = 0; e < nav.Links.Count; e++)
            {
                int a = nav.Links.From[e], b2 = nav.Links.To[e];
                var col = nav.Links.TwoWay[e] != 0 ? new Vector4(0.36f, 0.42f, 0.75f, 0.9f) : new Vector4(1f, 0.7f, 0f, 0.9f);
                lines.Add((new[]
                {
                    new Vector2((a % n + 0.5f) * csM, (a / n + 0.5f) * csM),
                    new Vector2((b2 % n + 0.5f) * csM, (b2 / n + 0.5f) * csM),
                }, csM * 0.2f, col));
            }
        }
    }

    private static void AddEntryWire(
        List<(Vector2[] Pts, float Thick, Vector4 Color)> lines,
        NavTileEntry? e, int ox, int oy, float csM, Vector4 color)
    {
        if (e == null) return;
        for (int p = 0; p < e.Count; p++)
        {
            int s = e.PolyStart[p], e2 = e.PolyStart[p + 1], count = e2 - s;
            var pts = new Vector2[count + 1];
            for (int k = 0; k < count; k++)
            {
                int v = e.PolyVerts[s + k];
                pts[k] = new Vector2((ox + e.Vx[v]) * csM, (oy + e.Vy[v]) * csM);
            }

            pts[count] = pts[0];
            lines.Add((pts, csM * 0.12f, color));
        }
    }
}
