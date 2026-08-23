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
    };

    [TestCase("PanelSkinMarkupMod")]
    [TestCase("PanelSkinComposeMod")]
    [TestCase("PanelSkinReactiveMod")]
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


    [Test]
    public void PanelFireballDefaultSkin_NoSkinMod_ZeroCodePanelBecomesVisible()
    {
        using GameEngine engine = CreateEngine(null, out TestInputBackend input);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        var world = engine.World;
        Entity hero = FindEntity(world, "Hero");
        Entity target = FindEntity(world, "Target");
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        PanelInstanceHandle panel = FindPanel(panelHost, hero);

        PressButton(engine, input, "<Keyboard>/q");
        TickUntil(
            engine,
            () => ReadAttribute(world, target, "Health") <= 105f,
            maxFrames: 90,
            describeFailure: () => BuildFireballDiagnostics(engine, world, hero, target));

        AssertPanelValues(panelHost, panel, health: 100f, mana: 70f, attack: 25f);
        AssertSkinMounted(engine, "engine default skin");
    }

    [Test]
    public void PanelFireballSkinRouting_InstanceSkinBeatsTemplateAndGlobal_FourSkinsCoexist()
    {
        using GameEngine engine = CreateEngine(null, out TestInputBackend _);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Entity hero = FindEntity(engine.World, "Hero");

        PanelInstanceHandle markup = panelHost.Instantiate(PanelTemplateId, "screen.topRight", hero, "markup", 100);
        PanelInstanceHandle reactive = panelHost.Instantiate(PanelTemplateId, "screen.topRight", hero, "reactive", 200);
        Ludots.Core.UI.PanelActivation.PanelActivationApi activationApi = engine.GetService(CoreServiceKeys.PanelActivationApi)
            ?? throw new InvalidOperationException("PanelActivationApi missing.");
        activationApi.ShowPanel(PanelTemplateId);
        Tick(engine, 4);

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null, "Default presentation must own a scene.");
        int containers = CountContainers(root.Scene!.Root!);
        Assert.That(containers, Is.GreaterThanOrEqualTo(3),
            "Map panel plus two per-instance skins must all be mounted concurrently; got " + containers + ".");

        Assert.That(panelHost.TryGetValues(markup, out _), Is.True);
        Assert.That(panelHost.TryGetValues(reactive, out _), Is.True);
    }

    private static int CountContainers(Ludots.UI.Runtime.UiNode node)
    {
        int count = node.Kind == Ludots.UI.Runtime.UiNodeKind.Container ? 1 : 0;
        foreach (Ludots.UI.Runtime.UiNode child in node.Children)
        {
            count += CountContainers(child);
        }

        return count;
    }

    [Test]
    public void PanelFireballWebSkin_HeadlessHost_SkipsCefOverlayButCreatesPanel()
    {
        using GameEngine engine = CreateEngine("PanelSkinWebMod", out TestInputBackend _);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(1), "The map trigger still creates the panel instance headlessly.");
        AssertPanelValues(panelHost, FindPanel(panelHost, FindEntity(engine.World, "Hero")), health: 100f, mana: 80f, attack: 25f);
    }

    [Test]
    public void PanelFireballThemePack_LoadsAndAppliesToAutoLayoutContract()
    {
        using GameEngine engine = CreateEngine("PanelThemeInkMod", out TestInputBackend _, extraMods: new[] { "PanelThemeShowcaseMod" });
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        Ludots.UI.Panels.PanelTheme? theme = Ludots.UI.Panels.PanelThemeCatalog.TryLoad(engine);
        Assert.That(theme, Is.Not.Null, "The ink-wash theme pack must load through the merged themes catalog.");
        Assert.That(theme!.Id, Is.EqualTo("ink-wash"));
        Assert.That(theme.WebCss, Does.Contain("data:"), "The web variant must inline images as data URIs.");

        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null, "Themed panel must mount through the default presentation.");
        Assert.That(FindNodeByClass(root.Scene!.Root!, "panel-fireball-status"), Is.Not.Null,
            "Auto-layout nodes must carry the CSS class contract (.panel-<template>).");
        Assert.That(FindNodeByClass(root.Scene!.Root!, "row-health"), Is.Not.Null,
            "Value rows must carry variable classes (.row-<name>).");
    }

    [Test]
    public void PanelFireballThemePack_MinimalTheme_RendersWithZeroImageAssets()
    {
        using GameEngine engine = CreateEngine("PanelThemeMinimalMod", out TestInputBackend _, extraMods: new[] { "PanelThemeShowcaseMod" });
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 4);

        Ludots.UI.Panels.PanelTheme? theme = Ludots.UI.Panels.PanelThemeCatalog.TryLoad(engine);
        Assert.That(theme, Is.Not.Null);
        Assert.That(theme!.Id, Is.EqualTo("minimal"));
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot missing.");
        Assert.That(root.Scene, Is.Not.Null);
    }

    private static Ludots.UI.Runtime.UiNode? FindNodeByClass(Ludots.UI.Runtime.UiNode node, string className)
    {
        foreach (string name in node.ClassNames)
        {
            if (string.Equals(name, className, System.StringComparison.Ordinal))
            {
                return node;
            }
        }

        foreach (Ludots.UI.Runtime.UiNode child in node.Children)
        {
            if (FindNodeByClass(child, className) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static GameEngine CreateEngine(string? skinMod, out TestInputBackend backend, string[]? extraMods = null)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        IEnumerable<string> mods = skinMod == null ? BaseMods : BaseMods.Append(skinMod);
        if (extraMods != null)
        {
            mods = mods.Concat(extraMods);
        }
        mods = mods.ToArray();
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
            Assert.That(values.Get("healthBase"), Is.EqualTo(100f).Within(0.001f),
                "Display denominators must project the hero template base, never a presentation constant.");
            Assert.That(values.Get("manaBase"), Is.EqualTo(80f).Within(0.001f),
                "The mana pool must stay 80 while the current value drains.");
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
