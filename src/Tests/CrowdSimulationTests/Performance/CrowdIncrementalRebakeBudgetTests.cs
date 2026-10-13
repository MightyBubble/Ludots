using System.Diagnostics;
using System.Globalization;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// S4 种子 1337、建筑 14000 cm 的暖机增量重烘。全量校验关闭。
/// 硬门只在优化构建上生效:报告里的工作刻度必须低于 16 ms,且本次重烘本身不分配。
/// 放置之后的脏 tile 闪烁,折线建一次,随后的呈现帧也不分配。
/// </summary>
public sealed class CrowdIncrementalRebakeBudgetTests
{
    private const int XCm = 503125;
    private const int YCm = 565625;
    private const int SizeCm = 14000;
    private const double BudgetMs = 16.0;

    [Test]
    public void WarmPlaceAndRemove_StaysUnderBudget_AndFlashFramesAllocateNothing()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(session.VerifyIncrementalNav, Is.False);

        Drain(session, Place(session));
        Drain(session, Remove(session));
        WaitForLoad();

        double maxMs = 0;
        long maxAlloc = 0;
        CrowdRebakeReport? placed = null;
        for (int i = 0; i < 4; i++)
        {
            var place = Measure(session, Place);
            placed = place.Report;
            Note(i, "place", place, ref maxMs, ref maxAlloc);
            AssertWork(place.Report);
            if (i == 3) AssertFlashSteady(session);
            var remove = Measure(session, Remove);
            Note(i, "remove", remove, ref maxMs, ref maxAlloc);
            AssertWork(remove.Report);
        }

