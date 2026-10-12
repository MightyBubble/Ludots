using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 数值不变时 HUD 更新路径不再分配:状态行、重烘行、按键行和遥测行都复用上一份字符串。
/// </summary>
public sealed class CrowdHudAllocTests
{
    [Test]
    public void DrawHud_SteadyState_AllocatesZeroBytes()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s7");
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
                        SizeCm = 14000,
                        SelectAction = "CrowdSimulation.S4.Template1",
                    },
                },
            },
        };
        var demo = new S4DeployDemoRuntime(null);
        demo.UseViewLabels(presentation.ViewLabels);
        demo.UseStructures(new[]
        {
            new S4DeployDemoRuntime.StructureChoice("building", "建筑", S4DeployDemoRuntime.ShapePoint, 14000, 0, "CrowdSimulation.S4.Template1"),
        });
        var overlay = new ScreenOverlayBuffer();
        var system = new S4DeployDemoPresentationSystem(
            demo,
            new RouteVisualBuffer(),
            overlay,
            () => null,
            () => session,
            presentation);

        overlay.Clear();
        system.DrawHud(session, null, null);
        Assert.That(overlay.Count, Is.GreaterThan(0));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            overlay.Clear();
            system.DrawHud(session, null, null);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"数值不变时 HUD 更新分配了 {allocated} 字节");
    }
}
