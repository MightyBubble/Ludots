using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class PanelFireballShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "fireball_arena";
    private const string PanelTemplateId = "panel.fireball.status";

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FireballSharedMod",
        "UiShowcaseCoreMod",
    };

    [TestCase("PanelSkinMarkupMod")]
    [TestCase("PanelSkinComposeMod")]
    [TestCase("PanelSkinReactiveMod")]
    [TestCase("PanelSkinWebMod")]
    public void PanelFireballSkinShowcase_QCastsGasFireballAndRefreshesPanel(string skinMod)
    {
        using GameEngine engine = CreateEngine(skinMod, out TestInputBackend input);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        Assert.That(engine.CurrentMapSession?.MapConfig?.Tags, Does.Contain("fireball"));
        Assert.That(engine.MergedConfig.StartupInputContexts, Does.Contain("Default_Gameplay"));
        Assert.That(engine.MergedConfig.HasStartupLocalSeats, Is.True);
        PlayerInputHandler liveInput = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("InputHandler missing.");
        Assert.That(liveInput.HasContext("Default_Gameplay"), Is.True);
        Assert.That(liveInput.HasAction("SkillQ"), Is.True);
        Assert.That(engine.GetService(CoreServiceKeys.ActiveInputOrderMapping), Is.Not.Null);
        Assert.That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Hostile));

        World world = engine.World;
        Entity hero = FindEntity(world, "Hero");
        Entity target = FindEntity(world, "Target");
        Assert.That(ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localRep), Is.True);
        Assert.That(localRep, Is.EqualTo(hero));
        AssertTargetCanBeCommanded(engine, world, hero, target);

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        PanelInstanceHandle panel = FindPanel(panelHost, hero);
        Assert.That(panelHost.Count, Is.EqualTo(1), "The map trigger creates one panel instance; skin mods must only render it.");
        Assert.That(panelHost.TryGetAnchor(panel, out string panelAnchor), Is.True);
        Assert.That(panelAnchor, Is.EqualTo("screen.topRight"));
        AssertPanelValues(panelHost, panel, health: 100f, mana: 80f, attack: 25f);
        AssertSkinMounted(engine, skinMod);

        PressButton(engine, input, "<Keyboard>/q");
        TickUntil(
            engine,
            () => ReadAttribute(world, target, "Health") <= 105f,
            maxFrames: 90,
            describeFailure: () => BuildFireballDiagnostics(engine, world, hero, target));

        Assert.Multiple(() =>
        {
            Assert.That(ReadAttribute(world, hero, "Mana"), Is.EqualTo(70f).Within(0.001f));
            Assert.That(ReadAttribute(world, hero, "Attack"), Is.EqualTo(25f).Within(0.001f));
            Assert.That(ReadAttribute(world, target, "Health"), Is.EqualTo(105f).Within(0.001f));
        });
        AssertPanelValues(panelHost, panel, health: 100f, mana: 70f, attack: 25f);
        AssertSkinMounted(engine, skinMod);
    }

    private static GameEngine CreateEngine(string skinMod, out TestInputBackend backend)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        string[] mods = new string[BaseMods.Length + 1];
        Array.Copy(BaseMods, mods, BaseMods.Length);
        mods[^1] = skinMod;
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, mods),
            Path.Combine(repoRoot, "assets"));
        backend = InstallInput(engine);
        AcceptanceUiHostInstaller.Install(engine);
        return engine;
    }

    private static TestInputBackend InstallInput(GameEngine engine)
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
        return backend;
    }

    private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
    {
        backend.SetButton(path, true);
        Tick(engine, 1);
        backend.SetButton(path, false);
        Tick(engine, 1);
    }

    private static void TickUntil(
        GameEngine engine,
        Func<bool> condition,
        int maxFrames,
        Func<string> describeFailure)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail(describeFailure());
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, Entity scope)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.Scope == scope &&
                string.Equals(info.TemplateId, PanelTemplateId, StringComparison.Ordinal))
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException("Fireball status panel was not instantiated for the hero.");
    }

    private static void AssertPanelValues(
        PanelHost host,
        PanelInstanceHandle handle,
        float health,
        float mana,
        float attack)
    {
        Assert.That(host.TryGetValues(handle, out PanelVariableSet values), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(values.Get("health"), Is.EqualTo(health).Within(0.001f));
            Assert.That(values.Get("mana"), Is.EqualTo(mana).Within(0.001f));
            Assert.That(values.Get("attack"), Is.EqualTo(attack).Within(0.001f));
        });
    }

    private static void AssertSkinMounted(GameEngine engine, string skinMod)
    {
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null, $"{skinMod} must mount a UI scene for the shared panel instance.");
    }

    private static void AssertTargetCanBeCommanded(GameEngine engine, World world, Entity hero, Entity target)
    {
        Assert.Multiple(() =>
        {
            Assert.That(world.Has<CommandSourceSelectableTag>(hero), Is.True);
            Assert.That(world.Has<CommandSourceSelectableTag>(target), Is.True);
            Assert.That(world.Has<VisionEmitterCm>(hero), Is.True);
            Assert.That(world.Has<FogOccupantCm>(target), Is.True);
            Assert.That(CommandSourceEligibility.IsSelectableNow(world, target), Is.True);
        });

        KnowledgeProjectionResolver resolver = engine.GetService(CoreServiceKeys.KnowledgeProjectionResolver)
            ?? throw new InvalidOperationException("KnowledgeProjectionResolver missing.");
        IClock clock = engine.GetService(CoreServiceKeys.Clock)
            ?? throw new InvalidOperationException("Clock missing.");
        var gate = new KnowledgeCommandTargetGate(world, resolver, clock);
        Assert.That(gate.CanTarget(hero, target), Is.True, "Target should be visible to the local hero through production knowledge gating before Q is pressed.");
    }

    private static float ReadAttribute(World world, Entity entity, string attribute)
    {
        int id = AttributeRegistry.GetId(attribute);
        Assert.That(id, Is.GreaterThanOrEqualTo(0), attribute);
        Assert.That(world.Has<AttributeBuffer>(entity), Is.True, attribute);
        return world.Get<AttributeBuffer>(entity).GetCurrent(id);
    }

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
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

    private static string BuildFireballDiagnostics(GameEngine engine, World world, Entity hero, Entity target)
    {
        string lastOrder = engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastOrder", out object? lastOrderObj)
            ? lastOrderObj?.ToString() ?? "<null>"
            : "<missing>";
        InputOrderMappingSystem? mapping = engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
        string localSeat = ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity localRep)
            ? $"{localRep.Id}:{localRep.WorldId}:{localRep.Version}"
            : "<missing>";
        return string.Join(" | ",
            $"mapping={(mapping == null ? "<missing>" : mapping.InteractionMode.ToString())}",
            $"activation={mapping?.LastActivationResult.State.ToString() ?? "<missing>"}",
            $"lastOrder={lastOrder}",
            $"localSeat={localSeat}",
            $"heroMP={ReadAttribute(world, hero, "Mana"):0.###}",
            $"targetHP={ReadAttribute(world, target, "Health"):0.###}",
            $"team12={TeamManager.GetRelationship(1, 2)}",
            $"errors={engine.TriggerManager.Errors.Count}");
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
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) =>
            _buttons.TryGetValue(devicePath, out bool isDown) && isDown;

        public Vector2 GetMousePosition() => Vector2.Zero;

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool isDown)
        {
            _buttons[path] = isDown;
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