        TestContext.Out.WriteLine(
            $"warm rebake max={maxMs.ToString("0.00", CultureInfo.InvariantCulture)} ms alloc={maxAlloc} tiles={placed!.Tiles} contexts={placed.Contexts}");
#if DEBUG
        TestContext.Out.WriteLine($"Debug 不测 {BudgetMs:F0} ms 门。max={maxMs:F2}");
#else
        Assert.That(maxMs, Is.LessThan(BudgetMs),
            $"暖机增量重烘 {maxMs.ToString("0.00", CultureInfo.InvariantCulture)} ms,未低于 {BudgetMs:F0} ms");
#endif
        Assert.That(maxAlloc, Is.EqualTo(0L), $"暖机增量重烘分配了 {maxAlloc} 字节");
    }

    private static void AssertWork(CrowdRebakeReport report)
    {
        Assert.That(report.Tiles, Is.GreaterThanOrEqualTo(1));
        Assert.That(report.Contexts, Is.EqualTo(4));
        Assert.That(report.CostOnly, Is.EqualTo(0));
        Assert.That(report.Hits, Is.GreaterThan(0));
        Assert.That(report.Misses, Is.EqualTo(0));
        Assert.That(report.Orders, Is.EqualTo(0));
        Assert.That(report.Refreshes, Is.EqualTo(0));
    }

    private static void AssertFlashSteady(CrowdSimSession session)
    {
        var demo = new S4DeployDemoRuntime(null);
        demo.UseViewLabels(new[] { "路线", "可走", "线框" });
        demo.UseStructures(new[]
        {
            new S4DeployDemoRuntime.StructureChoice("building", "建筑", S4DeployDemoRuntime.ShapePoint, SizeCm, 0, "CrowdSimulation.S4.Template1"),
        });
        var presentation = new CrowdPresentationConfig
        {
            Kind = CrowdPresentationConfig.KindDeploy,
            Title = "S4",
            ViewLabels = new[] { "路线", "可走", "线框" },
            Hud = new CrowdHudPanel
            {
                OriginXPx = 16,
                OriginYPx = 16,
                WidthPx = 640,
                HeightPx = 148,
                Keys = "V 视图 {view} · 模板[{templateIndex}] · {activity}{pending}",
            },
            Deploy = new CrowdDeployPresentation
            {
                Actions = new CrowdDeployActions
                {
                    CycleView = "CrowdSimulation.S4.CycleView",
                    ToggleBuild = "CrowdSimulation.S4.ToggleBuild",
                    Remove = "CrowdSimulation.S4.RemoveStructure",
                    Place = "CrowdSimulation.S4.Place",
                },
                BuildActivity = "建造模式 · {label}{placeHint}",
                ObserveActivity = "观察模式",
                PlaceOk = "(此处可放)",
                PlaceBlocked = "(此处不可放)",
                PlaceOffMap = "(光标不在地图上)",
                PendingNav = " · 导航变体未就绪",
                Structures = new[]
                {
                    new CrowdStructureTemplateDto
                    {
                        Id = "building",
                        Label = "建筑",
                        Shape = S4DeployDemoRuntime.ShapePoint,
                        SizeCm = SizeCm,
                        SelectAction = "CrowdSimulation.S4.Template1",
                    },
                },
            },
        };
        var routes = new RouteVisualBuffer();
        var overlay = new ScreenOverlayBuffer();
        var system = new S4DeployDemoPresentationSystem(demo, routes, overlay, () => null, () => session, presentation);
        for (int i = 0; i < 2; i++)
        {
            overlay.Clear();
            system.Update(0f);
        }

        Assert.That(routes.Polylines.Count, Is.GreaterThan(0), "脏 tile 闪烁应提交折线");
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            overlay.Clear();
            system.Update(0f);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"闪烁稳态呈现分配了 {allocated} 字节");
        Assert.That(routes.Polylines.Count, Is.GreaterThan(0));
    }

    private readonly record struct Sample(CrowdRebakeReport Report, double Ms, long Alloc);

    private static Sample Measure(CrowdSimSession session, Func<CrowdSimSession, CrowdRebakeReport?> op)
    {
        _ = GC.GetAllocatedBytesForCurrentThread();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var report = Drain(session, op(session));
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        return new Sample(report, report.ElapsedTicks * 1000.0 / Stopwatch.Frequency, alloc);
    }

    private static void Note(int iter, string op, Sample sample, ref double maxMs, ref long maxAlloc)
    {
        if (sample.Ms > maxMs) maxMs = sample.Ms;
        if (sample.Alloc > maxAlloc) maxAlloc = sample.Alloc;
        TestContext.Out.WriteLine(
            $"rebake {op} {iter} {sample.Ms.ToString("0.00", CultureInfo.InvariantCulture)} ms alloc={sample.Alloc} " +
            $"tiles={sample.Report.Tiles} contexts={sample.Report.Contexts} costOnly={sample.Report.CostOnly} " +
            $"hits={sample.Report.Hits} misses={sample.Report.Misses} orders={sample.Report.Orders} refreshes={sample.Report.Refreshes}");
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

    private static CrowdRebakeReport? Remove(CrowdSimSession session)
    {
        int before = session.Structures!.EntityIds.Count;
        var report = CrowdStructureOps.RemoveStructureAt(session, Fix64.FromInt(XCm), Fix64.FromInt(YCm));
        if (report == null && session.RebakeJob == null && session.Structures.EntityIds.Count == before)
            throw new InvalidOperationException("拆除没有命中建筑。");
        return report;
    }

    private static void WaitForLoad()
    {
        string loadAtWindow = File.ReadAllText("/proc/loadavg").Trim();
        double load1 = double.Parse(loadAtWindow.Split(' ', 2)[0], CultureInfo.InvariantCulture);
        int cores = Environment.ProcessorCount;
        var wait = Stopwatch.StartNew();
        while (load1 > cores && wait.ElapsedMilliseconds < 180_000)
        {
            Thread.Sleep(5000);
            loadAtWindow = File.ReadAllText("/proc/loadavg").Trim();
            load1 = double.Parse(loadAtWindow.Split(' ', 2)[0], CultureInfo.InvariantCulture);
        }

        TestContext.Out.WriteLine($"rebake window load1={load1.ToString("F2", CultureInfo.InvariantCulture)} cores={cores} waitMs={wait.ElapsedMilliseconds} load={loadAtWindow}");
    }
}
