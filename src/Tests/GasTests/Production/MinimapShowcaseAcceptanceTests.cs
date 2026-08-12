using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class MinimapShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "minimap_showcase";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FourXDemoMod",
        "MinimapShowcaseMod",
    };

    private sealed record MarkerView(
        int StableId,
        float WorldXcm,
        float WorldYcm,
        float NormalizedX,
        float NormalizedY,
        string Color,
        float SizePx,
        float OrientationRad,
        float OrientationLengthPx,
        uint Flags,
        MinimapKnowledgeState KnowledgeState);

    private sealed record SnapshotView(
        string MapId,
        MinimapZoomBand ZoomBand,
        MinimapPreset Preset,
        float CenterXcm,
        float CenterYcm,
        float HalfExtentCm,
        float MinWorldXcm,
        float MinWorldYcm,
        float MaxWorldXcm,
        float MaxWorldYcm,
        float CameraTargetXcm,
        float CameraTargetYcm,
        int MarkerCount,
        int VisibleMarkerCount,
        IReadOnlyList<MarkerView> VisibleMarkers);

    [Test]
    public void MinimapShowcase_WritesMarkerOnlyAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "minimap-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(screensDir);

        var timeline = new List<string>();
        var traces = new List<object>();
        var frameTimesMs = new List<double>();

        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        MinimapRuntime runtime = ResolveMinimapRuntime(engine);
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

        SeedAuthoredMarkers(markers);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();
        runtime.Refresh(engine, markers, screenMarkers);

        SnapshotView rts = MapSnapshot(runtime.CaptureDebugSnapshot());
        Assert.That(rts.Preset, Is.EqualTo(MinimapPreset.RtsFullMap));
        Assert.That(rts.MarkerCount, Is.EqualTo(20));
        Assert.That(rts.VisibleMarkerCount, Is.EqualTo(20));
        WriteSnapshotSvg(rts, Path.Combine(screensDir, "001_rts_marker_overview.svg"));
        timeline.Add("[T+001] RTS preset draws all authored performer markers directly from the core marker buffer.");
        traces.Add(new
        {
            step = "001_rts_marker_overview",
            preset = rts.Preset.ToString(),
            band = rts.ZoomBand.ToString(),
            visible_markers = rts.VisibleMarkerCount,
            screenshot = "screens/001_rts_marker_overview.svg"
        });

        runtime.UseFollowCameraPreset(13000f, rotateWithCamera: false);
        runtime.Refresh(engine, markers, screenMarkers);
        SnapshotView camera = MapSnapshot(runtime.CaptureDebugSnapshot());
        Assert.That(camera.Preset, Is.EqualTo(MinimapPreset.FollowCamera));
        Assert.That(camera.VisibleMarkerCount, Is.GreaterThan(0));
        Assert.That(camera.VisibleMarkerCount, Is.LessThan(rts.VisibleMarkerCount));
        WriteSnapshotSvg(camera, Path.Combine(screensDir, "002_camera_marker_window.svg"));
        timeline.Add("[T+002] Follow-camera preset keeps the camera target centered and clips markers outside the local window.");
        traces.Add(new
        {
            step = "002_camera_marker_window",
            preset = camera.Preset.ToString(),
            band = camera.ZoomBand.ToString(),
            visible_markers = camera.VisibleMarkerCount,
            screenshot = "screens/002_camera_marker_window.svg"
        });

        runtime.ApplyWheelZoom(1f);
        runtime.Refresh(engine, markers, screenMarkers);
        SnapshotView zoomed = MapSnapshot(runtime.CaptureDebugSnapshot());
        SelectZoomStableMarkerPair(camera, zoomed, out float beforeDistance, out float afterDistance);
        Assert.That(afterDistance, Is.GreaterThan(beforeDistance * 1.05f));
        WriteSnapshotSvg(zoomed, Path.Combine(screensDir, "003_camera_marker_zoom.svg"));
        timeline.Add("[T+003] Zooming in increases screen-space distance between the same authored markers.");
        traces.Add(new
        {
            step = "003_camera_marker_zoom",
            preset = zoomed.Preset.ToString(),
            band = zoomed.ZoomBand.ToString(),
            visible_markers = zoomed.VisibleMarkerCount,
            screenshot = "screens/003_camera_marker_zoom.svg"
        });

        File.WriteAllText(
            Path.Combine(artifactDir, "trace.jsonl"),
            string.Join(Environment.NewLine, traces.Select(trace => JsonSerializer.Serialize(trace))));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, rts, camera, zoomed, frameTimesMs));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
    }

    [Test]
    public void CoreMinimapRuntime_RefreshAndRender_StayWithinZeroAllocBudget()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        MinimapRuntime runtime = ResolveMinimapRuntime(engine);
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

        SeedAuthoredMarkers(markers);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();
        for (int i = 0; i < 24; i++)
        {
            overlay.Clear();
            runtime.Refresh(engine, markers, screenMarkers);
            runtime.Render(overlay);
            Tick(engine, 1, frameTimesMs);
            SeedAuthoredMarkers(markers);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 96; i++)
        {
            overlay.Clear();
            runtime.Refresh(engine, markers, screenMarkers);
            runtime.Render(overlay);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocatedBytes, Is.LessThanOrEqualTo(2048L), $"Expected zero-allocation SoA hot path budget, got {allocatedBytes} bytes.");
    }

    [Test]
    public void CoreMinimapRuntime_Render_SubmitsCameraFrustumAsLineOverlay()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        MinimapRuntime runtime = ResolveMinimapRuntime(engine);
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

        SeedAuthoredMarkers(markers);
        runtime.Visible = true;
        runtime.UseRtsFullMapPreset();
        overlay.Clear();
        runtime.Refresh(engine, markers, screenMarkers);
        runtime.Render(overlay);

        int lineCount = 0;
        int thickLineCount = 0;
        foreach (ref readonly ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Line)
            {
                continue;
            }

            lineCount++;
            if (item.Thickness >= 3)
            {
                thickLineCount++;
            }

            Assert.That(item.X, Is.InRange(runtime.FieldX, runtime.FieldX + runtime.FieldSize - 1));
            Assert.That(item.Y, Is.InRange(runtime.FieldY, runtime.FieldY + runtime.FieldSize - 1));
            Assert.That(item.Width, Is.InRange(runtime.FieldX, runtime.FieldX + runtime.FieldSize - 1));
            Assert.That(item.Height, Is.InRange(runtime.FieldY, runtime.FieldY + runtime.FieldSize - 1));
        }

        Assert.That(lineCount, Is.GreaterThanOrEqualTo(8), "Camera frustum should submit shadow and foreground line segments.");
        Assert.That(thickLineCount, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void CoreMinimapRuntime_ProjectsConfigDrivenMarkerOrientationIntoScreenSpace()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        MinimapRuntime runtime = ResolveMinimapRuntime(engine);
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

        markers.BeginFrame();
        var color = new Vector4(1f, 0.28f, 0.08f, 1f);
        Assert.That(markers.TryAdd(
            7001,
            0f,
            0f,
            in color,
            12f,
            MinimapMarkerFlags.HasOrientation,
            0f,
            22f), Is.True);

        runtime.Visible = true;
        runtime.UseFollowCameraPreset(7000f, rotateWithCamera: false);
        runtime.Refresh(engine, markers, screenMarkers);

        Assert.That(screenMarkers.Count, Is.EqualTo(1));
        Assert.That((screenMarkers.GetFlags(0) & MinimapMarkerFlags.HasOrientation), Is.Not.EqualTo(0u));
        Assert.That(NormalizeSignedRadians(screenMarkers.GetOrientationRad(0)), Is.EqualTo(0f).Within(0.001f));
        Assert.That(screenMarkers.GetOrientationLengthPx(0), Is.EqualTo(22f));

        engine.AuthorityCamera().State.Yaw = 90f;
        runtime.SetRotateWithCamera(true);
        runtime.Refresh(engine, markers, screenMarkers);

        Assert.That(screenMarkers.Count, Is.EqualTo(1));
        Assert.That(NormalizeSignedRadians(screenMarkers.GetOrientationRad(0)), Is.EqualTo(MathF.PI * 0.5f).Within(0.001f));
    }

    [Test]
    public void CoreMinimapRuntime_ZoomUpdatesMetricGridStepAndRotationBasis()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        MinimapRuntime runtime = ResolveMinimapRuntime(engine);
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

        SeedAuthoredMarkers(markers);
        runtime.Visible = true;
        runtime.UseFollowCameraPreset(22000f, rotateWithCamera: false);
        runtime.SetRotateWithCamera(false);
        overlay.Clear();
        runtime.Refresh(engine, markers, screenMarkers);
        runtime.Render(overlay);
        float beforeStep = runtime.MetricGridStepCm;

        Vector2 centerScreen = new(runtime.FieldX + (runtime.FieldSize * 0.5f), runtime.FieldY + (runtime.FieldSize * 0.5f));
        runtime.ApplyWheelZoom(2f, centerScreen);
        overlay.Clear();
        runtime.Refresh(engine, markers, screenMarkers);
        runtime.Render(overlay);
        float afterStep = runtime.MetricGridStepCm;
        Assert.That(afterStep, Is.LessThanOrEqualTo(beforeStep), "Metric grid spacing must become finer or remain at a snap boundary after zoom-in.");

        Vector2 rightScreen = new(runtime.FieldX + (runtime.FieldSize * 0.95f), runtime.FieldY + (runtime.FieldSize * 0.5f));
        Assert.That(runtime.TryScreenToWorld(rightScreen, out Vector2 northUpWorld), Is.True);
        engine.AuthorityCamera().State.Yaw = 90f;
        runtime.SetRotateWithCamera(true);
        runtime.Refresh(engine, markers, screenMarkers);
        Assert.That(runtime.TryScreenToWorld(rightScreen, out Vector2 rotatedWorld), Is.True);

        Assert.That(Vector2.Distance(rotatedWorld, northUpWorld), Is.GreaterThan(10f));
        Assert.That(runtime.RotateWithCamera, Is.True);
    }

    private static void SeedAuthoredMarkers(MinimapMarkerBuffer markers)
    {
        markers.BeginFrame();
        for (int i = 0; i < 20; i++)
        {
            int column = i % 5;
            int row = i / 5;
            float x = -14000f + (column * 7000f);
            float y = -10500f + (row * 7000f);
            var color = new Vector4(0.12f, 0.82f, 1f, 1f);
            if (i % 5 == 0)
            {
                color = new Vector4(1f, 0.72f, 0.18f, 1f);
            }

            float facingRad = MathF.Atan2(y, x);
            Assert.That(markers.TryAdd(
                1000 + i,
                x,
                y,
                in color,
                7f,
                MinimapMarkerFlags.HasOrientation,
                facingRad,
                16f), Is.True);
        }
    }

    private static string BuildBattleReport(
        IReadOnlyList<string> timeline,
        SnapshotView rts,
        SnapshotView camera,
        SnapshotView zoomed,
        IReadOnlyList<double> frameTimesMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario: minimap-showcase");
        sb.AppendLine();
        sb.AppendLine("## Header");
        sb.AppendLine("- build: GasTests / MinimapShowcase_WritesMarkerOnlyAcceptanceArtifacts");
        sb.AppendLine("- map: minimap_showcase");
        sb.AppendLine("- source: core MinimapMarkerBuffer");
        sb.AppendLine("- screenshots: `screens/001_rts_marker_overview.svg`, `screens/002_camera_marker_window.svg`, `screens/003_camera_marker_zoom.svg`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        for (int i = 0; i < timeline.Count; i++)
        {
            sb.AppendLine(timeline[i]);
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- result: success");
        sb.AppendLine("- failure_branch: minimap marker projection failed, camera preset did not clip to local markers, or render hot path exceeded allocation budget");
        sb.AppendLine($"- rts_visible: {rts.VisibleMarkerCount}/{rts.MarkerCount}");
        sb.AppendLine($"- camera_visible: {camera.VisibleMarkerCount}/{camera.MarkerCount}");
        sb.AppendLine($"- zoom_visible: {zoomed.VisibleMarkerCount}/{zoomed.MarkerCount}");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- marker_pool: {rts.MarkerCount}");
        sb.AppendLine($"- median_tick_ms: {Median(frameTimesMs):0.000}");
        sb.AppendLine($"- max_tick_ms: {(frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max()):0.000}");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[\"Seed core MinimapMarkerBuffer\"] --> B[\"RTS preset projects full marker set\"]",
            "    B --> C[\"Camera-centered preset clips to local markers\"]",
            "    C --> D[\"Zoom changes marker screen spacing\"]",
            "    D --> E[\"Write marker-only artifacts\"]"
        });
    }

    private static void WriteSnapshotSvg(SnapshotView snapshot, string path)
    {
        const int width = 1200;
        const int height = 860;
        const int fieldX = 80;
        const int fieldY = 100;
        const int fieldSize = 620;

        var shapes = new List<string>
        {
            $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#091018\" />",
            $"<rect x=\"40\" y=\"40\" width=\"1120\" height=\"780\" rx=\"18\" fill=\"#12202d\" stroke=\"#4d728a\" stroke-width=\"2\" />",
            $"<rect x=\"{fieldX}\" y=\"{fieldY}\" width=\"{fieldSize}\" height=\"{fieldSize}\" rx=\"10\" fill=\"#08141d\" stroke=\"#365264\" stroke-width=\"2\" />"
        };

        int gridStep = fieldSize / 4;
        for (int i = 1; i < 4; i++)
        {
            int offset = gridStep * i;
            shapes.Add($"<line x1=\"{fieldX + offset}\" y1=\"{fieldY}\" x2=\"{fieldX + offset}\" y2=\"{fieldY + fieldSize}\" stroke=\"#274051\" stroke-width=\"1\" />");
            shapes.Add($"<line x1=\"{fieldX}\" y1=\"{fieldY + offset}\" x2=\"{fieldX + fieldSize}\" y2=\"{fieldY + offset}\" stroke=\"#274051\" stroke-width=\"1\" />");
        }

        foreach (MarkerView marker in snapshot.VisibleMarkers)
        {
            int x = fieldX + (int)MathF.Round(marker.NormalizedX * fieldSize);
            int y = fieldY + (int)MathF.Round((1f - marker.NormalizedY) * fieldSize);
            int radius = Math.Max(3, (int)MathF.Round(marker.SizePx * 0.75f));
            shapes.Add($"<circle cx=\"{x}\" cy=\"{y}\" r=\"{radius + 2}\" fill=\"#021017\" stroke=\"#092b38\" stroke-width=\"1\" />");
            shapes.Add($"<circle cx=\"{x}\" cy=\"{y}\" r=\"{radius}\" fill=\"{marker.Color}\" />");
            if ((marker.Flags & MinimapMarkerFlags.HasOrientation) != 0u && marker.OrientationLengthPx > 0f)
            {
                int endX = x + (int)MathF.Round(MathF.Cos(marker.OrientationRad) * marker.OrientationLengthPx);
                int endY = y + (int)MathF.Round(MathF.Sin(marker.OrientationRad) * marker.OrientationLengthPx);
                shapes.Add($"<line x1=\"{x}\" y1=\"{y}\" x2=\"{endX}\" y2=\"{endY}\" stroke=\"#020608\" stroke-width=\"5\" stroke-linecap=\"round\" />");
                shapes.Add($"<line x1=\"{x}\" y1=\"{y}\" x2=\"{endX}\" y2=\"{endY}\" stroke=\"{marker.Color}\" stroke-width=\"3\" stroke-linecap=\"round\" />");
            }
        }

        float cameraNormalizedX = (snapshot.CameraTargetXcm - (snapshot.CenterXcm - snapshot.HalfExtentCm)) / MathF.Max(1f, snapshot.HalfExtentCm * 2f);
        float cameraNormalizedY = (snapshot.CameraTargetYcm - (snapshot.CenterYcm - snapshot.HalfExtentCm)) / MathF.Max(1f, snapshot.HalfExtentCm * 2f);
        int cameraX = fieldX + (int)MathF.Round(Math.Clamp(cameraNormalizedX, 0f, 1f) * fieldSize);
        int cameraY = fieldY + (int)MathF.Round((1f - Math.Clamp(cameraNormalizedY, 0f, 1f)) * fieldSize);
        shapes.Add($"<rect x=\"{cameraX - 18}\" y=\"{cameraY - 18}\" width=\"36\" height=\"36\" fill=\"none\" stroke=\"#ffd95c\" stroke-width=\"4\" />");
        shapes.Add($"<line x1=\"{cameraX - 16}\" y1=\"{cameraY}\" x2=\"{cameraX + 16}\" y2=\"{cameraY}\" stroke=\"#fff3a0\" stroke-width=\"4\" />");
        shapes.Add($"<line x1=\"{cameraX}\" y1=\"{cameraY - 16}\" x2=\"{cameraX}\" y2=\"{cameraY + 16}\" stroke=\"#fff3a0\" stroke-width=\"4\" />");

        string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}">
  {{string.Join(Environment.NewLine + "  ", shapes)}}
  <text x="760" y="120" fill="#f7fafc" font-size="34" font-family="Consolas, monospace">Core Marker Minimap</text>
  <text x="760" y="160" fill="#f6d56e" font-size="24" font-family="Consolas, monospace">Preset: {{snapshot.Preset}}</text>
  <text x="760" y="204" fill="#dde8f2" font-size="22" font-family="Consolas, monospace">Band: {{snapshot.ZoomBand}}</text>
  <text x="760" y="248" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Markers: {{snapshot.VisibleMarkerCount}}/{{snapshot.MarkerCount}}</text>
  <text x="760" y="280" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Viewport: center=({{snapshot.CenterXcm:0}}, {{snapshot.CenterYcm:0}}) extent={{snapshot.HalfExtentCm:0}}</text>
  <text x="760" y="330" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Source</text>
  <text x="760" y="360" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Authored performer marker buffer</text>
  <text x="760" y="390" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">No Name/Team/MapEntity scan</text>
  <text x="760" y="440" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Bounds</text>
  <text x="760" y="470" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">min=({{snapshot.MinWorldXcm:0}}, {{snapshot.MinWorldYcm:0}})</text>
  <text x="760" y="500" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">max=({{snapshot.MaxWorldXcm:0}}, {{snapshot.MaxWorldYcm:0}})</text>
</svg>
""";
        File.WriteAllText(path, svg);
    }

    private static MinimapRuntime ResolveMinimapRuntime(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("Core MinimapRuntime missing.");
    }

    private static SnapshotView MapSnapshot(MinimapDebugSnapshot snapshot)
    {
        var markers = new List<MarkerView>(snapshot.VisibleMarkers.Count);
        foreach (MinimapDebugMarker marker in snapshot.VisibleMarkers)
        {
            markers.Add(new MarkerView(
                marker.StableId,
                marker.WorldXcm,
                marker.WorldYcm,
                marker.NormalizedX,
                marker.NormalizedY,
                ToSvgColor(marker.Color),
                marker.SizePx,
                marker.OrientationRad,
                marker.OrientationLengthPx,
                marker.Flags,
                marker.KnowledgeState));
        }

        return new SnapshotView(
            snapshot.MapId,
            snapshot.ZoomBand,
            snapshot.Preset,
            snapshot.CenterXcm,
            snapshot.CenterYcm,
            snapshot.HalfExtentCm,
            snapshot.MinWorldXcm,
            snapshot.MinWorldYcm,
            snapshot.MaxWorldXcm,
            snapshot.MaxWorldYcm,
            snapshot.CameraTargetXcm,
            snapshot.CameraTargetYcm,
            snapshot.MarkerCount,
            snapshot.VisibleMarkerCount,
            markers);
    }

    private static void SelectZoomStableMarkerPair(SnapshotView before, SnapshotView after, out float beforeDistance, out float afterDistance)
    {
        beforeDistance = 0f;
        afterDistance = 0f;
        for (int i = 0; i < before.VisibleMarkers.Count; i++)
        {
            int stableIdA = before.VisibleMarkers[i].StableId;
            if (!after.VisibleMarkers.Any(marker => marker.StableId == stableIdA))
            {
                continue;
            }

            for (int j = i + 1; j < before.VisibleMarkers.Count; j++)
            {
                int stableIdB = before.VisibleMarkers[j].StableId;
                if (!after.VisibleMarkers.Any(marker => marker.StableId == stableIdB))
                {
                    continue;
                }

                float candidateBefore = DistanceBetweenMarkers(before, stableIdA, stableIdB);
                if (candidateBefore <= beforeDistance)
                {
                    continue;
                }

                beforeDistance = candidateBefore;
                afterDistance = DistanceBetweenMarkers(after, stableIdA, stableIdB);
            }
        }

        Assert.That(
            beforeDistance,
            Is.GreaterThan(0f),
            "Expected at least one stable marker pair to remain visible after zoom. " +
            $"beforeVisible={before.VisibleMarkerCount}/{before.MarkerCount} " +
            $"afterVisible={after.VisibleMarkerCount}/{after.MarkerCount} " +
            $"beforeExtent={before.HalfExtentCm:0.###} afterExtent={after.HalfExtentCm:0.###} " +
            $"beforeIds=[{string.Join(",", before.VisibleMarkers.Select(marker => marker.StableId))}] " +
            $"afterIds=[{string.Join(",", after.VisibleMarkers.Select(marker => marker.StableId))}]");
    }

    private static void InstallKnowledgeServices(
        GameEngine engine,
        KnowledgeProjectionStore store,
        KnowledgeRelationCollectionProjector? projector)
    {
        engine.SetService(CoreServiceKeys.KnowledgeProjectionStore, store);

        if (projector != null)
        {
            engine.SetService(CoreServiceKeys.KnowledgeRelationCollectionProjector, projector);
        }

        engine.SetService(CoreServiceKeys.KnowledgeProjectionResolver, new KnowledgeProjectionResolver(store, projector));
    }

    private static void SeedKnowledgeMarkers(MinimapMarkerBuffer markers, params Entity[] owners)
    {
        markers.BeginFrame();
        var color = new Vector4(0.12f, 0.82f, 1f, 1f);
        for (int i = 0; i < owners.Length; i++)
        {
            Assert.That(markers.TryAdd(
                8001 + i,
                owners[i],
                1000f * (i + 1),
                0f,
                in color,
                8f), Is.True);
        }
    }

    private static KnowledgeDisclosureRecord CreateKnowledgeRecord(
        KnowledgePresence presence,
        KnowledgePositionAccess position,
        Entity source,
        int expiryTick = 0)
    {
        return new KnowledgeDisclosureRecord(
            presence,
            position,
            KnowledgeIdMask256.Empty.WithId(1),
            KnowledgeIdMask256.Empty.WithId(2),
            KnowledgeIdMask256.Empty,
            source,
            observedTick: 1,
            expiryTick,
            confidencePermille: 900,
            revision: 0);
    }

    private static int CountState(SnapshotView snapshot, MinimapKnowledgeState state)
    {
        int count = 0;
        for (int i = 0; i < snapshot.VisibleMarkers.Count; i++)
        {
            if (snapshot.VisibleMarkers[i].KnowledgeState == state)
            {
                count++;
            }
        }

        return count;
    }

    private static MarkerView FindMarkerByWorldX(SnapshotView snapshot, float worldXcm)
    {
        for (int i = 0; i < snapshot.VisibleMarkers.Count; i++)
        {
            if (MathF.Abs(snapshot.VisibleMarkers[i].WorldXcm - worldXcm) <= 0.001f)
            {
                return snapshot.VisibleMarkers[i];
            }
        }

        throw new InvalidOperationException($"Marker at worldX={worldXcm} was not visible.");
    }

    private static float DistanceBetweenMarkers(SnapshotView snapshot, int stableIdA, int stableIdB)
    {
        MarkerView a = snapshot.VisibleMarkers.First(marker => marker.StableId == stableIdA);
        MarkerView b = snapshot.VisibleMarkers.First(marker => marker.StableId == stableIdB);
        float dx = b.NormalizedX - a.NormalizedX;
        float dy = b.NormalizedY - a.NormalizedY;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static string ToSvgColor(Vector4 color)
    {
        int r = ToByte(color.X);
        int g = ToByte(color.Y);
        int b = ToByte(color.Z);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static int ToByte(float value)
    {
        return (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var backend = new TestInputBackend();
        engine.SetService(CoreServiceKeys.InputBackend, backend);
        engine.GlobalContext["Tests.MinimapShowcase.InputBackend"] = backend;
    }

    private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
    {
        engine.LoadMap(mapId);
        engine.Start();
        Tick(engine, 3, frameTimesMs);
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        var backend = engine.GlobalContext["Tests.MinimapShowcase.InputBackend"] as TestInputBackend;
        for (int i = 0; i < frames; i++)
        {
            backend?.SetMouseWheel(0f);

            long start = Stopwatch.GetTimestamp();
            engine.Tick(DeltaTime);
            long end = Stopwatch.GetTimestamp();
            frameTimesMs.Add((end - start) * 1000d / Stopwatch.Frequency);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) * 0.5d
            : ordered[middle];
    }

    private static float NormalizeSignedRadians(float radians)
    {
        float twoPi = MathF.PI * 2f;
        radians %= twoPi;
        if (radians > MathF.PI)
        {
            radians -= twoPi;
        }
        else if (radians < -MathF.PI)
        {
            radians += twoPi;
        }

        return radians;
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition;
        private float _mouseWheel;

        public void SetMouseWheel(float wheel) => _mouseWheel = wheel;

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
        public Vector2 GetMousePosition() => _mousePosition;
        public float GetMouseWheel() => _mouseWheel;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class StubViewController : IViewController
    {
        public StubViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 60f;
        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }
}
