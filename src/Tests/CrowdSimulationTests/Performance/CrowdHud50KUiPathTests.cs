using System.Diagnostics;
using System.Globalization;
using CrowdSimulationTests.Parity;
using CrowdSimulationTests.Presentation;
using CrowdSimulationUiMod.UI;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 50k 压力场景下全部 HUD 面板打开（状态、玩家卡、检视、导航、遥测），走 UIRoot + Skia + UiSurfaceHost 真实链路。
/// 第一段每个仿真 tick 之后跑一帧 HUD，比较 HUD 开关两种情况下的 tick 中位数。
/// 第二段模拟 30 s：HUD 按 16 ms 一帧推进，仿真按固定频率在帧间步进。
/// 断言：预热后采样帧和非采样帧都零分配，窗口内零 GC，HUD 重画帧的折算 CPU 时间 p95 不超过 1.5 ms，tick 中位数打开与不开在噪声内。
/// 重画按测试线程的 CPU 时间量，被抢占、等调度不算。缓存和内存带宽被别的进程占着时 CPU 时间仍会成倍变长，
/// 所以每次重画后紧跟着量一遍固定的 Skia 光栅参照（<see cref="SkiaReferenceRaster"/>），按同一帧两者的比值记成本：
/// 折算 = 重画 CPU ÷ 本帧参照 CPU × <see cref="ReferenceRasterNominalMs"/>。参照只在重画帧跑，和 HUD 一样隔着仿真 tick 冷启动，
/// 机器忙时两者一起变慢；HUD 自己多做的活不会让参照变慢，照样超预算。
/// 墙钟和未折算的 CPU 时间只记进 trace。计时类断言只在优化构建上作硬门；Debug 只记数字。
/// </summary>
public sealed class CrowdHud50KUiPathTests
{
    private const int Rounds = 6;
    private const int TicksPerRound = 40;
    private const double FrameMs = 16d;
    private const double WindowSeconds = 30d;
    private const double RedrawP95BudgetMs = 1.5d;
    /// <summary>参照光栅在定 1.5 ms 预算的那台机器上、空闲时的 CPU 时间中位数，把比值换回毫秒。</summary>
    private const double ReferenceRasterNominalMs = 0.87d;

