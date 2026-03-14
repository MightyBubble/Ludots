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
    public void OverlaySceneBuilder_MapsHudAndOverlayBuffersIntoNativeLayers()
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
        ReadOnlySpan<PresentationOverlayItem> items = scene.GetSpan();

        Assert.That(items.Length, Is.EqualTo(4));
        Assert.That(items[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Bar));
        Assert.That(items[0].Layer, Is.EqualTo(PresentationOverlayLayer.UnderUi));
        Assert.That(items[1].Kind, Is.EqualTo(PresentationOverlayItemKind.Text));
        Assert.That(items[1].Layer, Is.EqualTo(PresentationOverlayLayer.UnderUi));
        Assert.That(items[1].Text, Is.EqualTo("HP 42"));
        Assert.That(items[2].Kind, Is.EqualTo(PresentationOverlayItemKind.Rect));
        Assert.That(items[2].Layer, Is.EqualTo(PresentationOverlayLayer.TopMost));
        Assert.That(items[3].Kind, Is.EqualTo(PresentationOverlayItemKind.Text));
        Assert.That(items[3].Layer, Is.EqualTo(PresentationOverlayLayer.TopMost));
        Assert.That(items[3].Text, Is.EqualTo("Telemetry"));
    }

    [Test]
    public void SkiaOverlayRenderer_DrawsVisiblePixelsForHudAndOverlayLayers()
    {
        var scene = new PresentationOverlayScene(8);
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

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(128, 128));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(CountOpaquePixels(bitmap, 8, 8, 88, 18), Is.GreaterThan(0));
        Assert.That(CountOpaquePixels(bitmap, 64, 64, 104, 88), Is.GreaterThan(0));
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
