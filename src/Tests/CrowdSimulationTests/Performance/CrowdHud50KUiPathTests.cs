using System.Diagnostics;
using System.Globalization;
using CrowdSimulationTests.Parity;
using CrowdSimulationTests.Presentation;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 50k 压力场景下全部 HUD 面板打开（状态、玩家卡、检视、导航、遥测），走 UIRoot + Skia + UiSurfaceHost 真实链路。
/// 每个仿真 tick 之后跑一帧 HUD，时钟按 16 ms 一帧推进。
/// 断言：预热后没到采样点的帧零分配；HUD 帧不进 tick 计时，tick 中位数打开与不开 HUD 在噪声内。
/// tick 比较只在优化构建上作硬门；Debug 只记数字。
/// </summary>
public sealed class CrowdHud50KUiPathTests
{
    private const int Rounds = 6;
    private const int TicksPerRound = 40;
    private const double FrameMs = 16d;

    [Test]
    public void AllPanelsVisible_NonSampleFramesAllocateNothing_AndTickTimeStaysInNoise()
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

        double hiddenMedian = Median(hidden);
        double visibleMedian = Median(visible);
        double hiddenSpread = Percentile(hidden, 0.75) - Percentile(hidden, 0.25);
        double noise = Math.Max(0.5, Math.Max(hiddenSpread, hiddenMedian * 0.10));
        string line =
            $"50k HUD ui-path: tickMedian hidden={hiddenMedian:F3}ms visible={visibleMedian:F3}ms noise={noise:F3}ms " +
            $"hudFrame median={Median(hudFrameMs):F3}ms p95={Percentile(hudFrameMs, 0.95):F3}ms max={hudFrameMs.Max():F3}ms " +
            $"nonSampleFrames={nonSampleFrames} nonSampleAlloc={nonSampleAlloc} sampleFrames={sampleFrames} sampleAllocAvg={(sampleFrames > 0 ? sampleAlloc / sampleFrames : 0)} " +
            $"composes={ui.Controller.ComposeCount - composes0} renders={ui.Renders - renders0} lastComposeMs={ui.Controller.LastComposeMs:F3} " +
            $"reads status={feed.DeployReads} player={feed.PlayerReads} inspect={feed.InspectReads} nav={feed.NavReads} telemetry={feed.TelemetryReads}";
        TestContext.Out.WriteLine(line);
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "artifacts", "benchmarks", "crowd-hud-50k-ui"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "trace.txt"), line + "\n" + $"load {File.ReadAllText("/proc/loadavg").Trim()}\n");

        Assert.That(nonSampleFrames, Is.GreaterThan(0), "没量到非采样帧");
        Assert.That(sampleFrames, Is.GreaterThan(0), "没量到采样帧");
        Assert.That(feed.NavReads, Is.GreaterThan(0));
        Assert.That(feed.TelemetryReads, Is.GreaterThan(0));
        Assert.That(feed.PlayerReads, Is.GreaterThan(0));
        Assert.That(nonSampleAlloc, Is.EqualTo(0L), $"非采样帧分配了 {nonSampleAlloc} 字节");
#if !DEBUG
        Assert.That(visibleMedian - hiddenMedian, Is.LessThanOrEqualTo(noise),
            $"打开 HUD 后 tick 中位数 {visibleMedian:F3} ms，关着 {hiddenMedian:F3} ms，差超过噪声 {noise:F3} ms");
#endif
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
