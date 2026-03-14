using System;
using System.Numerics;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using Ludots.Presentation.Skia;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class NativeSkiaOverlayTests
{
    [Test]
    public void OverlaySceneBuilder_MapsHudAndOverlayBuffersIntoNativeLanes()
    {
        var screenHud = new ScreenHudBatchBuffer(8);
        var catalog = CreateCatalog();
        var locale = new PresentationTextLocaleSelection(catalog);
        var worldHudStrings = new WorldHudStringTable(catalog, locale, legacyCapacity: 4);
        var overlayBuffer = new ScreenOverlayBuffer();

        var textPacket = PresentationTextPacket.FromToken(1);
        textPacket.SetArg(0, PresentationTextArg.FromInt32(42));

        screenHud.TryAdd(new ScreenHudItem
        {
            Kind = WorldHudItemKind.Bar,
            ScreenX = 10f,
            ScreenY = 12f,
            Width = 90f,
            Height = 8f,
            Value0 = 0.75f,
            Color0 = new Vector4(0.1f, 0.1f, 0.1f, 0.9f),
            Color1 = new Vector4(0.2f, 0.8f, 0.2f, 1f),
        });

        screenHud.TryAdd(new ScreenHudItem
        {
            Kind = WorldHudItemKind.Text,
            ScreenX = 16f,
            ScreenY = 28f,
            FontSize = 18,
            Color0 = new Vector4(1f, 1f, 1f, 1f),
            Text = textPacket,
        });

        overlayBuffer.AddRect(100, 110, 64, 24, new Vector4(0f, 0f, 0f, 0.85f), new Vector4(1f, 1f, 1f, 1f));
        overlayBuffer.AddText(108, 116, "Telemetry", 14, new Vector4(0.9f, 0.9f, 0.2f, 1f));

        var builder = new PresentationOverlaySceneBuilder(screenHud, worldHudStrings, catalog, locale, overlayBuffer);
        var scene = new PresentationOverlayScene(16);

        builder.Build(scene);

        ReadOnlySpan<PresentationOverlayItem> underUiBars = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar);
        ReadOnlySpan<PresentationOverlayItem> underUiText = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text);
        ReadOnlySpan<PresentationOverlayItem> topRects = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect);
        ReadOnlySpan<PresentationOverlayItem> topText = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text);

        Assert.That(scene.Count, Is.EqualTo(4));
        Assert.That(scene.DirtyLaneCount, Is.EqualTo(4));
        Assert.That(underUiBars.Length, Is.EqualTo(1));
        Assert.That(underUiText.Length, Is.EqualTo(1));
        Assert.That(topRects.Length, Is.EqualTo(1));
        Assert.That(topText.Length, Is.EqualTo(1));
        Assert.That(underUiBars[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Bar));
        Assert.That(underUiText[0].Text, Is.EqualTo("HP 42"));
        Assert.That(topRects[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Rect));
        Assert.That(topText[0].Text, Is.EqualTo("Telemetry"));
    }

    [Test]
    public void OverlaySceneBuilder_RetainsUnchangedLanes_AndOnlyInvalidatesTouchedLane()
    {
        var screenHud = new ScreenHudBatchBuffer(8);
        var overlayBuffer = new ScreenOverlayBuffer();
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, overlayBuffer);
        var scene = new PresentationOverlayScene(16);

        SeedLegacyOverlay(screenHud, overlayBuffer, "Telemetry");
        builder.Build(scene);

        int underUiBarVersion = scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar);
        int underUiTextVersion = scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text);
        int topRectVersion = scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect);
        int topTextVersion = scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text);

        screenHud.Clear();
        overlayBuffer.Clear();
        SeedLegacyOverlay(screenHud, overlayBuffer, "Telemetry");
        builder.Build(scene);

        Assert.That(scene.DirtyLaneCount, Is.EqualTo(0));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar), Is.EqualTo(underUiBarVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text), Is.EqualTo(underUiTextVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect), Is.EqualTo(topRectVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text), Is.EqualTo(topTextVersion));

        screenHud.Clear();
        overlayBuffer.Clear();
        SeedLegacyOverlay(screenHud, overlayBuffer, "Telemetry+");
        builder.Build(scene);

        Assert.That(scene.DirtyLaneCount, Is.EqualTo(1));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar), Is.EqualTo(underUiBarVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text), Is.EqualTo(underUiTextVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect), Is.EqualTo(topRectVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text), Is.GreaterThan(topTextVersion));
    }

    [Test]
    public void SkiaOverlayRenderer_RetainsLanePictures_AndTextLayoutsUntilContentChanges()
    {
        var scene = new PresentationOverlayScene(8);
        scene.BeginBuild();
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 8f,
            y: 8f,
            width: 80f,
            height: 10f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f));
        scene.TryAddText(
            PresentationOverlayLayer.UnderUi,
            x: 10f,
            y: 24f,
            text: "HUD",
            fontSize: 16,
            color: new Vector4(1f, 1f, 1f, 1f));
        scene.TryAddRect(
            PresentationOverlayLayer.TopMost,
            x: 64f,
            y: 64f,
            width: 40f,
            height: 24f,
            fill: new Vector4(0f, 0f, 0f, 0.85f),
            border: new Vector4(1f, 0.8f, 0.2f, 1f));
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(128, 128));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        int firstRebuiltLaneCount = renderer.RebuiltLaneCountLastFrame;
        int firstLayoutCacheCount = renderer.CachedTextLayoutCount;

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(firstRebuiltLaneCount, Is.EqualTo(3));
        Assert.That(firstLayoutCacheCount, Is.GreaterThan(0));
        Assert.That(CountOpaquePixels(bitmap, 8, 8, 88, 18), Is.GreaterThan(0));
        Assert.That(CountOpaquePixels(bitmap, 64, 64, 104, 88), Is.GreaterThan(0));

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0));
        Assert.That(renderer.CachedTextLayoutCount, Is.EqualTo(firstLayoutCacheCount));

        scene.BeginBuild();
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 8f,
            y: 8f,
            width: 80f,
            height: 10f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f));
        scene.TryAddText(
            PresentationOverlayLayer.UnderUi,
            x: 10f,
            y: 24f,
            text: "HUD+",
            fontSize: 16,
            color: new Vector4(1f, 1f, 1f, 1f));
        scene.TryAddRect(
            PresentationOverlayLayer.TopMost,
            x: 64f,
            y: 64f,
            width: 40f,
            height: 24f,
            fill: new Vector4(0f, 0f, 0f, 0.85f),
            border: new Vector4(1f, 0.8f, 0.2f, 1f));
        scene.EndBuild();

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(1));
    }

    [Test]
    public void SkiaOverlayRenderer_UsesImmediatePath_ForLargeUnderUiHudLanes()
    {
        var scene = new PresentationOverlayScene(256);
        scene.BeginBuild();
        for (int i = 0; i < 64; i++)
        {
            scene.TryAddBar(
                PresentationOverlayLayer.UnderUi,
                x: 8f,
                y: 8f + (i * 3f),
                width: 80f,
                height: 2f,
                value: 0.5f,
                background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
                foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f));
            scene.TryAddText(
                PresentationOverlayLayer.UnderUi,
                x: 96f,
                y: 4f + (i * 3f),
                text: $"{100 + i}",
                fontSize: 12,
                color: new Vector4(1f, 1f, 1f, 1f));
        }

        scene.TryAddRect(
            PresentationOverlayLayer.TopMost,
            x: 64f,
            y: 64f,
            width: 40f,
            height: 24f,
            fill: new Vector4(0f, 0f, 0f, 0.85f),
            border: new Vector4(1f, 0.8f, 0.2f, 1f));
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(1),
            "Large dynamic HUD lanes should draw immediately without rebuilding retained lane pictures.");
        Assert.That(renderer.CachedTextLayoutCount, Is.GreaterThan(0));

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0));
    }

    private static void SeedLegacyOverlay(ScreenHudBatchBuffer screenHud, ScreenOverlayBuffer overlayBuffer, string topText)
    {
        screenHud.TryAdd(new ScreenHudItem
        {
            Kind = WorldHudItemKind.Bar,
            ScreenX = 10f,
            ScreenY = 12f,
            Width = 90f,
            Height = 8f,
            Value0 = 0.75f,
            Color0 = new Vector4(0.1f, 0.1f, 0.1f, 0.9f),
            Color1 = new Vector4(0.2f, 0.8f, 0.2f, 1f),
        });
        screenHud.TryAdd(new ScreenHudItem
        {
            Kind = WorldHudItemKind.Text,
            ScreenX = 16f,
            ScreenY = 28f,
            FontSize = 18,
            Color0 = new Vector4(1f, 1f, 1f, 1f),
            Id1 = (int)WorldHudValueMode.AttributeCurrent,
            Value0 = 42f,
        });

        overlayBuffer.AddRect(100, 110, 64, 24, new Vector4(0f, 0f, 0f, 0.85f), new Vector4(1f, 1f, 1f, 1f));
        overlayBuffer.AddText(108, 116, topText, 14, new Vector4(0.9f, 0.9f, 0.2f, 1f));
    }

    private static int CountOpaquePixels(SKBitmap bitmap, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static PresentationTextCatalog CreateCatalog()
    {
        var tokenIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        tokenIds.Register("hud.hp");
        tokenIds.Freeze();

        var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        localeIds.Register("en-US");
        localeIds.Freeze();

        var tokens = new PresentationTextTokenDefinition[2];
        tokens[1] = new PresentationTextTokenDefinition
        {
            TokenId = 1,
            Key = "hud.hp",
            ArgCount = 1,
        };

        var templates = new PresentationTextTemplate[2];
        templates[1] = new PresentationTextTemplate(
            "HP {0}",
            new[]
            {
                new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, "HP ", argIndex: -1),
                new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, argIndex: 0),
            });

        var locales = new PresentationTextLocaleTable[2];
        locales[1] = new PresentationTextLocaleTable(1, "en-US", templates);

        return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: 1);
    }
}
