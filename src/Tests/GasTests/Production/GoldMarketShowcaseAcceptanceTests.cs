using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using GoldMarketShowcaseMod;
using GoldMarketShowcaseMod.Runtime;
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
public sealed class GoldMarketShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.GoldMarket.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "GoldMarketShowcaseMod",
    };

    [Test]
    public void GoldMarketShowcase_SpendsAttributeBlocksInsufficientFundsAndRollsBackAtomicFailure()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "gold-market-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(96);
        var evidence = new List<UiAcceptanceEvidenceFrame>(3);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(GoldMarketIds.ShowcaseMapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        GoldMarketRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        PressButton(engine, input, "<Keyboard>/b", frameTimesMs);
        GoldMarketSnapshot bought = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(bought.LastStatus, Is.EqualTo(ExchangeExecutionStatus.Success));
            Assert.That(bought.Gold, Is.EqualTo(7));
            Assert.That(bought.Relics, Is.EqualTo(1));
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Has.Some.Contains("Gold Market Showcase"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 1, "buy-success"));

        PressButton(engine, input, "<Keyboard>/x", frameTimesMs);
        GoldMarketSnapshot insufficient = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(insufficient.LastStatus, Is.EqualTo(ExchangeExecutionStatus.InsufficientInput));
            Assert.That(insufficient.Gold, Is.EqualTo(7));
            Assert.That(insufficient.Relics, Is.EqualTo(1));
        });
        evidence.Add(Capture(uiRoot, screensDir, 2, "insufficient-funds"));

        PressButton(engine, input, "<Keyboard>/f", frameTimesMs);
        GoldMarketSnapshot atomicFailure = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(atomicFailure.LastStatus, Is.EqualTo(ExchangeExecutionStatus.OutputBlocked));
            Assert.That(atomicFailure.Gold, Is.EqualTo(7));
            Assert.That(atomicFailure.Relics, Is.EqualTo(1));
            Assert.That(atomicFailure.Bonuses, Is.EqualTo(0));
            Assert.That(atomicFailure.FailedPurchases, Is.GreaterThanOrEqualTo(2));
        });
        evidence.Add(Capture(uiRoot, screensDir, 3, "atomic-rollback"));

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            runtime.RefreshPanel(engine);
        }

        long uiAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        WriteTrace(artifactDir, bought, insufficient, atomicFailure, uiAllocated, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Gold Market Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("gold-market-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static GoldMarketRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(GoldMarketIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is GoldMarketRuntime runtime
            ? runtime
            : throw new InvalidOperationException("GoldMarketRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Gold market input backend missing.");
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
            who: "Player spending a Gold attribute in the market",
            what: "Buy successfully, fail when poor, and trigger a two-output rollback.",
            where: GoldMarketIds.ShowcaseMapId,
            why: "Verify AAC-7 Exchange attribute inputs settle and roll back atomically.",
            how: "Drive real input bindings, inspect UI state, and assert Gold plus item counts.");
    }

    private static void WriteTrace(
        string artifactDir,
        GoldMarketSnapshot bought,
        GoldMarketSnapshot insufficient,
        GoldMarketSnapshot atomicFailure,
        long uiAllocated,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "buy-success", snapshot = bought }, options),
                JsonSerializer.Serialize(new { step = "insufficient-funds", snapshot = insufficient }, options),
                JsonSerializer.Serialize(new { step = "atomic-rollback", snapshot = atomicFailure }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Gold Market Showcase Acceptance",
                string.Empty,
                $"- buy status: {bought.LastStatus}",
                $"- buy gold after success: {bought.Gold}",
                $"- insufficient status: {insufficient.LastStatus}",
                $"- rollback status: {atomicFailure.LastStatus}",
                $"- rollback gold after failure: {atomicFailure.Gold}",
                $"- rollback relics after failure: {atomicFailure.Relics}",
                $"- ui refresh allocated bytes (reported, not asserted): {uiAllocated}",
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
