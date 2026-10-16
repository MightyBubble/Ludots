using NUnit.Framework;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 七个演示场景都挂 CrowdSimulationUiMod 的 Markup HUD：进图跑几帧后，标题等于合并后的 presentation.title，
/// 玩家条、鼠标说明、指令坞、遥测右栏都在，遥测有单位数；按键图例已经不在页面上。
/// </summary>
[TestFixture]
public sealed class ShowcaseHudTests
{
    [TestCase("CrowdSimulationMarchShowcaseMod")]
    [TestCase("CrowdSimulationRepeatClickShowcaseMod")]
    [TestCase("CrowdSimulationReversalShowcaseMod")]
    [TestCase("CrowdSimulationFogExploreShowcaseMod")]
    [TestCase("CrowdSimulationFogAllyVisionShowcaseMod")]
    [TestCase("CrowdSimulationFogRerouteShowcaseMod")]
    [TestCase("CrowdSimulationStress50kShowcaseMod")]
    public void SceneTitleAndPanels_AreOnTheMarkupHud(string scene)
    {
        using var host = CrowdShowcaseHost.Start(scene);
        string title = CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationDebug.json")["presentation"]!["title"]!.GetValue<string>();
        host.AdvanceTo(2);

        Assert.That(host.HudText("crowd-title"), Is.EqualTo(title));
        Assert.That(host.HudVisible("crowd-playerbar"), Is.True, "玩家条");
        Assert.That(host.HudNode("crowd-players").Children, Has.Count.GreaterThanOrEqualTo(2), "每个玩家一枚");
        Assert.That(host.HudText("crowd-hint-text"), Does.Contain("左键"));
        Assert.That(host.HudVisible("crowd-dock"), Is.True);
        Assert.That(host.HudVisible("crowd-dv"), Is.True);
        Assert.That(host.HudVisible("crowd-telemetry"), Is.True, "原型右栏常驻遥测");
        Assert.That(host.HudText("crowd-m-units"), Does.Match(@"^[\d,]+$"));
        Assert.That(host.HudText("crowd-hint-text"), Does.Contain("F10"), "快捷键说明合并在鼠标提示模板里");
    }
}
