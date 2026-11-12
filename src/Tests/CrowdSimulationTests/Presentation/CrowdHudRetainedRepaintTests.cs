using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// HUD 读数变化时渲染器只清掉并重画改动节点的墨迹范围。仿真推进、各面板按自己的周期采样，
/// 每次重画后保留画布要和另起画布整张重画逐像素一致；采样帧要走局部重画，不能退回整张。
/// </summary>
public sealed class CrowdHudRetainedRepaintTests
{
    private const int Frames = 200;

    [TestCase(1920, 1080)]
    [TestCase(1280, 720)]
    public void SampleRedraws_RepaintOnlyDamage_AndMatchAFullRepaintPixelForPixel(int width, int height)
    {
        using var ui = CrowdHudLayoutTests.AllPanelsOpen(width, height, out var paths, out var session, out var feed);
        using var service = paths;
        int first = ui.DiffAgainstFullRepaint(out var firstBounds);
        Assert.That(first, Is.Zero, $"开局几帧画完后有 {first} 个像素和整张重画不同，范围 {firstBounds}");

        int full0 = ui.Renderer.FullPaintCount, damage0 = ui.Renderer.DamagePaintCount, compared = 0;
        for (int f = 0; f < Frames; f++)
        {
            session.Step(out _);
            int renders = ui.Renders;
            ui.Frame(feed);
            if (ui.Renders == renders) continue;
            compared++;
            int diff = ui.DiffAgainstFullRepaint(out var bounds);
            Assert.That(diff, Is.Zero, $"第 {f} 帧局部重画后有 {diff} 个像素和整张重画不同，范围 {bounds}");
        }

		Assert.That(compared, Is.GreaterThan(5), "窗口里读数应变过多次");
        Assert.That(ui.Renderer.FullPaintCount - full0, Is.Zero, "只改读数的帧不该整张重画");
        Assert.That(ui.Renderer.DamagePaintCount - damage0, Is.EqualTo(compared));
    }
}
