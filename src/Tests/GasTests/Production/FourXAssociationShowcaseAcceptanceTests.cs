using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using FourXAssociationShowcaseMod;
using FourXAssociationShowcaseMod.Runtime;
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
public sealed class FourXAssociationShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.FourXAssociation.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "AssociationStressShowcaseMod",
        "FogVisionDecayShowcaseMod",
        "ScopeSwitchShowcaseMod",
        "OwnershipCascadeShowcaseMod",
        "DiplomacyTradeGateShowcaseMod",
        "GoldMarketShowcaseMod",
        "TeamResearchShowcaseMod",
        "FourXAssociationShowcaseMod",
    };

    [Test]
    public void FourXAssociationShowcase_ChainsFogDiplomacyTradeResearchAndOwnership()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "fourx-association-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(160);
        var evidence = new List<UiAcceptanceEvidenceFrame>(5);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(FourXAssociationIds.MapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        FourXAssociationRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        FourXAssociationSnapshot initial = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(initial.HiddenBeforeScout, Is.True);
            Assert.That(initial.OwnershipRootMatchesPlayer, Is.True);
            Assert.That(initial.OwnershipDirectCityToStash, Is.True);
            Assert.That(initial.ResearchRequirementSatisfied, Is.False);
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Has.Some.Contains("4X Association Showcase"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 1, "initial"));

        PressButton(engine, input, "<Keyboard>/t", frameTimesMs);
        FourXAssociationSnapshot blockedTrade = runtime.Snapshot;
        Assert.That(blockedTrade.TradeBeforePact, Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));

        PressButton(engine, input, "<Keyboard>/r", frameTimesMs);
        FourXAssociationSnapshot scouted = runtime.Snapshot;
        Assert.That(scouted.VisibleAfterScout, Is.True);

        PressButton(engine, input, "<Keyboard>/n", frameTimesMs);
        FourXAssociationSnapshot fogDecayed = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(fogDecayed.HiddenAfterDecay, Is.True);
            Assert.That(fogDecayed.ExpiredKnowledgeRecords, Is.GreaterThanOrEqualTo(1));
        });
        evidence.Add(Capture(uiRoot, screensDir, 2, "fog-decay"));

        PressButton(engine, input, "<Keyboard>/p", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/t", frameTimesMs);
        FourXAssociationSnapshot traded = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(traded.PactSigned, Is.True);
            Assert.That(traded.TradeAfterPact, Is.EqualTo(ExchangeExecutionStatus.Success));
            Assert.That(traded.Gold, Is.EqualTo(13));
            Assert.That(traded.SupplyCount, Is.EqualTo(1));
        });
        evidence.Add(Capture(uiRoot, screensDir, 3, "pact-trade"));

        PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        FourXAssociationSnapshot blockedResearch = runtime.Snapshot;
        Assert.That(blockedResearch.ResearchProgress, Is.EqualTo(0));

        PressButton(engine, input, "<Keyboard>/a", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        FourXAssociationSnapshot unlocked = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(unlocked.ActiveResearchMembers, Is.EqualTo(2));
            Assert.That(unlocked.ResearchRequirementSatisfied, Is.True);
            Assert.That(unlocked.TechUnlocked, Is.True);
            Assert.That(unlocked.ResearchProgress, Is.EqualTo(unlocked.ResearchCost));
        });
        evidence.Add(Capture(uiRoot, screensDir, 4, "shared-tech"));

        WriteTrace(artifactDir, initial, blockedTrade, fogDecayed, traded, blockedResearch, unlocked, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "4X Association Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("fourx-association-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static FourXAssociationRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(FourXAssociationIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is FourXAssociationRuntime runtime
            ? runtime
            : throw new InvalidOperationException("FourXAssociationRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("FourX association input backend missing.");
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
            who: "Player completing a compact 4X association scenario",
            what: "Reveal fog, validate diplomacy-gated trade, spend Gold, trace ownership, and unlock shared research.",
            where: FourXAssociationIds.MapId,
            why: "Verify AAC-10 integrates the Entity Association Core feature seams into one playable acceptance root.",
            how: "Drive real input bindings, inspect rendered UI, and assert Core services through the public showcase snapshot.");
    }

    private static void WriteTrace(
        string artifactDir,
        FourXAssociationSnapshot initial,
        FourXAssociationSnapshot blockedTrade,
        FourXAssociationSnapshot fogDecayed,
        FourXAssociationSnapshot traded,
        FourXAssociationSnapshot blockedResearch,
        FourXAssociationSnapshot unlocked,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "initial", snapshot = initial }, options),
                JsonSerializer.Serialize(new { step = "relationship-gated-trade-blocked", snapshot = blockedTrade }, options),
                JsonSerializer.Serialize(new { step = "fog-decayed", snapshot = fogDecayed }, options),
                JsonSerializer.Serialize(new { step = "pact-trade", snapshot = traded }, options),
                JsonSerializer.Serialize(new { step = "research-blocked", snapshot = blockedResearch }, options),
                JsonSerializer.Serialize(new { step = "shared-tech-unlocked", snapshot = unlocked }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# 4X Association Showcase Acceptance",
                string.Empty,
                $"- fog gate: hidden before scout `{initial.HiddenBeforeScout}`, hidden after decay `{fogDecayed.HiddenAfterDecay}`",
                $"- trade before pact: `{blockedTrade.TradeBeforePact}`",
                $"- trade after pact: `{traded.TradeAfterPact}`",
                $"- gold after trade: `{traded.Gold}`",
                $"- supply count after trade: `{traded.SupplyCount}`",
                $"- research blocked progress: `{blockedResearch.ResearchProgress}`",
                $"- shared tech unlocked: `{unlocked.TechUnlocked}` with `{unlocked.ActiveResearchMembers}` members",
                $"- ownership root player: `{initial.OwnershipRootMatchesPlayer}`",
                $"- sampled frames: `{frameTimesMs.Count}`",
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

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => new(-1f, -1f);

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
