using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using NUnit.Framework;
using ScopeSwitchShowcaseMod;
using ScopeSwitchShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ScopeSwitchShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.ScopeSwitch.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "ScopeSwitchShowcaseMod",
    };

    [Test]
    public void ScopeSwitchShowcase_SwitchesVisibleAndSelectableSetsAcrossScopes()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "scope-switch-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(96);
        var evidence = new List<UiAcceptanceEvidenceFrame>();
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(ScopeSwitchIds.MapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        ScopeSwitchRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        ScopeSwitchSnapshot self = runtime.Snapshot;
        Assert.That(self.ActiveScopeId, Is.EqualTo("self"));
        Assert.That(self.VisibleLabels, Is.EqualTo(new[] { "Hero Scout" }));
        Assert.That(self.SelectedLabels, Is.EqualTo(new[] { "Hero Scout" }));
        evidence.Add(Capture(uiRoot, screensDir, 1, "self"));

        PressButton(engine, input, "<Keyboard>/2", frameTimesMs);
        ScopeSwitchSnapshot squad = runtime.Snapshot;
        Assert.That(squad.ActiveScopeId, Is.EqualTo("squad"));
        Assert.That(squad.VisibleLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer" }));
        Assert.That(squad.SelectedLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer" }));
        evidence.Add(Capture(uiRoot, screensDir, 2, "squad"));

        PressButton(engine, input, "<Keyboard>/3", frameTimesMs);
        ScopeSwitchSnapshot team = runtime.Snapshot;
        Assert.That(team.ActiveScopeId, Is.EqualTo("team"));
        Assert.That(team.VisibleLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer", "Team Captain", "Team Archer" }));
        Assert.That(team.SelectedLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer", "Team Captain", "Team Archer" }));
        evidence.Add(Capture(uiRoot, screensDir, 3, "team"));

        PressButton(engine, input, "<Keyboard>/4", frameTimesMs);
        ScopeSwitchSnapshot city = runtime.Snapshot;
        Assert.That(city.ActiveScopeId, Is.EqualTo("city"));
        Assert.That(city.VisibleLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer", "Team Captain", "Team Archer", "City Watch", "City Market" }));
        Assert.That(city.SelectedLabels, Is.EqualTo(new[] { "Hero Scout", "Squad Medic", "Squad Engineer", "Team Captain", "Team Archer", "City Watch", "City Market" }));
        evidence.Add(Capture(uiRoot, screensDir, 4, "city"));

        Assert.That(self.VisibleCount, Is.LessThan(squad.VisibleCount));
        Assert.That(squad.VisibleCount, Is.LessThan(team.VisibleCount));
        Assert.That(team.VisibleCount, Is.LessThan(city.VisibleCount));
        Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Does.Contain("Scope Switch Showcase"));

        WriteTrace(artifactDir, self, squad, team, city, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Scope Switch Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("scope-switch-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static ScopeSwitchRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(ScopeSwitchIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is ScopeSwitchRuntime runtime
            ? runtime
            : throw new InvalidOperationException("ScopeSwitchRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Scope switch input backend missing.");
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
            who: "Player switching the active viewer scope",
            what: "Change the same viewer from self to squad, team, and city scope.",
            where: ScopeSwitchIds.MapId,
            why: "Verify AAC-4 unified ScopeKey and RoleSlot resolution in a playable mod.",
            how: "Drive real input bindings, let the showcase panel render the resolved visible and selectable sets, and capture evidence.");
    }

    private static void WriteTrace(string artifactDir, ScopeSwitchSnapshot self, ScopeSwitchSnapshot squad, ScopeSwitchSnapshot team, ScopeSwitchSnapshot city, IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "self", snapshot = self }, options),
                JsonSerializer.Serialize(new { step = "squad", snapshot = squad }, options),
                JsonSerializer.Serialize(new { step = "team", snapshot = team }, options),
                JsonSerializer.Serialize(new { step = "city", snapshot = city }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Scope Switch Showcase Acceptance",
                string.Empty,
                $"- self visible: {self.VisibleCount}",
                $"- squad visible: {squad.VisibleCount}",
                $"- team visible: {team.VisibleCount}",
                $"- city visible: {city.VisibleCount}",
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
