using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;
using OwnershipCascadeShowcaseMod;
using OwnershipCascadeShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class OwnershipCascadeShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.OwnershipCascade.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "OwnershipCascadeShowcaseMod",
    };

    [Test]
    public void OwnershipCascadeShowcase_CapturesAndReclaimsOwnsChain()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "ownership-cascade-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(64);
        var evidence = new List<UiAcceptanceEvidenceFrame>(3);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(OwnershipCascadeIds.ShowcaseMapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        OwnershipCascadeRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        OwnershipCascadeSnapshot initial = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(initial.CityOwner, Is.EqualTo("Ember Pact"));
            Assert.That(initial.GarrisonOwner, Is.EqualTo("Ember Pact"));
            Assert.That(initial.WarehouseOwner, Is.EqualTo("Ember Pact"));
            Assert.That(initial.ProductionOwner, Is.EqualTo("Ember Pact"));
            Assert.That(initial.CityIncomingCount, Is.EqualTo(1));
            Assert.That(initial.GarrisonIncomingCount, Is.EqualTo(1));
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Does.Contain("Ownership Cascade Showcase"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 1, "initial-enemy-owned"));

        PressButton(engine, input, "<Keyboard>/c", frameTimesMs);
        OwnershipCascadeSnapshot captured = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(captured.CityOwner, Is.EqualTo("Azure Concord"));
            Assert.That(captured.GarrisonOwner, Is.EqualTo("Azure Concord"));
            Assert.That(captured.WarehouseOwner, Is.EqualTo("Azure Concord"));
            Assert.That(captured.ProductionOwner, Is.EqualTo("Azure Concord"));
            Assert.That(captured.Status, Does.Contain("Captured"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 2, "captured"));

        PressButton(engine, input, "<Keyboard>/r", frameTimesMs);
        OwnershipCascadeSnapshot reclaimed = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(reclaimed.CityOwner, Is.EqualTo("Ember Pact"));
            Assert.That(reclaimed.GarrisonOwner, Is.EqualTo("Ember Pact"));
            Assert.That(reclaimed.WarehouseOwner, Is.EqualTo("Ember Pact"));
            Assert.That(reclaimed.ProductionOwner, Is.EqualTo("Ember Pact"));
            Assert.That(reclaimed.Status, Does.Contain("Reclaimed"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 3, "reclaimed"));

        var relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        int ownsTypeId = engine.GetService(CoreServiceKeys.OwnershipResolver)?.OwnsTypeId
            ?? throw new InvalidOperationException("OwnershipResolver missing.");
        Assert.That(ownsTypeId, Is.EqualTo(relationships.TypeRegistry.GetId("Owns")));

        WriteTrace(artifactDir, initial, captured, reclaimed, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Ownership Cascade Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("ownership-cascade-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static OwnershipCascadeRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(OwnershipCascadeIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is OwnershipCascadeRuntime runtime
            ? runtime
            : throw new InvalidOperationException("OwnershipCascadeRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Ownership cascade input backend missing.");
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
            who: "Player capturing and losing a city through Owns relation edges",
            what: "Change city ownership and verify garrison, warehouse, and production inherit the root owner.",
            where: OwnershipCascadeIds.ShowcaseMapId,
            why: "Verify AAC-5 high-level ownership is represented by a single Owns relationship chain.",
            how: "Drive real input bindings, inspect rendered ownership chain text, and assert RelationshipRuntime incoming edges.");
    }

    private static void WriteTrace(
        string artifactDir,
        OwnershipCascadeSnapshot initial,
        OwnershipCascadeSnapshot captured,
        OwnershipCascadeSnapshot reclaimed,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "initial-enemy-owned", snapshot = initial }, options),
                JsonSerializer.Serialize(new { step = "captured", snapshot = captured }, options),
                JsonSerializer.Serialize(new { step = "reclaimed", snapshot = reclaimed }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Ownership Cascade Showcase Acceptance",
                string.Empty,
                $"- initial city owner: {initial.CityOwner}",
                $"- captured city owner: {captured.CityOwner}",
                $"- reclaimed city owner: {reclaimed.CityOwner}",
                $"- Owns type id: {captured.OwnsTypeId}",
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
