using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using NUnit.Framework;
using TeamResearchShowcaseMod;
using TeamResearchShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class TeamResearchShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.TeamResearch.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "EntityCommandPanelMod",
        "TeamResearchShowcaseMod",
    };

    [Test]
    public void TeamResearchShowcase_UsesCollectionBackedScopeMembersForSharedUnlock()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "team-research-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frameTimesMs = new List<double>(96);
        var evidence = new List<UiAcceptanceEvidenceFrame>(4);
        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(TeamResearchIds.MapId);
        Tick(engine, 8, frameTimesMs);

        UIRoot uiRoot = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        TeamResearchRuntime runtime = ResolveRuntime(engine);
        TestInputBackend input = GetInputBackend(engine);

        TeamResearchSnapshot initial = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(initial.ActiveMemberCount, Is.EqualTo(1));
            Assert.That(initial.RequirementSatisfied, Is.False);
            Assert.That(initial.Unlocked, Is.False);
            Assert.That(AcceptanceUiEvidenceWriter.ExtractUiText(uiRoot), Does.Contain("Team Research Showcase"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 1, "blocked"));

        PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        TeamResearchSnapshot blockedPulse = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(blockedPulse.Progress, Is.EqualTo(0));
            Assert.That(blockedPulse.LastContribution, Is.EqualTo(0));
            Assert.That(blockedPulse.RequirementSatisfied, Is.False);
            Assert.That(blockedPulse.Status, Does.Contain("Need 2 active member"));
        });

        PressButton(engine, input, "<Keyboard>/a", frameTimesMs);
        TeamResearchSnapshot memberAdded = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(memberAdded.ActiveMemberCount, Is.EqualTo(2));
            Assert.That(memberAdded.RequirementSatisfied, Is.True);
            Assert.That(memberAdded.Unlocked, Is.False);
        });
        evidence.Add(Capture(uiRoot, screensDir, 2, "member-added"));

        Entity teamHost = FindEntity(engine.World, "Team Research Cell");
        Entity researcher = FindEntityWithScopeBinding(engine.World, "Lead Researcher");
        AssertCollectionBackedTeamScope(engine, teamHost, researcher, expectedMembers: 2);
        AssertProgressionRequirement(engine, researcher, expected: true);

        for (int i = 0; i < 4; i++)
        {
            PressButton(engine, input, "<Keyboard>/space", frameTimesMs);
        }

        TeamResearchSnapshot unlocked = runtime.Snapshot;
        Assert.Multiple(() =>
        {
            Assert.That(unlocked.Progress, Is.EqualTo(unlocked.ResearchCost));
            Assert.That(unlocked.Unlocked, Is.True);
            Assert.That(unlocked.Status, Does.Contain("Signal Relay unlocked"));
        });
        evidence.Add(Capture(uiRoot, screensDir, 3, "unlocked"));

        int progressionId = ProgressionIdRegistry.GetId("Progression.Showcase.TeamResearch.SignalRelay");
        Assert.That(progressionId, Is.GreaterThan(0));
        ref readonly var state = ref engine.World.Get<Ludots.Core.Gameplay.Progression.Components.ProgressionStateBuffer>(teamHost);
        Assert.That(state.HasCompleted(progressionId), Is.True);

        WriteTrace(artifactDir, initial, blockedPulse, memberAdded, unlocked, frameTimesMs);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(evidence, screensDir, Path.Combine(artifactDir, "timeline.png"), "Team Research Showcase");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown("team-research-showcase", evidence, Path.Combine(artifactDir, "5w1h.md"));
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

    private static TeamResearchRuntime ResolveRuntime(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TeamResearchIds.RuntimeServiceKey, out object? runtimeObj) &&
               runtimeObj is TeamResearchRuntime runtime
            ? runtime
            : throw new InvalidOperationException("TeamResearchRuntime missing.");
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Team research input backend missing.");
    }

    private static void AssertCollectionBackedTeamScope(GameEngine engine, Entity teamHost, Entity researcher, int expectedMembers)
    {
        var scopeKeys = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
            ?? throw new InvalidOperationException("ScopeKeyRegistry missing.");
        Assert.That(scopeKeys.TryGetId("team", out int teamScopeId), Is.True);
        Assert.That(scopeKeys.TryGetMembershipSource(teamScopeId, out ScopeMembershipSource source), Is.True);
        Assert.That(source.Kind, Is.EqualTo(ScopeMembershipSourceKind.EntityCollection));

        var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        int teamMembersKey = collections.KeyRegistry.GetId("team.members");
        Assert.That(teamMembersKey, Is.GreaterThan(0));
        Assert.That(source.KeyId, Is.EqualTo(teamMembersKey));

        var resolver = engine.GetService(CoreServiceKeys.ScopeResolver)
            ?? throw new InvalidOperationException("ScopeResolver missing.");
        var context = new RoleResolverContext(actor: researcher, subject: researcher);
        Span<Entity> members = stackalloc Entity[8];
        int count = resolver.ResolveMembers(ScopeKey.Named(teamScopeId), in context, members);
        Assert.That(count, Is.EqualTo(expectedMembers));
        for (int i = 0; i < count; i++)
        {
            Assert.That(engine.World.IsAlive(members[i]), Is.True);
        }

        Assert.That(resolver.TryResolveHost(ScopeKey.Named(teamScopeId), in context, out Entity resolvedHost), Is.True);
        Assert.That(resolvedHost, Is.EqualTo(teamHost));
    }

    private static void AssertProgressionRequirement(GameEngine engine, Entity researcher, bool expected)
    {
        int requirementId = ProgressionRequirementIdRegistry.GetId("Req.Showcase.TeamResearch.SignalRelay.Use");
        Assert.That(requirementId, Is.GreaterThan(0));
        var evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        var context = new RoleResolverContext(actor: researcher, subject: researcher);
        Assert.That(evaluator.Evaluate(requirementId, in context), Is.EqualTo(expected));
    }

    private static UiAcceptanceEvidenceFrame Capture(UIRoot uiRoot, string screensDir, int order, string step)
    {
        return AcceptanceUiEvidenceWriter.CaptureFrame(
            uiRoot,
            screensDir,
            order,
            step,
            when: $"T+{order:000}",
            who: "Player adding researchers and pulsing shared team research",
            what: "Drive a team meta-entity technology that reads ScopeMembers from EntityCollectionStore.",
            where: TeamResearchIds.MapId,
            why: "Verify AAC-8 Progression scope membership reuses association collections instead of a third member store.",
            how: "Use real input bindings, inspect the rendered panel, and assert ScopeResolver plus ProgressionRequirementEvaluator state.");
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

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
            {
                result = entity;
            }
        });

        if (result == Entity.Null)
        {
            throw new InvalidOperationException($"Missing entity '{entityName}'.");
        }

        return result;
    }

    private static Entity FindEntityWithScopeBinding(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name, ScopeRefBuffer>();
        world.Query(in query, (Entity entity, ref Name name, ref ScopeRefBuffer refs) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
            {
                result = entity;
            }
        });

        if (result == Entity.Null)
        {
            throw new InvalidOperationException($"Missing scope-bound entity '{entityName}'.");
        }

        return result;
    }

    private static void WriteTrace(
        string artifactDir,
        TeamResearchSnapshot initial,
        TeamResearchSnapshot blockedPulse,
        TeamResearchSnapshot memberAdded,
        TeamResearchSnapshot unlocked,
        IReadOnlyList<double> frameTimesMs)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        File.WriteAllLines(
            Path.Combine(artifactDir, "trace.jsonl"),
            new[]
            {
                JsonSerializer.Serialize(new { step = "initial", snapshot = initial }, options),
                JsonSerializer.Serialize(new { step = "blocked-pulse", snapshot = blockedPulse }, options),
                JsonSerializer.Serialize(new { step = "member-added", snapshot = memberAdded }, options),
                JsonSerializer.Serialize(new { step = "unlocked", snapshot = unlocked }, options),
            });

        File.WriteAllLines(
            Path.Combine(artifactDir, "battle-report.md"),
            new[]
            {
                "# Team Research Showcase Acceptance",
                string.Empty,
                $"- initial active members: {initial.ActiveMemberCount}",
                $"- blocked progress: {blockedPulse.Progress}/{blockedPulse.ResearchCost}",
                $"- active members after A: {memberAdded.ActiveMemberCount}",
                $"- requirement after A: {memberAdded.RequirementSatisfied}",
                $"- unlocked: {unlocked.Unlocked}",
                $"- final progress: {unlocked.Progress}/{unlocked.ResearchCost}",
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
