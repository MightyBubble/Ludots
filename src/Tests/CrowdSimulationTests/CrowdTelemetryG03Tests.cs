using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 子步钳位、挤离无处安放、规划应用在事件点推进遥测,并出现在调试面板区块。
/// </summary>
public sealed class CrowdTelemetryG03Tests
{
    [Test]
    public void Counters_IncrementAtClampEvictAndPlan()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        Assert.That(session.Telemetry.SubStepClamped, Is.EqualTo(0), "默认步长封顶用不上,不应记钳位");
        Assert.That(session.SubSteps, Is.LessThanOrEqualTo(runtime.Movement.MaxSubSteps));

        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        int spawned = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 8, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(8));
        session.Commands.Submit(
            session,
            new JsonObject
            {
                ["type"] = "order",
                ["player"] = 1,
                ["xCm"] = 1200000,
                ["yCm"] = 800000,
                ["shape"] = "box",
            },
            CrowdSimCommands.Exec);
        int guard = 0;
        while (session.Telemetry.Plans == 0)
        {
            if (guard++ > 20) Assert.Fail("下令后应有成功应用的规划");
            Assert.That(session.Step(out _), Is.True);
        }

        Assert.That(session.Telemetry.Plans, Is.GreaterThan(0));

        foreach (var nav in session.Navs.Values) Array.Clear(nav.Passable);
        int navCells = session.Config.NavCellsPerSide;
        CrowdStructureOps.EvictBlocked(session, (0, 0, navCells, navCells));
        Assert.That(session.Telemetry.EvictStuck, Is.GreaterThan(0), "全图不可走时挤离应计入无处安放");

        typeof(RuntimeMovementSection)
            .GetProperty(nameof(RuntimeMovementSection.MaxStepNavCells))!
            .SetValue(runtime.Movement, Fix64.FromDouble(0.01));
        var clamped = new CrowdSimSession(
            runtime, World.Create(), session.Navs, session.NavByLayerRadius, session.Presentation);
        Assert.That(clamped.Telemetry.SubStepClamped, Is.EqualTo(1));
        Assert.That(clamped.SubSteps, Is.EqualTo(runtime.Movement.MaxSubSteps));
        Assert.That(session.Telemetry.SubStepClamped, Is.EqualTo(0), "已构造的会话不因事后改配置重记钳位");
    }

    [Test]
    public void Panel_ShowsG03Counters_CollapsedUntilExpanded()
    {
        var telemetry = new CrowdSimulationTelemetry();
        telemetry.MarkSubStepClamped();
        telemetry.MarkEvictStuck(3);
        telemetry.MarkPlans(2);
        telemetry.BeginFrame(10, 7, stalled: false, tickMs: 1);
        telemetry.MarkPathWait();

        var hud = new CrowdHudPanel
        {
            OriginXPx = 16,
            OriginYPx = 16,
            WidthPx = 640,
            HeightPx = 148,
            Keys = "k",
        };
        var panel = new CrowdSimTelemetryPanel();
        var overlay = new ScreenOverlayBuffer();
        panel.Draw(overlay, hud, telemetry, expanded: false, visibleCells: 4, exploredCells: 5, ghostCells: 6);
        string collapsed = JoinText(overlay);
        Assert.That(collapsed, Does.Contain("subStepClamped 1"));
        Assert.That(collapsed, Does.Contain("evictStuck 3"));
        Assert.That(collapsed, Does.Contain("plans 2"));
        Assert.That(collapsed, Does.Contain("contacts 7"));
        Assert.That(collapsed, Does.Not.Contain("pathWait"));

        overlay.Clear();
        panel.Draw(overlay, hud, telemetry, expanded: true, visibleCells: 4, exploredCells: 5, ghostCells: 6);
        string expanded = JoinText(overlay);
        Assert.That(expanded, Does.Contain("pathWait 1"));
        Assert.That(expanded, Does.Contain("fogCells vis 4 explored 5 ghost 6"));
    }

    private static string JoinText(ScreenOverlayBuffer overlay)
    {
        var parts = new List<string>();
        foreach (var item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text) continue;
            string? text = overlay.GetString(item.StringId);
            if (text != null) parts.Add(text);
        }

        return string.Join('\n', parts);
    }
}
