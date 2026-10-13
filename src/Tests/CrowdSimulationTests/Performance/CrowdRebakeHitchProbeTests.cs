using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// S4 种子 1337 上拆开一次建筑重烘：增量各段、全量校验、以及 12000 单位行军后的反应。
/// 数字写到 /tmp/rebake-hitch-probe.txt。显式测试，不进默认全套。
/// </summary>
[TestFixture]
[Explicit]
public sealed class CrowdRebakeHitchProbeTests
{
    private const int XCm = 503125;
    private const int YCm = 565625;
    private const int SizeCm = 14000;

    [Test]
    public void Probe_S4Building_RebakePhases()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var log = new StringBuilder();
        void Line(string text)
        {
            log.AppendLine(text);
            TestContext.Out.WriteLine(text);
            Console.WriteLine(text);
            File.WriteAllText("/tmp/rebake-hitch-probe.txt", log.ToString());
        }

        Line($"navs={session.Navs.Count} slices={session.RebakeSliceCount} cells={session.Config.NavCellsPerSide} beliefs={(session.Beliefs == null ? 0 : 1)} variants={(session.Beliefs?.VariantCount() ?? 0)}");
        foreach (var kv in session.Navs)
        {
            var nav = kv.Value;
            Line($"nav id={nav.Id} layer={nav.LayerIndex} clearance={nav.ClearanceNavCells} links={nav.Links?.Count ?? 0}");
        }

        session.VerifyIncrementalNav = false;
        var warmupPlace = Place(session);
        var store = session.Structures!;
        int lastId = store.EntityIds.Count == 0 ? -1 : store.EntityIds[^1];
        string fpText = lastId < 0
            ? "none"
            : Describe(store.FootprintOf(lastId));
        Line($"warmup-place directNull={warmupPlace == null} ids={store.EntityIds.Count} at={store.EntityAt(Fix64.FromInt(XCm), Fix64.FromInt(YCm))} last={lastId} {fpText} job={session.RebakeJob != null}");
        var warmupReport = Drain(session, warmupPlace);
        Line($"warmup-drained ids={store.EntityIds.Count} at={store.EntityAt(Fix64.FromInt(XCm), Fix64.FromInt(YCm))} tiles={warmupReport.Tiles} contexts={warmupReport.Contexts} costOnly={warmupReport.CostOnly} hits={warmupReport.Hits} misses={warmupReport.Misses} elapsed={Ms(warmupReport.ElapsedTicks)}");
        Drain(session, Remove(session));
        for (int i = 0; i < 3; i++)
        {
            Measure(session, "off", i, "place", Line);
            Measure(session, "off", i, "remove", Line);
        }

        session.VerifyIncrementalNav = true;
        Measure(session, "on", 0, "place", Line);
        Measure(session, "on", 0, "remove", Line);
        Measure(session, "on", 1, "place", Line);
        Measure(session, "on", 1, "remove", Line);
        session.VerifyIncrementalNav = false;

        Line("units-begin");
        int tick = session.TickCount;
        var script = CrowdReferenceScript.Load(new JsonArray
        {
            Row(tick, new JsonObject { ["type"] = "spawn", ["count"] = 12000 }),
            Row(tick + 2, new JsonObject
            {
                ["type"] = "order",
                ["player"] = 1,
                ["xCm"] = 1200000,
                ["yCm"] = 800000,
                ["shape"] = "box",
            }),
        }, session.Units.Capacity);
        var march = Stopwatch.StartNew();
        script.Advance(session, 40);
        march.Stop();
        int flowing = 0;
        int groups = 0;
        foreach (var g in session.Groups.Groups)
        {
            if (g == null || g.Count == 0) continue;
            groups++;
            if (g.Flow != null) flowing++;
        }

        Line($"marched units={session.Units.Count} tick={session.TickCount} groups={groups} flowing={flowing} variants={session.Beliefs?.VariantCount() ?? 0} marchMs={march.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        session.VerifyIncrementalNav = true;
        var placed = Measure(session, "units-on", 0, "place", Line);
        CountRouteVsMask(session, placed, Line);
        Measure(session, "units-on", 0, "remove", Line);
        session.VerifyIncrementalNav = false;
        var placedOff = Measure(session, "units-off", 1, "place", Line);
        CountRouteVsMask(session, placedOff, Line);
        Measure(session, "units-off", 1, "remove", Line);
        Line("done");
    }