    [Test]
    public void AllPanelsVisible_FramesAllocateNothing_NoGcIn30s_RedrawWithinBudget_AndTickTimeStaysInNoise()
    {
        var scenario = CrowdKernel50KBenchmarkTests.LoadScenario();
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        var reference = CrowdReferenceScript.Load(scenario.Script, session.Units.Capacity);
        var watch = new Stopwatch();
        CrowdKernel50KBenchmarkTests.Advance(session, reference, watch, scenario.WarmupTicks, null, null, null);
        Assert.That(session.Units.Count, Is.EqualTo(scenario.Units));

        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var debug = CrowdHudSamplingTests.ShippedDebugView();
        debug.NavPanel = true;
        debug.TelemetryPanel = true;
        debug.UnitColorMode = CrowdSimulationDebugViewConfig.ColorTeam;
        var player = CrowdHudSamplingTests.ShippedPlayerView(session);
        player.Draw(session, new Ludots.Platform.Abstractions.RouteVisualBuffer(), new Ludots.Core.Presentation.Hud.ScreenOverlayBuffer());
        feed.UseDebugView(debug);
        feed.UsePlayerView(player);
        using var ui = new CrowdHudTestHost();

        for (int i = 0; i < 64; i++)
        {
            Tick(session, reference, watch);
            ui.Frame(feed, FrameMs);
        }

        foreach (string id in new[] { "crowd-left", "crowd-right", "crowd-dock", "crowd-toolbar", "crowd-player", "crowd-playerbar", "crowd-order", "crowd-inspect", "crowd-nav", "crowd-telemetry", "crowd-status", "crowd-legend" })
            Assert.That(ui.Has(id), Is.True, $"#{id} 应在 UI 文档里");
        Assert.That(ui.Text("crowd-status-line"), Does.Contain(scenario.Units.ToString(CultureInfo.InvariantCulture)));
        WarmSparkGlyphs(ui);

        var hidden = new List<double>();
        var visible = new List<double>();
        var hudFrameMs = new List<double>();
        long nonSampleAlloc = 0, sampleAlloc = 0;
        int nonSampleFrames = 0, sampleFrames = 0, renders0 = ui.Renders, composes0 = ui.Controller.ComposeCount;
        var hudWatch = new Stopwatch();
        for (int round = 0; round < Rounds; round++)
        {
            bool showHud = (round & 1) == 1;
            for (int t = 0; t < TicksPerRound; t++)
            {
                double tickMs = Tick(session, reference, watch);
                (showHud ? visible : hidden).Add(tickMs);
                if (!showHud) continue;

                int reads = TotalReads(feed);
                long before = GC.GetAllocatedBytesForCurrentThread();
                hudWatch.Restart();
                ui.Frame(feed, FrameMs);
                hudWatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                hudFrameMs.Add(hudWatch.Elapsed.TotalMilliseconds);
                if (TotalReads(feed) == reads)
                {
                    nonSampleFrames++;
                    nonSampleAlloc += allocated;
                }
                else
                {
                    sampleFrames++;
                    sampleAlloc += allocated;
                }
            }
        }

        var window = RunWindow(session, reference, watch, ui, feed);

        double hiddenMedian = Median(hidden);
        double visibleMedian = Median(visible);
        double hiddenSpread = Percentile(hidden, 0.75) - Percentile(hidden, 0.25);
        double noise = Math.Max(0.5, Math.Max(hiddenSpread, hiddenMedian * 0.10));
        string line =
            $"50k HUD ui-path: tickMedian hidden={hiddenMedian:F3}ms visible={visibleMedian:F3}ms noise={noise:F3}ms " +
            $"hudFrame median={Median(hudFrameMs):F3}ms p95={Percentile(hudFrameMs, 0.95):F3}ms max={hudFrameMs.Max():F3}ms " +
            $"nonSampleFrames={nonSampleFrames} nonSampleAlloc={nonSampleAlloc} sampleFrames={sampleFrames} sampleAllocAvg={(sampleFrames > 0 ? sampleAlloc / sampleFrames : 0)} " +
            $"composes={ui.Controller.ComposeCount - composes0} renders={ui.Renders - renders0} lastComposeMs={ui.Controller.LastComposeMs:F3} " +
            $"reads status={feed.DeployReads} player={feed.PlayerReads} inspect={feed.InspectReads} nav={feed.NavReads} telemetry={feed.TelemetryReads}\n" +
            $"window {WindowSeconds:F0}s: frames={window.Frames} ticks={window.Ticks} gc0={window.Gc0} gc1={window.Gc1} gc2={window.Gc2} " +
            $"processAllocated={window.ProcessAllocated} hudAlloc sample={window.SampleAlloc} nonSample={window.NonSampleAlloc} sampleFrames={window.SampleFrames} " +
            $"redraws={window.Redraws.Count} redrawCost median={Median(window.RedrawCost):F3}ms p95={Percentile(window.RedrawCost, 0.95):F3}ms max={window.RedrawCost.Max():F3}ms " +
            $"redrawCpu median={Median(window.RedrawCpu):F3}ms p95={Percentile(window.RedrawCpu, 0.95):F3}ms " +
            $"referenceCpu median={Median(window.ReferenceCpu):F3}ms p95={Percentile(window.ReferenceCpu, 0.95):F3}ms " +
            $"redrawWall median={Median(window.Redraws):F3}ms p95={Percentile(window.Redraws, 0.95):F3}ms max={window.Redraws.Max():F3}ms " +
            $"composes={window.Composes}";
        TestContext.Out.WriteLine(line);
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "artifacts", "benchmarks", "crowd-hud-50k-ui"));
        Directory.CreateDirectory(dir);
        string load = OperatingSystem.IsLinux() ? $"load {File.ReadAllText("/proc/loadavg").Trim()}\n" : "";
        File.WriteAllText(Path.Combine(dir, "trace.txt"), line + "\n" + load);

        Assert.That(nonSampleFrames, Is.GreaterThan(0), "没量到非采样帧");
        Assert.That(sampleFrames, Is.GreaterThan(0), "没量到采样帧");
        Assert.That(feed.NavReads, Is.GreaterThan(0));
        Assert.That(feed.TelemetryReads, Is.GreaterThan(0));
        Assert.That(feed.PlayerReads, Is.GreaterThan(0));
        Assert.That(nonSampleAlloc, Is.EqualTo(0L), $"非采样帧分配了 {nonSampleAlloc} 字节");
        Assert.That(sampleAlloc, Is.EqualTo(0L), $"采样帧分配了 {sampleAlloc} 字节（{sampleFrames} 帧）");
        Assert.That(window.SampleFrames, Is.GreaterThan(0), "窗口内没量到采样帧");
        Assert.That(window.Redraws.Count, Is.GreaterThan(0), "窗口内没有重画");
        Assert.That(window.SampleAlloc + window.NonSampleAlloc, Is.EqualTo(0L),
            $"窗口内 HUD 帧分配了 采样 {window.SampleAlloc} / 非采样 {window.NonSampleAlloc} 字节");
        Assert.That(window.Gc0 + window.Gc1 + window.Gc2, Is.EqualTo(0),
            $"{WindowSeconds:F0} s 窗口内发生 GC：gen0 {window.Gc0} gen1 {window.Gc1} gen2 {window.Gc2}（进程共分配 {window.ProcessAllocated} 字节）");
