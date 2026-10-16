using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using CrowdSimulationTests.Presentation;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 子步钳位、挤离无处安放、规划应用在事件点推进遥测,并出现在调试面板区块。
/// </summary>
public sealed class CrowdTelemetryClampEvictPlanTests
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
        session.Commands.Submit(session, MoveOrder(session, 1, 1200000, 800000), CrowdSimCommands.Exec);
        int guard = 0;
        while (session.Telemetry.Plans == 0)
        {
            if (guard++ > 20) Assert.Fail("下令后应有成功应用的规划");
            Assert.That(session.Step(out _), Is.True);
        }

        Assert.That(session.Telemetry.Plans, Is.GreaterThan(0));

        session.Commands.Submit(
            session,
            JsonNode.Parse("""{"type":"placeStructure","template":"s7barrier","xCm":480000,"yCm":560000,"sizeCm":200000}""")!,
            CrowdSimCommands.Exec);
        foreach (var nav in session.Navs.Values) Array.Clear(nav.Passable);
        CrowdStructureOps.FinishRebake(session);
        Assert.That(session.LastRebakeReport, Is.Not.Null);
        Assert.That(session.LastRebakeReport!.Stuck, Is.GreaterThan(0), "阶段 1 应把本次挤离无处安放写入重烘报告");
        Assert.That(session.Telemetry.EvictStuck, Is.GreaterThan(0));

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
    public void Hud_HidesDetailUntilChunkDebugVisible()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        session.Telemetry.MarkSubStepClamped();
        session.Telemetry.MarkEvictStuck(3);
        session.Telemetry.MarkPlans(2);
        session.Telemetry.BeginFrame(10, 7, stalled: false, tickMs: 1);
        session.Telemetry.MarkPathWait();

        using var engine = new GameEngine();
        var chunkDebug = new ChunkDebugPanelRuntime();
        engine.SetService(CoreServiceKeys.ChunkDebugPanelRuntime, chunkDebug);
        var feed = HudFeed(engine, session);
        using var ui = new CrowdHudTestHost(engine);

        ui.Frame(feed);
        string collapsed = ui.Text("crowd-status");
        Assert.That(collapsed, Does.Contain("subStepClamped 1"));
        Assert.That(collapsed, Does.Contain("evictStuck 3"));
        Assert.That(collapsed, Does.Contain("plans 2"));
        Assert.That(collapsed, Does.Not.Contain("contacts"));
        Assert.That(collapsed, Does.Not.Contain("pathWait"));
        Assert.That(ui.Has("crowd-counter-detail-line"), Is.False);

        chunkDebug.Visible = true;
        ui.FrameAfterAllPeriods(feed);
        string expanded = ui.Text("crowd-counter-detail-line");
        Assert.That(expanded, Does.Contain("contacts 7"));
        Assert.That(expanded, Does.Contain("pathWait 1"));
        Assert.That(expanded, Does.Contain("fogCells"));
    }

    private static CrowdHudFeed HudFeed(GameEngine engine, CrowdSimSession session)
    {
        var presentation = new CrowdPresentationConfig
        {
            Kind = CrowdPresentationConfig.KindDeploy,
            Title = "S4",
            ViewLabels = CrowdViewCatalog.Named(("route", "路线"), ("walkable", "可走"), ("wire", "线框")),
            Hud = new CrowdHudConfig { Keys = "k {view}" },
            Deploy = new CrowdDeployPresentation
            {
                Actions = new CrowdDeployActions
                {
                    CycleView = "a", ToggleBuild = "b", Remove = "c", Place = "d",
                },
                BuildActivity = "建造",
                ObserveActivity = "观察",
                PlaceOk = "可放",
                PlaceBlocked = "不可放",
                PlaceOffMap = "界外",
                PendingNav = "待导航",
                Palette = new[]
                {
                    new CrowdPaletteSlotDto { Entry = "building", SelectAction = "t" },
                },
            },
        };
        var demo = new S4DeployDemoRuntime(engine);
        demo.UseViewLabels(presentation.ViewLabels);
        demo.UsePalette(new[]
        {
            new S4DeployDemoRuntime.PaletteChoice("building", "building", "建筑", CrowdPlacementGesture.Point, 14000, 0, 0, "t"),
        });
        return new CrowdHudFeed(demo, presentation, () => session, () => null, null);
    }

    private static JsonObject MoveOrder(CrowdSimSession session, int player, int xCm, int yCm)
    {
        var handles = new uint[session.Units.Count];
        int count = session.Units.CopyOwnHandles(player, handles);
        var members = new JsonArray();
        for (int i = 0; i < count; i++) members.Add((long)handles[i]);
        return new JsonObject
        {
            ["type"] = "order",
            ["player"] = player,
            ["xCm"] = xCm,
            ["yCm"] = yCm,
            ["shape"] = "box",
            ["members"] = members,
        };
    }
}
