using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using DiplomacyTradeGateShowcaseMod;
using DiplomacyTradeGateShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class DiplomacyTradeGateShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.DiplomacyTradeGate.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "DiplomacyTradeGateShowcaseMod",
    };

    [Test]
    public void DiplomacyTradeGateShowcase_RelationshipControlsExchangeSettlement()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "diplomacy-trade-gate-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(96);
        var evidence = new List<UiAcceptanceEvidenceFrame>(3);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(DiplomacyTradeGateIds.ShowcaseMapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        DiplomacyTradeGateRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        PressButton(engine, input, "<Keyboard>/t", frameTimesMs);
        DiplomacyTradeGateSnapshot denied = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(denied.LastStatus, Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));
            Assert.That(denied.SourceCredits, Is.EqualTo(30));
            Assert.That(denied.TargetGoods, Is.EqualTo(0));
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Has.Some.Contains("RelationshipDenied"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 1, "denied-no-agreement"));

        PressButton(engine, input, "<Keyboard>/p", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/t", frameTimesMs);
        DiplomacyTradeGateSnapshot settled = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(settled.LastStatus, Is.EqualTo(ExchangeExecutionStatus.Success));
            Assert.That(settled.Trust, Is.GreaterThanOrEqualTo(50));
            Assert.That(settled.Embargo, Is.False);
            Assert.That(settled.SourceCredits, Is.EqualTo(25));
            Assert.That(settled.TargetGoods, Is.EqualTo(1));
            Assert.That(settled.SuccessfulTrades, Is.EqualTo(1));
        });
        evidence.Add(Capture(uiRoot, screensDir, 2, "settled-after-agreement"));

        PressButton(engine, input, "<Keyboard>/e", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/t", frameTimesMs);
        DiplomacyTradeGateSnapshot embargoed = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(embargoed.LastStatus, Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));
            Assert.That(embargoed.Embargo, Is.True);
            Assert.That(embargoed.SourceCredits, Is.EqualTo(25));
            Assert.That(embargoed.TargetGoods, Is.EqualTo(1));
            Assert.That(embargoed.SuccessfulTrades, Is.EqualTo(1));
        });
        evidence.Add(Capture(uiRoot, screensDir, 3, "denied-embargo"));

        WriteTrace(artifactDir, denied, settled, embargoed, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Diplomacy Trade Gate Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("diplomacy-trade-gate-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static DiplomacyTradeGateRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(DiplomacyTradeGateIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is DiplomacyTradeGateRuntime runtime
            ? runtime
            : throw new InvalidOperationException("DiplomacyTradeGateRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Diplomacy trade gate input backend missing.");
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
            who: "Player testing whether a border exchange is allowed by relationship state",
            what: "Try the same exchange before agreement, after agreement, and after embargo.",
            where: DiplomacyTradeGateIds.ShowcaseMapId,
            why: "Verify AAC-6 Exchange settlement is gated by configured RelationshipRuntime requirements.",
            how: "Drive real input bindings, inspect rendered status text, and assert item counts plus ExchangeExecutionStatus.");
    }

    private static void WriteTrace(
        string artifactDir,
        DiplomacyTradeGateSnapshot denied,
        DiplomacyTradeGateSnapshot settled,
        DiplomacyTradeGateSnapshot embargoed,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "denied-no-agreement", snapshot = denied }, options),
                JsonSerializer.Serialize(new { step = "settled-after-agreement", snapshot = settled }, options),
                JsonSerializer.Serialize(new { step = "denied-embargo", snapshot = embargoed }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Diplomacy Trade Gate Showcase Acceptance",
                string.Empty,
                $"- no agreement status: {denied.LastStatus}",
                $"- after agreement status: {settled.LastStatus}",
                $"- after embargo status: {embargoed.LastStatus}",
                $"- trust after agreement: {settled.Trust}",
                $"- source credits after settlement: {settled.SourceCredits}",
                $"- target goods after settlement: {settled.TargetGoods}",
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
