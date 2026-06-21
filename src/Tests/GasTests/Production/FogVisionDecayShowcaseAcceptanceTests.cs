using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using FogVisionDecayShowcaseMod;
using FogVisionDecayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.Tests;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class FogVisionDecayShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.FogVisionDecay.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FogVisionDecayShowcaseMod",
    };

    [Test]
    public void FogVisionDecayShowcase_DecaysLiveKnowledgeAndKeepsStoreBounded()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "fog-vision-decay-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);
        foreach (string svg in Directory.GetFiles(screensDir, "*.svg"))
        {
            File.Delete(svg);
        }

        var frameTimesMs = new List<double>(160);
        var evidence = new List<UiAcceptanceEvidenceFrame>();
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(FogVisionDecayIds.MapId);
        Tick(engine, 8, frameTimesMs);

        var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        FogVisionDecayShowcaseRuntime runtime = ResolveRuntime(engine);
        FogVisionDecaySnapshot initial = runtime.Snapshot;
        Assert.That(initial.LiveCount, Is.GreaterThan(0));
        Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Does.Contain("Fog Vision Decay"));
        WriteMinimapSnapshotSvg(CaptureMinimap(engine), Path.Combine(screensDir, "001_minimap_live_contact.svg"));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "live_contact"));

        Tick(engine, 48, frameTimesMs);
        FogVisionDecaySnapshot decayed = runtime.Snapshot;
        MinimapDebugSnapshot minimap = CaptureMinimap(engine);
        int minimapLive = CountState(minimap, MinimapKnowledgeState.LiveVisible);
        int minimapLastKnown = CountState(minimap, MinimapKnowledgeState.LastKnown);
        Assert.That(decayed.LiveCount, Is.GreaterThan(0));
        Assert.That(decayed.KnownCount, Is.GreaterThan(0));
        Assert.That(decayed.ExpiredCount, Is.GreaterThan(0));
        Assert.That(decayed.RecordCapacity, Is.LessThanOrEqualTo(decayed.ConfiguredCapacityCeiling));
        Assert.That(minimapLive, Is.GreaterThan(0));
        Assert.That(minimapLastKnown, Is.GreaterThan(0));
        WriteMinimapSnapshotSvg(minimap, Path.Combine(screensDir, "002_minimap_live_and_ghosts.svg"));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "known_ghosts"));

        TestInputBackend input = GetInputBackend(engine);
        PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        FogVisionDecaySnapshot paused = runtime.Snapshot;
        Tick(engine, 6, frameTimesMs);
        Assert.That(runtime.Snapshot.Tick, Is.EqualTo(paused.Tick));
        PressButton(engine, input, "<Keyboard>/n", frameTimesMs);
        Assert.That(runtime.Snapshot.Tick, Is.EqualTo(paused.Tick + 1));
        PressButton(engine, input, "<Keyboard>/c", frameTimesMs);
        Tick(engine, 2, frameTimesMs);
        FogVisionDecaySnapshot compacted = runtime.Snapshot;
        Assert.That(compacted.PhysicalRecordCount, Is.LessThanOrEqualTo(decayed.PhysicalRecordCount));
        WriteMinimapSnapshotSvg(CaptureMinimap(engine), Path.Combine(screensDir, "003_minimap_after_compact.svg"));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "paused_step_compact"));

        for (int i = 0; i < 32; i++)
        {
            runtime.ProbeKnowledgeQueryHotPathOnly();
        }

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            runtime.ProbeKnowledgeQueryHotPathOnly();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        FogVisionDecaySnapshot hotPath = runtime.Snapshot;
        Assert.That(allocated, Is.EqualTo(0));
        Assert.That(hotPath.LastFrameAllocatedBytes, Is.EqualTo(0));
        Assert.That(hotPath.RecordCapacity, Is.LessThanOrEqualTo(hotPath.ConfiguredCapacityCeiling));

        WriteTrace(artifactDir, initial, decayed, compacted, hotPath, allocated, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Fog Vision Decay Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("fog-vision-decay-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);

        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new TestInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.GlobalContext[TestInputBackendKey] = backend;
    }

    private static FogVisionDecayShowcaseRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(FogVisionDecayIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is FogVisionDecayShowcaseRuntime runtime
            ? runtime
            : throw new InvalidOperationException("FogVisionDecayShowcaseRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Fog vision decay input backend missing.");
    }

    private static MinimapDebugSnapshot CaptureMinimap(GameEngine engine)
    {
        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("MinimapRuntime missing.");
        MinimapMarkerBuffer markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
        MinimapScreenMarkerBuffer screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
            ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
        minimap.Visible = true;
        minimap.UseRtsFullMapPreset();
        minimap.Refresh(engine, markers, screenMarkers);
        return minimap.CaptureDebugSnapshot();
    }

    private static int CountState(MinimapDebugSnapshot snapshot, MinimapKnowledgeState state)
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

    private static void WriteMinimapSnapshotSvg(MinimapDebugSnapshot snapshot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Missing screenshot directory."));
        const int width = 960;
        const int height = 720;
        const int fieldX = 48;
        const int fieldY = 72;
        const int fieldSize = 560;
        var shapes = new List<string>
        {
            $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#071018\" />",
            $"<rect x=\"{fieldX}\" y=\"{fieldY}\" width=\"{fieldSize}\" height=\"{fieldSize}\" rx=\"8\" fill=\"#0C1722\" stroke=\"#345066\" stroke-width=\"2\" />"
        };

        for (int i = 1; i < 4; i++)
        {
            int offset = fieldSize * i / 4;
            shapes.Add($"<line x1=\"{fieldX + offset}\" y1=\"{fieldY}\" x2=\"{fieldX + offset}\" y2=\"{fieldY + fieldSize}\" stroke=\"#263B4A\" stroke-width=\"1\" />");
            shapes.Add($"<line x1=\"{fieldX}\" y1=\"{fieldY + offset}\" x2=\"{fieldX + fieldSize}\" y2=\"{fieldY + offset}\" stroke=\"#263B4A\" stroke-width=\"1\" />");
        }

        for (int i = 0; i < snapshot.VisibleMarkers.Count; i++)
        {
            MinimapDebugMarker marker = snapshot.VisibleMarkers[i];
            int x = fieldX + (int)MathF.Round(marker.NormalizedX * fieldSize);
            int y = fieldY + (int)MathF.Round((1f - marker.NormalizedY) * fieldSize);
            string color = marker.KnowledgeState switch
            {
                MinimapKnowledgeState.LiveVisible => "#45E58A",
                MinimapKnowledgeState.LastKnown => "#7EA6FF",
                MinimapKnowledgeState.Disclosed => "#FFD36A",
                MinimapKnowledgeState.Known => "#B9C8D8",
                _ => "#566676",
            };
            int radius = marker.KnowledgeState == MinimapKnowledgeState.LiveVisible ? 6 : 4;
            shapes.Add($"<circle cx=\"{x}\" cy=\"{y}\" r=\"{radius + 2}\" fill=\"#02080D\" stroke=\"#081A22\" stroke-width=\"1\" />");
            shapes.Add($"<circle cx=\"{x}\" cy=\"{y}\" r=\"{radius}\" fill=\"{color}\" />");
        }

        string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}">
  {{string.Join(Environment.NewLine + "  ", shapes)}}
  <text x="650" y="106" fill="#F8FBFF" font-size="28" font-family="Consolas, monospace">Fog Vision Decay</text>
  <text x="650" y="150" fill="#45E58A" font-size="18" font-family="Consolas, monospace">Live: {{CountState(snapshot, MinimapKnowledgeState.LiveVisible)}}</text>
  <text x="650" y="180" fill="#7EA6FF" font-size="18" font-family="Consolas, monospace">Last-known: {{CountState(snapshot, MinimapKnowledgeState.LastKnown)}}</text>
  <text x="650" y="210" fill="#A9B8C8" font-size="18" font-family="Consolas, monospace">Visible: {{snapshot.VisibleMarkerCount}}/{{snapshot.MarkerCount}}</text>
</svg>
""";
        File.WriteAllText(path, svg);
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
        }
    }

    private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
    {
        backend.SetButton(path, true);
        Tick(engine, 2, frameTimesMs);
        backend.SetButton(path, false);
        Tick(engine, 2, frameTimesMs);
    }

    private static UiAcceptanceEvidenceFrame Capture(UIRoot uiRoot, string screensDir, int order, string step)
    {
        return AcceptanceUiEvidenceWriter.CaptureFrame(
            uiRoot,
            screensDir,
            order,
            step,
            when: $"T+{order:000}",
            who: "Player steering a scout patrol",
            what: "Watch enemy markers move from live contact to last-known ghost and then expire.",
            where: FogVisionDecayIds.MapId,
            why: "Verify AAC-3 knowledge TTL and compaction are visible in a playable 4X fog scenario.",
            how: "Load the real mod, drive input, snapshot UIRoot, and record minimap/runtime telemetry.");
    }

    private static void WriteTrace(
        string artifactDir,
        FogVisionDecaySnapshot initial,
        FogVisionDecaySnapshot decayed,
        FogVisionDecaySnapshot compacted,
        FogVisionDecaySnapshot hotPath,
        long allocated,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "initial", snapshot = initial }, options),
                JsonSerializer.Serialize(new { step = "decayed", snapshot = decayed }, options),
                JsonSerializer.Serialize(new { step = "compacted", snapshot = compacted }, options),
                JsonSerializer.Serialize(new { step = "hotpath", allocatedBytes = allocated, snapshot = hotPath }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Fog Vision Decay Showcase Acceptance",
                string.Empty,
                $"- initial live contacts: {initial.LiveCount:N0}",
                $"- decayed live/known/expired: {decayed.LiveCount:N0}/{decayed.KnownCount:N0}/{decayed.ExpiredCount:N0}",
                $"- compacted physical/capacity: {compacted.PhysicalRecordCount:N0}/{compacted.RecordCapacity:N0}",
                $"- hotpath allocated bytes: {allocated:N0}",
                $"- sampled frames: {frameTimesMs.Count:N0}",
            });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition = new(-1f, -1f);

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => _mousePosition;

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool down)
        {
            if (down)
            {
                _buttons.Add(path);
            }
            else
            {
                _buttons.Remove(path);
            }
        }

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }
}