    private static CrowdRebakeReport Measure(CrowdSimSession session, string tag, int iter, string op, Action<string> line)
    {
        CrowdRebakeProbe.Reset();
        CrowdRebakeProbe.Armed = true;
        int variants = session.Beliefs?.VariantCount() ?? 0;
        int submits = session.Planner?.FlowRefreshSubmits ?? 0;
        _ = GC.GetAllocatedBytesForCurrentThread();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        long t0 = Stopwatch.GetTimestamp();
        CrowdRebakeReport? direct = op == "place" ? Place(session) : Remove(session);
        var report = direct ?? Drain(session, direct);
        long wall = Stopwatch.GetTimestamp() - t0;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        CrowdRebakeProbe.Armed = false;
        int variantDelta = (session.Beliefs?.VariantCount() ?? 0) - variants;
        int submitDelta = (session.Planner?.FlowRefreshSubmits ?? 0) - submits;
        line(
            $"OP tag={tag} iter={iter} op={op} wall={Ms(wall)} report={Ms(report.ElapsedTicks)} alloc={alloc} " +
            $"tiles={report.Tiles} contexts={report.Contexts} costOnly={report.CostOnly} hits={report.Hits} misses={report.Misses} " +
            $"orders={report.Orders} refreshes={report.Refreshes} evicted={report.Evicted} stuck={report.Stuck} " +
            $"variantDelta={variantDelta} submitDelta={submitDelta}");
        line(
            $"PHASE classify={Ms(CrowdRebakeProbe.Classify)} erode={Ms(CrowdRebakeProbe.Erode)} bakeTiles={Ms(CrowdRebakeProbe.BakeTiles)} " +
            $"cacheScan={Ms(CrowdRebakeProbe.CacheScan)} cacheBake={Ms(CrowdRebakeProbe.CacheBake)} cacheHits={CrowdRebakeProbe.CacheHits} cacheMisses={CrowdRebakeProbe.CacheMisses} " +
            $"components={Ms(CrowdRebakeProbe.Components)} cells={Ms(CrowdRebakeProbe.Cells)} links={Ms(CrowdRebakeProbe.Links)} reach={Ms(CrowdRebakeProbe.Reach)} " +
            $"hpa={Ms(CrowdRebakeProbe.Hpa)} hpaBake={Ms(CrowdRebakeProbe.HpaBake)} hpaBlock={Ms(CrowdRebakeProbe.HpaBlock)} " +
            $"hpaBakeTiles={CrowdRebakeProbe.HpaBakeTiles} hpaBlocks={CrowdRebakeProbe.HpaBlocks} " +
            $"incremental={Ms(CrowdRebakeProbe.Incremental)} verify={Ms(CrowdRebakeProbe.Verify)} reaction={Ms(CrowdRebakeProbe.Reaction)} publish={Ms(CrowdRebakeProbe.Publish)} " +
            $"navCalls={CrowdRebakeProbe.NavCalls} passable={CrowdRebakeProbe.Passable} cost={CrowdRebakeProbe.Cost} none={CrowdRebakeProbe.None}");
        for (int i = 0; i < CrowdRebakeProbe.NoteCount; i++)
        {
            var note = CrowdRebakeProbe.Notes[i];
            line($"NAV id={note.Id} kind={note.Kind} tiles={note.Tiles} ms={Ms(note.Ticks)}");
        }

        return report;
    }

    private static void CountRouteVsMask(CrowdSimSession session, CrowdRebakeReport report, Action<string> line)
    {
        int mask = 0;
        int withFlow = 0;
        foreach (var g in session.Groups.Groups)
        {
            if (g == null || g.Flow?.Mask == null || g.Count == 0) continue;
            withFlow++;
            foreach (var ctx in report.ContextResults)
            {
                if (ctx.NavId != g.NavId || ctx.DirtyTiles.Count == 0) continue;
                bool hit = false;
                for (int i = 0; i < ctx.DirtyTiles.Count; i++)
                {
                    int tile = ctx.DirtyTiles[i];
                    if ((uint)tile < (uint)g.Flow.Mask.Length && g.Flow.Mask[tile] != 0)
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit) mask++;
                break;
            }
        }

        line($"ROUTE refreshes={report.Refreshes} orders={report.Orders} maskTouchGroups={mask} flowing={withFlow}");
    }

    private static CrowdRebakeReport Drain(CrowdSimSession session, CrowdRebakeReport? direct)
    {
        if (direct != null) return direct;
        long rebakes = session.Telemetry.Rebakes;
        int guard = 0;
        while (session.Telemetry.Rebakes == rebakes)
        {
            if (guard++ > 64) throw new InvalidOperationException("重烘报告没有发布。");
            int spins = 0;
            while (!session.Step(out _))
            {
                if (spins++ > 20000) throw new InvalidOperationException($"tick {session.TickCount} 停摆未恢复。");
            }
        }

        return session.LastRebakeReport ?? throw new InvalidOperationException("重烘报告为空。");
    }

    private static CrowdRebakeReport? Place(CrowdSimSession session) =>
        CrowdStructureOps.PlaceStructure(
            session, "building", Fix64.FromInt(XCm), Fix64.FromInt(YCm), Fix64.FromInt(SizeCm), Fix64.Zero, Fix64.Zero);

    private static CrowdRebakeReport? Remove(CrowdSimSession session) =>
        CrowdStructureOps.RemoveStructureAt(session, Fix64.FromInt(XCm), Fix64.FromInt(YCm))
        ?? throw new InvalidOperationException("拆除没有命中建筑。");

    private static JsonObject Row(int tick, JsonObject cmd) => new() { ["tick"] = tick, ["cmd"] = cmd };

    private static string Describe(CrowdStructureFootprint fp) =>
        $"shape={fp.Shape} cx={fp.CenterXCm.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)} cy={fp.CenterYCm.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)} hx={fp.Hx.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)} hy={fp.Hy.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)}";

    private static string Ms(long ticks) =>
        (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.00", CultureInfo.InvariantCulture);
}
