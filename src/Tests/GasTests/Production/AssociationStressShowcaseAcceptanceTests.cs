using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using AssociationStressShowcaseMod;
using AssociationStressShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class AssociationStressShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.AssociationStress.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "AssociationStressShowcaseMod",
    };

    [Test]
    public void AssociationStressShowcase_ScalesAssociationsAndMaintainsZeroAllocAfterWarmup()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "association-stress-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(128);
        var evidence = new List<UiAcceptanceEvidenceFrame>();
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(AssociationStressIds.MapId);
        Tick(engine, 8, frameTimesMs);

        var uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        AssociationStressShowcaseRuntime runtime = ResolveRuntime(engine);
        AssociationStressSnapshot small = runtime.Snapshot;
        Assert.That(small.AssociationCount, Is.GreaterThan(0));
        Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Does.Contain("Entity Association Core"));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "small_scale"));

        TestInputBackend input = GetInputBackend(engine);
        PressButton(engine, input, "<Keyboard>/RightBracket", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/RightBracket", frameTimesMs);
        Tick(engine, 12, frameTimesMs);

        AssociationStressSnapshot large = runtime.Snapshot;
        Assert.That(large.AssociationCount, Is.GreaterThan(small.AssociationCount));
        Assert.That(large.KnowledgeCapacity, Is.GreaterThanOrEqualTo(large.ActiveKnowledgeCount));
        Assert.That(large.KnowledgeCapacity, Is.LessThanOrEqualTo(large.AssociationCount * 2));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "large_scale"));

        for (int i = 0; i < 24; i++)
        {
            runtime.AdvanceSimulationOnly();
        }

        int capacityBefore = runtime.Snapshot.KnowledgeCapacity;
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            runtime.AdvanceSimulationOnly();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        AssociationStressSnapshot afterHotPath = runtime.Snapshot;
        Assert.That(allocated, Is.EqualTo(0), "Association stress core stores must allocate 0 bytes after warmup.");
        Assert.That(afterHotPath.LastFrameAllocatedBytes, Is.EqualTo(0));
        Assert.That(afterHotPath.KnowledgeCapacity, Is.EqualTo(capacityBefore));

        runtime.Compact(engine);
        Tick(engine, 2, frameTimesMs);
        AssociationStressSnapshot compacted = runtime.Snapshot;
        Assert.That(compacted.KnowledgeCapacity, Is.LessThanOrEqualTo(capacityBefore));
        evidence.Add(Capture(uiRoot, screensDir, evidence.Count + 1, "compacted"));

        WriteTrace(artifactDir, small, large, afterHotPath, compacted, allocated, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Association Stress Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("association-stress-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);

        AcceptanceUiHostInstaller.Install(engine);
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

    private static AssociationStressShowcaseRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(AssociationStressIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is AssociationStressShowcaseRuntime runtime
            ? runtime
            : throw new InvalidOperationException("AssociationStressShowcaseRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Association stress input backend missing.");
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
            who: "Player steering the association stress showcase",
            what: "Scale associations, inspect zero allocation and bounded capacity telemetry.",
            where: AssociationStressIds.MapId,
            why: "Verify AAC-2 shared sparse SoA base is visible as a playable capability.",
            how: "Load the real mod, drive input, snapshot UIRoot, and record runtime telemetry.");
    }

    private static void WriteTrace(
        string artifactDir,
        AssociationStressSnapshot small,
        AssociationStressSnapshot large,
        AssociationStressSnapshot afterHotPath,
        AssociationStressSnapshot compacted,
        long allocated,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var traceLines = new[]
        {
            JsonSerializer.Serialize(new { step = "small", snapshot = small }, options),
            JsonSerializer.Serialize(new { step = "large", snapshot = large }, options),
            JsonSerializer.Serialize(new { step = "hotpath", allocatedBytes = allocated, snapshot = afterHotPath }, options),
            JsonSerializer.Serialize(new { step = "compact", snapshot = compacted }, options),
        };
        File.WriteAllLines(Path.Combine(artifactDir, "trace.jsonl"), traceLines);

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Association Stress Showcase Acceptance",
                string.Empty,
                $"- small: {small.AssociationCount:N0} associations, capacity {small.KnowledgeCapacity:N0}",
                $"- large: {large.AssociationCount:N0} associations, capacity {large.KnowledgeCapacity:N0}",
                $"- hotpath allocated bytes: {allocated:N0}",
                $"- compacted capacity: {compacted.KnowledgeCapacity:N0}",
                $"- sampled frames: {frameTimesMs.Count}",
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
