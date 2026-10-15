using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 5 万单位演示 Mod 走真实进图：合并后的脚本与 50k 门禁的 stress-50k.json 逐条相同，tick 2 的四条行军令都成形，
/// 推过门禁预热窗后单位数不变、迷雾在刷新、遥测有逐 tick 的模拟耗时；遥测面板默认打开，玩家视图不按迷雾隐藏。
/// 不计时，不替代门禁。
/// </summary>
[TestFixture]
public sealed class Stress50kShowcaseTests
{
    private const string Scene = "CrowdSimulationStress50kShowcaseMod";

    [Test]
    public void SameScenarioAsGate_RunsThroughWarmup_WithTelemetryPanelOpen()
    {
        var gate = JsonNode.Parse(File.ReadAllText(Path.Combine(
            CrowdShowcaseHost.RepoRoot(), "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod",
            "assets", "CrowdSimulation", "regress", "stress-50k.json")))!.AsObject();
        int units = gate["units"]!.GetValue<int>();
        int warmup = gate["warmupTicks"]!.GetValue<int>();

        using var host = CrowdShowcaseHost.Start(Scene);
        var s = host.Session;
        Assert.That(JsonNode.DeepEquals(host.Script, gate["script"]), Is.True,
            $"演示脚本与门禁脚本不一致：\n{host.Script.ToJsonString()}\n{gate["script"]!.ToJsonString()}");
        var view = CrowdSimulationDebugViewConfig.Load(CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationDebugView.json"), "CrowdSimulationDebugView.json");
        Assert.That(view.Panels.Telemetry, Is.True, "遥测面板应默认打开");
        var player = CrowdPlayerViewConfig.Load(CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationPlayerView.json"), "CrowdSimulationPlayerView.json");
        Assert.That(player.FogHide, Is.False, "压力场景四方都要画出来");

        int[] orderTicks = host.TicksOf("order");
        host.AdvanceTo(orderTicks.Max() + 1);
        Assert.That(s.Units.Count, Is.EqualTo(units), "生成单位数");
        var ordered = s.Orders.List.Select(o => o.Player).OrderBy(p => p).ToArray();
        Assert.That(ordered, Is.EqualTo(s.Config.Deploy.Bases.Select(b => b.PlayerId).OrderBy(p => p).ToArray()), "每座基地一条行军令");

        double maxSimMs = 0;
        host.AdvanceTo(warmup + 2, () => maxSimMs = System.Math.Max(maxSimMs, s.Telemetry.LastTickMs));
        TestContext.Out.WriteLine(
            $"stress: units {s.Units.Count}, orders {ordered.Length}, tick {s.TickCount}, fogRefreshes {s.Telemetry.FogRefreshes}, lastSimMs {s.Telemetry.LastTickMs:F2}, maxSimMs {maxSimMs:F2}");
        Assert.That(s.Units.Count, Is.EqualTo(units), "预热后单位数");
        Assert.That(s.TruthNavFrozen, Is.False);
        Assert.That(s.Telemetry.FogRefreshes, Is.GreaterThan(0), "预热窗里迷雾应在刷新");
        Assert.That(maxSimMs, Is.GreaterThan(0), "遥测没有记到模拟耗时");
    }
}
