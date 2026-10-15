using NUnit.Framework;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 七个演示场景的标题和读数走 CrowdSimulationUiMod 的 HUD 页面：进图跑几帧后，UI 文档里的标题等于合并后的 presentation.title，
/// 状态区有读数。
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
    public void SceneTitleAndStatus_AreOnTheUiPage(string scene)
    {
        using var host = CrowdShowcaseHost.Start(scene);
        string title = CrowdShowcaseHost.MergedConfig(host.Engine, "CrowdSimulationDebug.json")["presentation"]!["title"]!.GetValue<string>();
        host.AdvanceTo(2);

        Assert.That(host.HudText("crowd-title"), Is.EqualTo(title));
        Assert.That(host.HudText("crowd-status"), Does.Contain("tick"));
    }
}
