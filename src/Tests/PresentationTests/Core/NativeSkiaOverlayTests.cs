using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
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
        var worldHudStrings = new WorldHudStringTable(catalog, locale, runtimeStringCapacity: 4);
        var overlayBuffer = new ScreenOverlayBuffer();
        var minimapMarkers = new MinimapScreenMarkerBuffer(8);

        var textPacket = PresentationTextPacket.FromToken(1);
        textPacket.SetArg(0, PresentationTextArg.FromInt32(42));

        screenHud.TryAdd(new ScreenHudItem
        {
            StableId = 101,
            DirtySerial = 1001,
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
            StableId = 202,
            DirtySerial = 2002,
            Kind = WorldHudItemKind.Text,
            ScreenX = 16f,
            ScreenY = 28f,
            FontSize = 18,
            Color0 = new Vector4(1f, 1f, 1f, 1f),
            Text = textPacket,
        });

        overlayBuffer.AddRect(100, 110, 64, 24, new Vector4(0f, 0f, 0f, 0.85f), new Vector4(1f, 1f, 1f, 1f));
        overlayBuffer.AddText(108, 116, "Telemetry", 14, new Vector4(0.9f, 0.9f, 0.2f, 1f));
        overlayBuffer.AddLine(112, 120, 156, 132, 4, new Vector4(1f, 0.85f, 0.2f, 1f));
        minimapMarkers.TryAdd(303, 144f, 152f, new Vector4(0.18f, 0.82f, 1f, 1f), 8f);

        var builder = new PresentationOverlaySceneBuilder(screenHud, worldHudStrings, catalog, locale, overlayBuffer, minimapMarkers);
        var scene = new PresentationOverlayScene(16);

        builder.Build(scene);

        ReadOnlySpan<PresentationOverlayItem> underUiBars = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar);
        ReadOnlySpan<PresentationOverlayItem> underUiText = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text);
        ReadOnlySpan<PresentationOverlayItem> topRects = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect);
        ReadOnlySpan<PresentationOverlayItem> topText = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text);
        ReadOnlySpan<PresentationOverlayItem> topLines = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Line);

        Assert.That(scene.Count, Is.EqualTo(5));
        Assert.That(scene.DirtyLaneCount, Is.EqualTo(6));
        Assert.That(underUiBars.Length, Is.EqualTo(1));
        Assert.That(underUiText.Length, Is.EqualTo(1));
        Assert.That(topRects.Length, Is.EqualTo(1));
        Assert.That(topText.Length, Is.EqualTo(1));
        Assert.That(topLines.Length, Is.EqualTo(1));
        Assert.That(scene.TopMostMinimapMarkers, Is.SameAs(minimapMarkers));
        Assert.That(underUiBars[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Bar));
        Assert.That(underUiBars[0].StableId, Is.EqualTo(101));
        Assert.That(underUiBars[0].DirtySerial, Is.EqualTo(1001));
        Assert.That(underUiText[0].Text, Is.EqualTo("HP 42"));
        Assert.That(underUiText[0].StableId, Is.EqualTo(202));
        Assert.That(underUiText[0].DirtySerial, Is.EqualTo(2002));
        Assert.That(topRects[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Rect));
        Assert.That(topText[0].Text, Is.EqualTo("Telemetry"));
        Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.MinimapMarker).Length, Is.EqualTo(0));
        Assert.That(scene.TopMostMinimapMarkers!.Count, Is.EqualTo(1));
        Assert.That(scene.TopMostMinimapMarkers.GetStableId(0), Is.EqualTo(303));
        Assert.That(scene.TopMostMinimapMarkers.GetScreenX(0), Is.EqualTo(144f));
        Assert.That(scene.TopMostMinimapMarkers.GetScreenY(0), Is.EqualTo(152f));
        Assert.That(scene.TopMostMinimapMarkers.GetSizePx(0), Is.EqualTo(8f));
        Assert.That(topLines[0].Kind, Is.EqualTo(PresentationOverlayItemKind.Line));
        Assert.That(topLines[0].X, Is.EqualTo(112f));
        Assert.That(topLines[0].Y, Is.EqualTo(120f));
        Assert.That(topLines[0].Width, Is.EqualTo(156f));
        Assert.That(topLines[0].Height, Is.EqualTo(132f));
        Assert.That(topLines[0].Value0, Is.EqualTo(4f));
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
        int underUiLayerVersion = scene.GetLayerVersion(PresentationOverlayLayer.UnderUi);
        int topLayerVersion = scene.GetLayerVersion(PresentationOverlayLayer.TopMost);

        screenHud.Clear();
        overlayBuffer.Clear();
        SeedLegacyOverlay(screenHud, overlayBuffer, "Telemetry");
        builder.Build(scene);

        Assert.That(scene.DirtyLaneCount, Is.EqualTo(0));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar), Is.EqualTo(underUiBarVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text), Is.EqualTo(underUiTextVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect), Is.EqualTo(topRectVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text), Is.EqualTo(topTextVersion));
        Assert.That(scene.GetLayerVersion(PresentationOverlayLayer.UnderUi), Is.EqualTo(underUiLayerVersion));
        Assert.That(scene.GetLayerVersion(PresentationOverlayLayer.TopMost), Is.EqualTo(topLayerVersion));

        screenHud.Clear();
        overlayBuffer.Clear();
        SeedLegacyOverlay(screenHud, overlayBuffer, "Telemetry+");
        builder.Build(scene);

        Assert.That(scene.DirtyLaneCount, Is.EqualTo(1));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar), Is.EqualTo(underUiBarVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text), Is.EqualTo(underUiTextVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect), Is.EqualTo(topRectVersion));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text), Is.GreaterThan(topTextVersion));
        Assert.That(scene.GetLayerVersion(PresentationOverlayLayer.UnderUi), Is.EqualTo(underUiLayerVersion));
        Assert.That(scene.GetLayerVersion(PresentationOverlayLayer.TopMost), Is.GreaterThan(topLayerVersion));
    }

    [Test]
    public void OverlaySceneBuilder_RebuildsPerFrameScreenOverlay_WhenHudRevisionIsUnchanged()
    {
        var screenHud = new ScreenHudBatchBuffer(8);
        var overlayBuffer = new ScreenOverlayBuffer();
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, overlayBuffer);
        var scene = new PresentationOverlayScene(16);

        overlayBuffer.AddText(108, 116, "NO MINIMAP SIGNALS", 14, new Vector4(1f, 0.7f, 0.4f, 1f));
        builder.Build(scene);

        Assert.That(GetSingleTopMostText(scene), Is.EqualTo("NO MINIMAP SIGNALS"));
        int topTextVersion = scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text);

        overlayBuffer.Clear();
        overlayBuffer.AddText(108, 116, "RTS full-map preset", 14, new Vector4(0.7f, 0.9f, 1f, 1f));
        builder.Build(scene);

        Assert.That(GetSingleTopMostText(scene), Is.EqualTo("RTS full-map preset"));
        Assert.That(scene.GetLaneVersion(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text), Is.GreaterThan(topTextVersion));

        overlayBuffer.Clear();
        builder.Build(scene);

        Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text).Length, Is.EqualTo(0));
        Assert.That(scene.ContainsLayer(PresentationOverlayLayer.TopMost), Is.False);
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
    public void SkiaOverlayRenderer_TopMostOpaquePanelOccludesUnderUiHud()
    {
        var scene = new PresentationOverlayScene(8);
        scene.BeginBuild();
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 32f,
            y: 32f,
            width: 112f,
            height: 64f,
            value: 1f,
            background: new Vector4(0f, 0.15f, 0f, 1f),
            foreground: new Vector4(0f, 1f, 0f, 1f));
        scene.TryAddText(
            PresentationOverlayLayer.UnderUi,
            x: 48f,
            y: 62f,
            text: "999/999",
            fontSize: 22,
            color: new Vector4(1f, 1f, 1f, 1f));
        scene.TryAddRect(
            PresentationOverlayLayer.TopMost,
            x: 40f,
            y: 40f,
            width: 104f,
            height: 72f,
            fill: new Vector4(0.02f, 0.05f, 0.07f, 1f),
            border: new Vector4(0.48f, 0.70f, 0.86f, 1f));
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(180, 140));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        SKColor overlapPixel = bitmap.GetPixel(92, 74);
        Assert.That(IsPixelNear(overlapPixel, new SKColor(5, 13, 18, 255), tolerance: 3), Is.True,
            $"Expected TopMost opaque minimap-style panel to cover UnderUi HUD, got {overlapPixel}.");
        Assert.That(CountPixelsNear(bitmap, new SKColor(0, 255, 0, 255), 64, 58, 120, 92, tolerance: 12), Is.EqualTo(0));
        Assert.That(CountPixelsNear(bitmap, new SKColor(255, 255, 255, 255), 64, 58, 120, 92, tolerance: 12), Is.EqualTo(0));
    }

    [Test]
    public void MinimapRuntime_Render_SubmitsOpaquePanelAndFieldMasks()
    {
        var runtime = new MinimapRuntime(new MinimapRuntimeConfig
        {
            InitialZoomNormalized = 1f,
            WheelZoomNormalizedStep = 0.08f,
            ButtonZoomNormalizedStep = 0.18f,
            ZoomSliderEnabled = true,
            ModeToggleEnabled = true,
            RotateToggleEnabled = true,
            DebugMarkerSampleCapacity = 64,
            MinZoomExtentMode = MinimapZoomExtentMode.OneChunk,
            MaxZoomExtentMode = MinimapZoomExtentMode.FullMap,
            MinZoomExplicitHalfExtentCm = 750f,
            MaxZoomExplicitHalfExtentCm = 0f,
        });
        var overlay = new ScreenOverlayBuffer();

        runtime.Visible = true;
        runtime.Render(overlay);

        ReadOnlySpan<ScreenOverlayItem> span = overlay.GetSpan();
        Assert.That(span.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(span[0].Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
        Assert.That(span[1].Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
        Assert.That(span[0].BackgroundColor.W, Is.EqualTo(1f),
            "Minimap panel must be an opaque TopMost mask so UnderUi world HUD cannot bleed through it.");
        Assert.That(span[1].BackgroundColor.W, Is.EqualTo(1f),
            "Minimap field must be an opaque TopMost mask so UnderUi world HUD cannot bleed through it.");
    }

    [Test]
    public void RaylibOverlayCompositor_DoesNotDirectDrawTopMostWhenUnderUiHudExists()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Adapters",
            "Raylib",
            "Ludots.Adapter.Raylib",
            "RaylibOverlayCompositor.cs"));

        Assert.That(
            source,
            Does.Contain("hasTopOverlay && _useGpuDirectUnderlay && !hasUnderlay && !hasUiLayer"),
            "TopMost direct overlay must stay disabled while UnderUi HUD exists; otherwise minimap is drawn under world HUD.");
        Assert.That(
            source,
            Does.Contain("orderedDirectOverlayComposite = hasUnderlay && hasTopOverlay && _useGpuDirectUnderlay"),
            "UnderUi and TopMost may share a direct GPU path when the compositor draws TopMost after the HUD.");
        Assert.That(
            source,
            Does.Not.Contain("hasUnderlay && hasTopOverlay && !hasUiLayer && _useGpuDirectUnderlay"),
            "A mounted Skia panel must not kick world HUD off the GPU underlay path.");
        Assert.That(
            source,
            Does.Contain("drawCompositeTexture"),
            "Skia UI must blit after GPU UnderUi HUD and before TopMost minimap so the UAT panel can stay mounted without rasterizing world HUD every frame.");
        Assert.That(
            source,
            Does.Contain("_framebufferTopOverlaySurface.Render("),
            "TopMost minimap must still render after direct UnderUi HUD so it occludes HUD bars/text.");
    }

    [Test]
    public void RaylibHostLoop_KeepsNativeDiagnosticHudOptIn()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Adapters",
            "Raylib",
            "Ludots.Adapter.Raylib",
            "RaylibHostLoop.cs"));

        Assert.That(
            source,
            Does.Contain("\"LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD\","),
            "Native Raylib diagnostics should stay available for explicit profiling runs.");
        Assert.That(
            source,
            Does.Contain("defaultValue: false"),
            "Native Raylib diagnostics must be opt-in so showcase screenshots are not covered by debug text.");
    }

    [Test]
    public void SkiaOverlayRenderer_DrawsLargeUnderUiHudLanesImmediately_WhenDirty()
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
            "Large HUD lanes should bypass SKPicture rebuild on dirty frames while unrelated retained lanes still cache normally.");

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0));
    }

    [Test]
    public void SkiaOverlayRenderer_DrawsLargeTopMostTextLane_WhenTextHasNoStableId()
    {
        var scene = new PresentationOverlayScene(256);
        scene.BeginBuild();
        for (int i = 0; i < 64; i++)
        {
            scene.TryAddText(
                PresentationOverlayLayer.TopMost,
                x: 10f,
                y: 6f + (i * 3f),
                text: $"M{i}",
                fontSize: 12,
                color: new Vector4(1f, 1f, 1f, 1f));
        }

        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0));
        Assert.That(CountOpaquePixels(bitmap, 10, 6, 60, 42), Is.GreaterThan(0),
            "Large TopMost diagnostic text emitted from ScreenOverlayBuffer has no stable ids and must still render.");
    }

    [Test]
    public void SkiaOverlayRenderer_ConsumesPrebucketedScreenMarkers()
    {
        var screenMarkers = new MinimapScreenMarkerBuffer(512);
        screenMarkers.BeginBucketedFrame();
        var color = new Vector4(0f, 1f, 0.75f, 1f);
        for (int i = 0; i < 300; i++)
        {
            float x = 20f + ((i % 30) * 7f);
            float y = 20f + ((i / 30) * 7f);
            Assert.That(screenMarkers.TryAddBucketKey(in color, 6f, 0u, 0f, 0f, out _), Is.True);
        }

        screenMarkers.MaterializeBuckets();
        for (int i = 0; i < 300; i++)
        {
            float x = 20f + ((i % 30) * 7f);
            float y = 20f + ((i / 30) * 7f);
            Assert.That(screenMarkers.TryGetBucketIndex(in color, 6f, 0u, 0f, 0f, out int bucketIndex), Is.True);
            Assert.That(screenMarkers.TryAddToBucket(bucketIndex, 10_000 + i, x, y, in color, 6f), Is.True);
        }

        var scene = new PresentationOverlayScene(512);
        scene.BeginBuild();
        scene.SetTopMostMinimapMarkers(screenMarkers);
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 128));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(screenMarkers.Count, Is.EqualTo(300));
        Assert.That(screenMarkers.BucketCount, Is.EqualTo(1));
        Assert.That(renderer.LastMinimapMarkerBatchBucketCount, Is.EqualTo(1));
        Assert.That(CountOpaquePixels(bitmap, 16, 16, 240, 100), Is.GreaterThan(1200));
        Assert.That(CountPixelsNear(bitmap, new SKColor(0, 255, 191, 255), 16, 16, 240, 100, tolerance: 10), Is.GreaterThan(600));
    }

    [Test]
    public void SkiaOverlayRenderer_DrawsPrebucketedMinimapMarkerOrientationWithExpectedColor()
    {
        var color = new Vector4(1f, 0.2f, 0.05f, 1f);
        var key = MinimapScreenMarkerBuffer.CreateBucketKey(
            in color,
            sizePx: 14f,
            MinimapMarkerFlags.HasOrientation,
            orientationRad: 0f,
            orientationLengthPx: 34f);
        var screenMarkers = new MinimapScreenMarkerBuffer(16);
        screenMarkers.BeginBucketedFrame();
        Assert.That(screenMarkers.TryAddBucketKey(in key, out int bucketIndex), Is.True);
        screenMarkers.MaterializeBuckets();
        Assert.That(screenMarkers.TryAddProjectedToBucket(
            bucketIndex,
            stableId: 404,
            screenX: 40f,
            screenY: 48f,
            out _), Is.True);

        var scene = new PresentationOverlayScene(16);
        scene.BeginBuild();
        scene.SetTopMostMinimapMarkers(screenMarkers);
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(96, 96));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(CountOpaquePixels(bitmap, 34, 42, 80, 55), Is.GreaterThan(130));
        Assert.That(CountPixelsNear(bitmap, new SKColor(255, 51, 13, 255), 54, 45, 78, 51, tolerance: 18), Is.GreaterThan(16));
    }

    [Test]
    public void SkiaOverlayRenderer_PreheatedPrebucketedMinimapMarkers_DoNotAllocatePerMarkerManagedMemory()
    {
        const int markerCount = 30000;
        var scene = BuildPrebucketedOrientationMarkerScene(markerCount);
        var baselineScene = BuildPrebucketedOrientationMarkerScene(MinimapScreenMarkerBuffer.OrientationBucketCount);

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(768, 768, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        for (int warm = 0; warm < 3; warm++)
        {
            canvas.Clear(SKColors.Transparent);
            renderer.ResetFrameStats();
            renderer.Render(scene, canvas, PresentationOverlayLayer.TopMost);
            canvas.Clear(SKColors.Transparent);
            renderer.ResetFrameStats();
            renderer.Render(baselineScene, canvas, PresentationOverlayLayer.TopMost);
        }

        long bucketedDrawBaseline = MeasureRenderAllocation(renderer, baselineScene, canvas);

        renderer.ResetFrameStats();
        long before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Render(scene, canvas, PresentationOverlayLayer.TopMost);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(
            allocated,
            Is.LessThanOrEqualTo(bucketedDrawBaseline),
            "Preheated 30k marker atlas draw must not allocate managed memory beyond the fixed one-atlas-call bucket baseline.");
        Assert.That(allocated, Is.LessThanOrEqualTo(64), "Any remaining allocation must stay a tiny fixed native-call cost, not scale with marker count.");
        Assert.That(renderer.LastMinimapMarkerBatchBucketCount, Is.EqualTo(MinimapScreenMarkerBuffer.OrientationBucketCount));
        Assert.That(renderer.LastMinimapMarkerBatchDrawMs, Is.GreaterThanOrEqualTo(0d));
    }

    private static PresentationOverlayScene BuildPrebucketedOrientationMarkerScene(int markerCount)
    {
        var screenMarkers = new MinimapScreenMarkerBuffer(markerCount);
        screenMarkers.BeginBucketedFrame();
        var color = new Vector4(1f, 0.18f, 0.08f, 1f);
        for (int i = 0; i < markerCount; i++)
        {
            int bucket = i & (MinimapScreenMarkerBuffer.OrientationBucketCount - 1);
            var key = MinimapScreenMarkerBuffer.CreateBucketKey(
                in color,
                sizePx: 7f,
                MinimapMarkerFlags.HasOrientation,
                WorldPlane2D.BucketToFacingRad(bucket, MinimapScreenMarkerBuffer.OrientationBucketCount),
                orientationLengthPx: 14f);
            Assert.That(screenMarkers.TryAddBucketKey(in key, out _), Is.True);
        }

        screenMarkers.MaterializeBuckets();
        for (int i = 0; i < markerCount; i++)
        {
            int bucket = i & (MinimapScreenMarkerBuffer.OrientationBucketCount - 1);
            var key = MinimapScreenMarkerBuffer.CreateBucketKey(
                in color,
                sizePx: 7f,
                MinimapMarkerFlags.HasOrientation,
                WorldPlane2D.BucketToFacingRad(bucket, MinimapScreenMarkerBuffer.OrientationBucketCount),
                orientationLengthPx: 14f);
            Assert.That(screenMarkers.TryGetBucketIndex(in key, out int bucketIndex), Is.True);
            float x = 24f + ((i % 220) * 3.2f);
            float y = 24f + (((i / 220) % 180) * 3.2f);
            Assert.That(screenMarkers.TryAddProjectedToBucket(bucketIndex, i + 1, x, y, out _), Is.True);
        }

        var scene = new PresentationOverlayScene(16);
        scene.BeginBuild();
        scene.SetTopMostMinimapMarkers(screenMarkers);
        scene.EndBuild();
        return scene;
    }

    private static long MeasureRenderAllocation(
        SkiaOverlayRenderer renderer,
        PresentationOverlayScene scene,
        SKCanvas canvas)
    {
        canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        long before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Render(scene, canvas, PresentationOverlayLayer.TopMost);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void SkiaOverlayRenderer_DrawsLineLaneWithExpectedColor()
    {
        var scene = new PresentationOverlayScene(16);
        scene.BeginBuild();
        scene.TryAddLine(
            PresentationOverlayLayer.TopMost,
            x0: 24f,
            y0: 24f,
            x1: 116f,
            y1: 84f,
            thickness: 5f,
            color: new Vector4(1f, 0.86f, 0.3f, 1f));
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(144, 108));
        surface.Canvas.Clear(SKColors.Transparent);

        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.TopMost);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(CountOpaquePixels(bitmap, 20, 20, 122, 90), Is.GreaterThan(320));
        Assert.That(CountPixelsNear(bitmap, new SKColor(255, 219, 76, 255), 20, 20, 122, 90, tolerance: 14), Is.GreaterThan(220));
    }

    [Test]
    public void PresentationOverlayLanePacer_AlternatesLargeDirtyUnderUiLanes()
    {
        var scene = new PresentationOverlayScene(256);
        BuildLargeUnderUiScene(scene, xOffset: 0f);

        var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
        PresentationOverlayLanePacer.LaneRefreshPlan coldStartPlan = pacer.BuildPlan(scene);
        Assert.That(coldStartPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.True);
        Assert.That(coldStartPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.True);

        pacer.MarkPresented(scene, coldStartPlan);

        BuildLargeUnderUiScene(scene, xOffset: 1f);
        PresentationOverlayLanePacer.LaneRefreshPlan firstDeferredPlan = pacer.BuildPlan(scene);
        Assert.That(firstDeferredPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.False);
        Assert.That(firstDeferredPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.True);

        pacer.MarkPresented(scene, firstDeferredPlan);

        BuildLargeUnderUiScene(scene, xOffset: 2f);
        PresentationOverlayLanePacer.LaneRefreshPlan secondDeferredPlan = pacer.BuildPlan(scene);
        Assert.That(secondDeferredPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.True);
        Assert.That(secondDeferredPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.False);
    }

    [Test]
    public void PresentationOverlayScene_ClassifiesPositionOnlyLaneMutation_AndAverageTranslation()
    {
        var scene = new PresentationOverlayScene(256);
        BuildLargeUnderUiScene(scene, xOffset: 0f);
        BuildLargeUnderUiScene(scene, xOffset: 6f);

        Assert.That(scene.GetLaneMutationKind(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar),
            Is.EqualTo(PresentationOverlayLaneMutationKind.PositionOnly));
        Assert.That(scene.GetLaneMutationKind(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text),
            Is.EqualTo(PresentationOverlayLaneMutationKind.PositionOnly));
        Assert.That(scene.GetLaneAverageTranslation(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar).X,
            Is.EqualTo(6f).Within(0.001f));
        Assert.That(scene.GetLaneAverageTranslation(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text).X,
            Is.EqualTo(6f).Within(0.001f));
        Assert.That(scene.TryGetLaneUniformTranslation(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar, out Vector2 barTranslation), Is.True);
        Assert.That(barTranslation.X, Is.EqualTo(6f).Within(0.001f));
        Assert.That(scene.TryGetLaneUniformTranslation(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text, out Vector2 textTranslation), Is.True);
        Assert.That(textTranslation.X, Is.EqualTo(6f).Within(0.001f));
    }

    [Test]
    public void PresentationOverlayScene_RejectsNonUniformLaneTranslation()
    {
        var scene = new PresentationOverlayScene(32);
        scene.BeginBuild();
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 8f,
            y: 8f,
            width: 12f,
            height: 2f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
            stableId: 1,
            dirtySerial: 11);
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 8f,
            y: 14f,
            width: 12f,
            height: 2f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
            stableId: 2,
            dirtySerial: 22);
        scene.EndBuild();

        scene.BeginBuild();
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 10f,
            y: 8f,
            width: 12f,
            height: 2f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
            stableId: 1,
            dirtySerial: 11);
        scene.TryAddBar(
            PresentationOverlayLayer.UnderUi,
            x: 11f,
            y: 14f,
            width: 12f,
            height: 2f,
            value: 0.5f,
            background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
            foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
            stableId: 2,
            dirtySerial: 22);
        scene.EndBuild();

        Assert.That(scene.GetLaneMutationKind(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar),
            Is.EqualTo(PresentationOverlayLaneMutationKind.PositionOnly));
        Assert.That(scene.TryGetLaneUniformTranslation(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Bar, out _), Is.False);
    }

    [Test]
    public void PresentationOverlayLanePacer_DoesNotDeferLargeLane_WhenContentChanges()
    {
        var scene = new PresentationOverlayScene(256);
        BuildLargeUnderUiScene(scene, xOffset: 0f);

        var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);
        PresentationOverlayLanePacer.LaneRefreshPlan coldStartPlan = pacer.BuildPlan(scene);
        pacer.MarkPresented(scene, coldStartPlan);

        BuildLargeUnderUiSceneWithTextValueOffset(scene, xOffset: 0f, valueOffset: 1);
        PresentationOverlayLanePacer.LaneRefreshPlan contentPlan = pacer.BuildPlan(scene);

        Assert.That(scene.GetLaneMutationKind(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text),
            Is.EqualTo(PresentationOverlayLaneMutationKind.Content));
        Assert.That(contentPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.False);
        Assert.That(contentPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.True);
    }

    [Test]
    public void SkiaOverlayRenderer_UsesLargeLaneHotpath_WhenPacerDefersRefresh()
    {
        var scene = new PresentationOverlayScene(256);
        BuildLargeUnderUiScene(scene, xOffset: 0f);

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);

        PresentationOverlayLanePacer.LaneRefreshPlan coldStartPlan = pacer.BuildPlan(scene);
        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, coldStartPlan);
        pacer.MarkPresented(scene, coldStartPlan);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0),
            "Large UnderUi bar/text lanes should stay on the immediate hotpath instead of rebuilding SKPictures.");

        BuildLargeUnderUiScene(scene, xOffset: 1f);
        PresentationOverlayLanePacer.LaneRefreshPlan refreshPlan = pacer.BuildPlan(scene);
        Assert.That(refreshPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.False);
        Assert.That(refreshPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.True);

        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, refreshPlan);
        pacer.MarkPresented(scene, refreshPlan);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0));
        Assert.That(CountOpaquePixels(bitmap, 96, 4, 132, 28), Is.GreaterThan(0),
            "Large text lane should stay current-frame while the skipped bar lane remains drawable through the large-lane hotpath.");
    }

    [Test]
    public void SkiaOverlayRenderer_ReusesSkippedLargeBarLanePicture_WhenPacerDefersTextRefresh()
    {
        var scene = new PresentationOverlayScene(256);
        BuildLargeUnderUiScene(scene, xOffset: 0f);

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        var pacer = new PresentationOverlayLanePacer(PresentationOverlayLayer.UnderUi);

        PresentationOverlayLanePacer.LaneRefreshPlan coldStartPlan = pacer.BuildPlan(scene);
        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, coldStartPlan);
        pacer.MarkPresented(scene, coldStartPlan);

        BuildLargeUnderUiScene(scene, xOffset: 1f);
        PresentationOverlayLanePacer.LaneRefreshPlan firstPlan = pacer.BuildPlan(scene);
        Assert.That(firstPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.False);
        Assert.That(firstPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.True);

        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, firstPlan);
        pacer.MarkPresented(scene, firstPlan);

        BuildLargeUnderUiScene(scene, xOffset: 2f);
        PresentationOverlayLanePacer.LaneRefreshPlan deferredBarPlan = pacer.BuildPlan(scene);
        Assert.That(deferredBarPlan.ShouldRefresh(PresentationOverlayItemKind.Bar), Is.True);
        Assert.That(deferredBarPlan.ShouldRefresh(PresentationOverlayItemKind.Text), Is.False);

        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi, deferredBarPlan);
        pacer.MarkPresented(scene, deferredBarPlan);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        Assert.That(CountOpaquePixels(bitmap, 8, 8, 88, 18), Is.GreaterThan(0),
            "Deferred bar lane should still draw from the retained picture while text lane refreshes.");
    }

    [Test]
    public void SkiaOverlayRenderer_ReusesLargeLanePicture_ForUniformPanWithoutPacer()
    {
        var scene = new PresentationOverlayScene(256);
        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));

        BuildLargeUnderUiScene(scene, xOffset: 0f);
        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0),
            "Initial large-lane frame should stay on direct current-frame rendering.");

        BuildLargeUnderUiScene(scene, xOffset: 2f);
        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(2),
            "First uniform-pan frame should materialize reusable lane pictures.");

        BuildLargeUnderUiScene(scene, xOffset: 4f);
        surface.Canvas.Clear(SKColors.Transparent);
        renderer.ResetFrameStats();
        renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi);
        Assert.That(renderer.RebuiltLaneCountLastFrame, Is.EqualTo(0),
            "Subsequent uniform-pan frames should reuse the retained pictures with translation only.");
    }

    [Test]
    public void SkiaOverlayRenderer_DoesNotCrash_WhenBarSpriteCacheTurnsOverWithinSingleFrame()
    {
        var scene = new PresentationOverlayScene(4096);
        scene.BeginBuild();
        for (int i = 0; i < 2400; i++)
        {
            scene.TryAddBar(
                PresentationOverlayLayer.UnderUi,
                x: 8f + ((i % 40) * 6f),
                y: 8f + ((i / 40) * 3f),
                width: 20f,
                height: 2f,
                value: i / 2399f,
                background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
                foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
                stableId: 5000 + i,
                dirtySerial: 9000 + i);
        }
        scene.EndBuild();

        using var renderer = new SkiaOverlayRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(512, 256));

        Assert.DoesNotThrow(() => renderer.Render(scene, surface.Canvas, PresentationOverlayLayer.UnderUi));
    }

    private static void SeedLegacyOverlay(ScreenHudBatchBuffer screenHud, ScreenOverlayBuffer overlayBuffer, string topText)
    {
        screenHud.TryAdd(new ScreenHudItem
        {
            StableId = 11,
            DirtySerial = 111,
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
            StableId = 22,
            DirtySerial = 222,
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

    private static string GetSingleTopMostText(PresentationOverlayScene scene)
    {
        ReadOnlySpan<PresentationOverlayItem> span = scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Text);
        Assert.That(span.Length, Is.EqualTo(1));
        return span[0].Text ?? string.Empty;
    }

    private static void BuildLargeUnderUiScene(PresentationOverlayScene scene, float xOffset)
    {
        scene.BeginBuild();
        for (int i = 0; i < 64; i++)
        {
            scene.TryAddBar(
                PresentationOverlayLayer.UnderUi,
                x: 8f + xOffset,
                y: 8f + (i * 3f),
                width: 80f,
                height: 2f,
                value: 0.5f,
                background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
                foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
                stableId: 1000 + i,
                dirtySerial: 2000 + i);
            scene.TryAddText(
                PresentationOverlayLayer.UnderUi,
                x: 96f + xOffset,
                y: 4f + (i * 3f),
                text: $"{100 + i}",
                fontSize: 12,
                color: new Vector4(1f, 1f, 1f, 1f),
                stableId: 3000 + i,
                dirtySerial: 4000 + i);
        }

        scene.EndBuild();
    }

    private static void BuildLargeUnderUiSceneWithTextValueOffset(PresentationOverlayScene scene, float xOffset, int valueOffset)
    {
        scene.BeginBuild();
        for (int i = 0; i < 64; i++)
        {
            scene.TryAddBar(
                PresentationOverlayLayer.UnderUi,
                x: 8f + xOffset,
                y: 8f + (i * 3f),
                width: 80f,
                height: 2f,
                value: 0.5f,
                background: new Vector4(0.15f, 0.15f, 0.15f, 1f),
                foreground: new Vector4(0.2f, 0.8f, 0.2f, 1f),
                stableId: 1000 + i,
                dirtySerial: 2000 + i);
            scene.TryAddText(
                PresentationOverlayLayer.UnderUi,
                x: 96f + xOffset,
                y: 4f + (i * 3f),
                text: $"{100 + i + valueOffset}",
                fontSize: 12,
                color: new Vector4(1f, 1f, 1f, 1f),
                stableId: 3000 + i,
                dirtySerial: 4000 + i + valueOffset);
        }

        scene.EndBuild();
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

    private static int CountPixelsNear(SKBitmap bitmap, SKColor target, int left, int top, int right, int bottom, int tolerance)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.Red - target.Red) <= tolerance &&
                    Math.Abs(pixel.Green - target.Green) <= tolerance &&
                    Math.Abs(pixel.Blue - target.Blue) <= tolerance &&
                    Math.Abs(pixel.Alpha - target.Alpha) <= tolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsPixelNear(SKColor pixel, SKColor target, int tolerance)
    {
        return Math.Abs(pixel.Red - target.Red) <= tolerance &&
            Math.Abs(pixel.Green - target.Green) <= tolerance &&
            Math.Abs(pixel.Blue - target.Blue) <= tolerance &&
            Math.Abs(pixel.Alpha - target.Alpha) <= tolerance;
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

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
