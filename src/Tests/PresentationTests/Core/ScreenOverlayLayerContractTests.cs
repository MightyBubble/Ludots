using System;
using System.Numerics;
using Ludots.Core.Presentation.Hud;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 屏幕叠加项自己声明合成层：场景构建按项上的层分道，不再把整个缓冲压到 TopMost。
/// 合成顺序是 UnderUi、UI、TopMost，所以 UnderUi 的标记被面板盖住，TopMost 的仍在面板上面。
/// </summary>
[TestFixture]
public sealed class ScreenOverlayLayerContractTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Green = new(0f, 1f, 0f, 1f);

    [Test]
    public void SceneBuilder_PutsEachItemOnItsDeclaredLayer()
    {
        var overlay = new ScreenOverlayBuffer();
        Assert.That(overlay.AddRect(PresentationOverlayLayer.UnderUi, 10, 10, 4, 4, Red, Red, stableId: 1, dirtySerial: 1), Is.True);
        Assert.That(overlay.AddLine(PresentationOverlayLayer.UnderUi, 0, 0, 20, 20, 2, Red, stableId: 2, dirtySerial: 1), Is.True);
        Assert.That(overlay.AddText(PresentationOverlayLayer.UnderUi, 30, 30, "marker", 14, Red, stableId: 3, dirtySerial: 1), Is.True);
        Assert.That(overlay.AddRect(PresentationOverlayLayer.TopMost, 40, 40, 30, 20, Green, Green, stableId: 4, dirtySerial: 1), Is.True);
        Assert.That(overlay.AddLine(PresentationOverlayLayer.TopMost, 40, 40, 70, 60, 2, Green, stableId: 5, dirtySerial: 1), Is.True);
        Assert.That(overlay.AddText(PresentationOverlayLayer.TopMost, 44, 44, "panel", 14, Green, stableId: 6, dirtySerial: 1), Is.True);

        var scene = Build(overlay);

        AssertLane(scene, PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Rect, 1);
        AssertLane(scene, PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Line, 2);
        AssertLane(scene, PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text, 3);
        AssertLane(scene, PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect, 4);
        AssertLane(scene, PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Line, 5);
        AssertLane(scene, PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text, 6);
        Assert.That(overlay.GetSpan()[0].Layer, Is.EqualTo(PresentationOverlayLayer.UnderUi));
        Assert.That(overlay.GetSpan()[3].Layer, Is.EqualTo(PresentationOverlayLayer.TopMost));
    }

    [Test]
    public void CompositeOrder_UiPanelCoversUnderUiMarker_TopMostStaysAbove()
    {
        var overlay = new ScreenOverlayBuffer();
        overlay.AddRect(PresentationOverlayLayer.UnderUi, 20, 20, 10, 10, Red, Red, stableId: 1, dirtySerial: 1);
        overlay.AddRect(PresentationOverlayLayer.TopMost, 60, 20, 10, 10, Green, Green, stableId: 2, dirtySerial: 1);
        var scene = Build(overlay);

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(100, 50, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        renderer.Render(scene, canvas, PresentationOverlayLayer.UnderUi);
        using (var panel = new SKPaint { Color = SKColors.Blue, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(new SKRect(0, 0, 100, 50), panel);
        }

        renderer.Render(scene, canvas, PresentationOverlayLayer.TopMost);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.That(bitmap.GetPixel(25, 25), Is.EqualTo(SKColors.Blue), "UnderUi 的标记应被 UI 面板盖住");
        Assert.That(bitmap.GetPixel(65, 25), Is.EqualTo(new SKColor(0, 255, 0)), "TopMost 的项应画在面板上面");
    }

    private static PresentationOverlayScene Build(ScreenOverlayBuffer overlay)
    {
        var builder = new PresentationOverlaySceneBuilder(new ScreenHudBatchBuffer(4), null, null, null, overlay);
        var scene = new PresentationOverlayScene(16);
        builder.Build(scene);
        return scene;
    }

    private static void AssertLane(PresentationOverlayScene scene, PresentationOverlayLayer layer, PresentationOverlayItemKind kind, int stableId)
    {
        ReadOnlySpan<PresentationOverlayItem> lane = scene.GetLaneSpan(layer, kind);
        Assert.That(lane.Length, Is.EqualTo(1), $"{layer}/{kind}");
        Assert.That(lane[0].StableId, Is.EqualTo(stableId), $"{layer}/{kind}");
    }
}
