using CrowdSimulationTests.Presentation;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 数值不变时 HUD 不分配：数据口只拷值，控制器比较读数后不重组，UIRoot 不脏也就不渲染。
/// </summary>
public sealed class CrowdHudAllocTests
{
    [Test]
    public void ReadDeploy_SteadyState_AllocatesZeroBytes()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = DeployFeed(new S4DeployDemoRuntime(null), () => session);
        var hud = default(CrowdDeployHud);
        Assert.That(feed.ReadDeploy(ref hud), Is.True);

        long allocated = Measure(() =>
        {
            for (int i = 0; i < 32; i++) feed.ReadDeploy(ref hud);
        });

        Assert.That(allocated, Is.EqualTo(0L), $"数值不变时读数分配了 {allocated} 字节");
        Assert.That(feed.DeployReads, Is.EqualTo(65), "预热一轮加测量一轮，各 32 次");
    }

    [Test]
    public void UiHud_SteadyState_SamplesWithoutComposeOrAllocation()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        var feed = DeployFeed(new S4DeployDemoRuntime(null), () => session);
        using var host = new CrowdHudTestHost();
        host.Frame(feed);
        host.FrameAfterAllPeriods(feed);
        host.FrameAfterAllPeriods(feed);
        Assert.That(host.Text("crowd-status-line"), Does.Contain("tick"));
        int composes = host.Controller.ComposeCount;
        int renders = host.Renders;
        int reads = feed.DeployReads;

        long allocated = Measure(() =>
        {
            for (int i = 0; i < 32; i++) host.FrameAfterAllPeriods(feed);
        });

        Assert.That(feed.DeployReads - reads, Is.EqualTo(64), "预热与测量各 32 帧，每帧都跨过周期，状态面板每帧都该采样");
        Assert.That(host.Controller.ComposeCount, Is.EqualTo(composes), "读数没变不该重组");
        Assert.That(host.Renders, Is.EqualTo(renders), "页面没变 UIRoot 不该重渲染");
        Assert.That(allocated, Is.EqualTo(0L), $"读数不变时 HUD 帧分配了 {allocated} 字节");
    }

    [Test]
    public void UiHud_ContactsChange_ComposesOnceThenRepeatIsZeroBytes()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, session.Config, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.ChunkDebugPanelRuntime, new ChunkDebugPanelRuntime { Visible = true });
        var demo = new S4DeployDemoRuntime(engine);
        var feed = DeployFeed(demo, () => session);
        using var host = new CrowdHudTestHost(engine);

        session.Movement!.ContactSum = 4;
        session.PublishTelemetryFrame(stalled: false, tickMs: 1);
        host.Frame(feed);
        host.FrameAfterAllPeriods(feed);
        Assert.That(host.Text("crowd-counter-detail-line"), Does.Contain("contacts 4"));

        session.Movement.ContactSum = 11;
        session.PublishTelemetryFrame(stalled: false, tickMs: 1);
        int composes = host.Controller.ComposeCount;
        host.FrameAfterAllPeriods(feed);
        Assert.That(host.Controller.ComposeCount, Is.EqualTo(composes + 1), "接触读数变化应重组一次");
        Assert.That(host.Text("crowd-counter-detail-line"), Does.Contain("contacts 11"));
        Assert.That(host.AllText(), Does.Not.Contain("contacts 15"));

        long hit = Measure(() => host.FrameAfterAllPeriods(feed));
        Assert.That(hit, Is.EqualTo(0L), $"同一读数再采样分配了 {hit} 字节");
        Assert.That(host.Text("crowd-counter-detail-line"), Does.Contain("contacts 11"));
    }

    /// <summary>先跑一遍预热，再量第二遍的分配。</summary>
    internal static long Measure(Action body)
    {
        body();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    internal static CrowdHudFeed DeployFeed(S4DeployDemoRuntime demo, Func<CrowdSimSession?> session)
    {
        var presentation = DeployPresentation();
        demo.UseViewLabels(presentation.ViewLabels);
        demo.UseStructures(new[]
        {
            new S4DeployDemoRuntime.StructureChoice("building", "建筑", S4DeployDemoRuntime.ShapePoint, 14000, 0, "CrowdSimulation.S4.Template1"),
        });
        return new CrowdHudFeed(demo, presentation, session, () => null, null);
    }

    internal static CrowdPresentationConfig DeployPresentation() => new()
    {
        Kind = CrowdPresentationConfig.KindDeploy,
        Title = "S4",
        ViewLabels = new[] { "路线", "可走", "线框" },
        Hud = new CrowdHudConfig
        {
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
                    SizeCm = 14000,
                    SelectAction = "CrowdSimulation.S4.Template1",
                },
            },
        },
    };
}