#if !DEBUG
        Assert.That(Percentile(window.RedrawCost, 0.95), Is.LessThanOrEqualTo(RedrawP95BudgetMs),
            $"HUD 重画帧折算 CPU 时间 p95 {Percentile(window.RedrawCost, 0.95):F3} ms 超过 {RedrawP95BudgetMs} ms" +
            $"（未折算 CPU p95 {Percentile(window.RedrawCpu, 0.95):F3} ms，墙钟 p95 {Percentile(window.Redraws, 0.95):F3} ms）");
        Assert.That(visibleMedian - hiddenMedian, Is.LessThanOrEqualTo(noise),
            $"打开 HUD 后 tick 中位数 {visibleMedian:F3} ms，关着 {hiddenMedian:F3} ms，差超过噪声 {noise:F3} ms");
#endif
    }

    private sealed class WindowStats
    {
        public int Frames;
        public int Ticks;
        public int SampleFrames;
        public long SampleAlloc;
        public long NonSampleAlloc;
        public long ProcessAllocated;
        public int Gc0;
        public int Gc1;
        public int Gc2;
        public int Composes;
        public readonly List<double> Redraws = new();
        public readonly List<double> RedrawCpu = new();
        public readonly List<double> ReferenceCpu = new();
        public readonly List<double> RedrawCost = new();
    }

    /// <summary>
    /// 墙钟不参与：HUD 时钟每帧前进 16 ms，仿真按固定频率在帧间补步，相当于 30 s 的实际运行。
    /// 统计列表预先留够容量，窗口内的记账本身不分配。
    /// </summary>
    private static WindowStats RunWindow(CrowdSimSession session, CrowdReferenceScript reference, Stopwatch watch, CrowdHudTestHost ui, CrowdHudFeed feed)
    {
        int frames = (int)(WindowSeconds * 1000d / FrameMs);
        double tickPeriodMs = 1000d / session.Config.FixedHz;
        var stats = new WindowStats { Frames = frames };
        stats.Redraws.Capacity = frames;
        stats.RedrawCpu.Capacity = frames;
        stats.ReferenceCpu.Capacity = frames;
        stats.RedrawCost.Capacity = frames;
        var redrawCpuRaw = new long[frames];
        var referenceCpuRaw = new long[frames];
        using var raster = new SkiaReferenceRaster((int)ui.Width, (int)ui.Height);
        raster.Run();
        var hudWatch = new Stopwatch();
        double simClockMs = 0d;
        int composes0 = ui.Controller.ComposeCount;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        long process0 = GC.GetTotalAllocatedBytes(precise: true);
        var cpu = ThreadCpuClock.StartOnCurrentThread();
        for (int f = 0; f < frames; f++)
        {
            simClockMs += FrameMs;
            while (simClockMs >= tickPeriodMs)
            {
                Tick(session, reference, watch);
                simClockMs -= tickPeriodMs;
                stats.Ticks++;
            }

            int reads = TotalReads(feed);
            int renders = ui.Renders;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long cpu0 = cpu.ReadRaw();
            hudWatch.Restart();
            ui.Frame(feed, FrameMs);
            hudWatch.Stop();
            long cpuRaw = cpu.ReadRaw() - cpu0;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (ui.Renders != renders)
            {
                long reference0 = cpu.ReadRaw();
                raster.Run();
                referenceCpuRaw[stats.Redraws.Count] = cpu.ReadRaw() - reference0;
                redrawCpuRaw[stats.Redraws.Count] = cpuRaw;
                stats.Redraws.Add(hudWatch.Elapsed.TotalMilliseconds);
            }
            if (TotalReads(feed) == reads)
            {
                stats.NonSampleAlloc += allocated;
            }
            else
            {
                stats.SampleFrames++;
                stats.SampleAlloc += allocated;
            }
        }

        stats.ProcessAllocated = GC.GetTotalAllocatedBytes(precise: true) - process0;
        double rawPerMs = cpu.RawPerMs();
        for (int i = 0; i < stats.Redraws.Count; i++)
        {
            stats.RedrawCpu.Add(redrawCpuRaw[i] / rawPerMs);
            stats.ReferenceCpu.Add(referenceCpuRaw[i] / rawPerMs);
        }

        for (int i = 0; i < stats.Redraws.Count; i++)
            stats.RedrawCost.Add(stats.RedrawCpu[i] / stats.ReferenceCpu[i] * ReferenceRasterNominalMs);
        stats.Gc0 = GC.CollectionCount(0) - gc0;
        stats.Gc1 = GC.CollectionCount(1) - gc1;
        stats.Gc2 = GC.CollectionCount(2) - gc2;
        stats.Composes = ui.Controller.ComposeCount - composes0;
        return stats;
    }

    /// <summary>
    /// 火花线的八级方块字不在首选字体里，走回退字体；回退查找第一次见到某个字会分配一次（填字体缓存）。
    /// 预热时火花线历史还短，八级不一定都出现过，所以按火花线节点的样式把整套字量一遍，之后才算稳态。
    /// </summary>
    private static void WarmSparkGlyphs(CrowdHudTestHost ui)
    {
        UiNode spark = FindByClass(ui.Find(CrowdHudView.RootId)!, "crowd-spark")
            ?? throw new AssertionException("预热后遥测面板里应已有火花线");
        ui.TextMeasurer.MeasureWidth(CrowdHudController.SparkBlocks, spark.Style);
    }

    private static UiNode? FindByClass(UiNode node, string className)
    {
        if (node.HasClass(className)) return node;
        for (int i = 0; i < node.Children.Count; i++)
        {
            if (FindByClass(node.Children[i], className) is { } found) return found;
        }

        return null;
    }

    private static double Tick(CrowdSimSession session, CrowdReferenceScript reference, Stopwatch watch)
    {
        int guard = 64;
        while (guard-- > 0)
        {
            reference.ApplyDue(session);
            watch.Restart();
            bool advanced = session.Step(out _);
            watch.Stop();
            if (advanced) return watch.Elapsed.TotalMilliseconds;
        }

        throw new InvalidOperationException("50k 推进停摆过长。");
    }

    private static int TotalReads(CrowdHudFeed feed) =>
        feed.DeployReads + feed.PlayerReads + feed.InspectReads + feed.NavReads + feed.TelemetryReads + feed.FaultReads;

    private static double Median(List<double> values) => Percentile(values, 0.5);

    private static double Percentile(List<double> values, double q)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * q))];
    }
}
