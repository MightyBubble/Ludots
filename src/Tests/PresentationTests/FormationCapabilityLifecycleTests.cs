using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.System;
using FormationCapabilityShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class FormationCapabilityLifecycleTests
{
    private static readonly HashSet<string> InstalledSystemNames = new(StringComparer.Ordinal)
    {
        "FormationCapabilityShowcaseScenarioBindingSystem",
        "FormationCapabilityShowcaseStateSystem",
        "FormationCapabilityLocalOrderSourceSystem",
        "FormationCapabilityShowcaseFormationOutlinePresentationSystem",
        "FormationCapabilityShowcaseObstacleOverlayPresentationSystem",
    };

    [Test]
    public void PlayableLifecycle_UsesMemberExecutionContractsAndUnloadsCleanly()
    {
        using GameEngine engine = CreateEngine();
        Assert.That(CountFormationSystems(engine), Is.Zero);

        LoadMap(engine);
        TickUntil(engine, () => CountFormationAnchors(engine.World) > 0 && CountFormationMembers(engine.World) > 0, 180);

        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        ControlDomainQuery controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException("Formation lifecycle test requires ControlDomainQuery.");
        int anchors = 0;
        var anchorQuery = new QueryDescription().WithAll<FormationAnchorState>();
        engine.World.Query(in anchorQuery, (Entity entity, ref FormationAnchorState _) =>
        {
            Assert.That(engine.World.Has<MassNavigationAgent>(entity), Is.False);
            Assert.That(engine.World.Has<OrderBuffer>(entity), Is.False);
            anchors++;
        });

        int members = 0;
        int controlledMembers = 0;
        var memberQuery = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgent>();
        engine.World.Query(in memberQuery, (Entity entity, ref FormationMemberState _) =>
        {
            Assert.That(engine.World.Has<OrderBuffer>(entity), Is.True);
            Assert.That(engine.World.Has<MovePlanExecutionIntent>(entity), Is.True);
            Assert.That(engine.World.Has<MovePlanExecutionResult>(entity), Is.True);
            if (controlDomains.TryResolveControlDomain(entity, out Entity domain))
            {
                Assert.That(domain, Is.EqualTo(localPlayer));
                controlledMembers++;
            }
            members++;
        });

        Assert.Multiple(() =>
        {
            Assert.That(anchors, Is.GreaterThan(0));
            Assert.That(members, Is.GreaterThan(0));
            Assert.That(controlledMembers, Is.GreaterThan(0));
            Assert.That(CountFormationSystems(engine), Is.EqualTo(InstalledSystemNames.Count));
        });

        engine.UnloadMap("formation_capability_showcase");
        Assert.That(CountFormationSystems(engine), Is.Zero);

        LoadMap(engine);
        TickUntil(engine, () => CountFormationMembers(engine.World) > 0, 180);
        Assert.That(CountFormationSystems(engine), Is.EqualTo(InstalledSystemNames.Count));
    }

    private static GameEngine CreateEngine()
    {
        GC.KeepAlive(typeof(FormationAnchorState).Assembly);
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(
                repoRoot,
                new[]
                {
                    "LudotsCoreMod",
                    "CoreInputMod",
                    "CameraProfilesMod",
                    "MassNavigationMod",
                    "FormationCapabilityShowcaseMod",
                }),
            Path.Combine(repoRoot, "assets"));

        var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new EmptyInputBackend();
        var input = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            input.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, input);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        HeadlessPresentationTestHost.Install(engine);
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine)
    {
        engine.LoadMap(MapLoadRequest.FromMapId(
            "formation_capability_showcase",
            MapLaunchContext.Create(engine.MergedConfig.StartupLocalPlayerId)));
    }

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail($"Formation lifecycle condition was not met within {maxFrames} frames.");
    }

    private static int CountFormationAnchors(World world)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<FormationAnchorState>();
        world.Query(in query, (ref FormationAnchorState _) => count++);
        return count;
    }

    private static int CountFormationMembers(World world)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
        world.Query(in query, (ref FormationMemberState _, ref MassNavigationAgentIndex __) => count++);
        return count;
    }

    private static int CountFormationSystems(GameEngine engine)
    {
        FieldInfo groupsField = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo presentationField = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var groups = (Dictionary<SystemGroup, List<ISystem<float>>>)groupsField.GetValue(engine)!;
        var presentation = (List<ISystem<float>>)presentationField.GetValue(engine)!;
        int count = presentation.Count(system => InstalledSystemNames.Contains(system.GetType().Name));
        foreach (List<ISystem<float>> systems in groups.Values)
        {
            count += systems.Count(system => InstalledSystemNames.Contains(system.GetType().Name));
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) && File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }

    private sealed class EmptyInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
