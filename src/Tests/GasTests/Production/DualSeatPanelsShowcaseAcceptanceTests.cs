using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using DualSeatPanelsShowcaseMod;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Dual-seat per-seat panel showcase acceptance (#1315): dual-seat map entry with the
/// per-seat schemes, two per-seat template panels plus one shared panel instantiated by
/// the MapLoaded trigger graphs, per-seat variable isolation through the shared graph,
/// seat-attributed admission (own seat admitted / other seat refused with the engine's
/// reason), shared-panel one-instance-two-seats operation, and hotseat audience rotation
/// through the activation store.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class DualSeatPanelsShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string ShowcaseMapId = DualSeatPanelsShowcaseIds.MapId;
    private const string SeatZeroPanel = DualSeatPanelsShowcaseIds.SeatZeroPanelId;
    private const string SeatOnePanel = DualSeatPanelsShowcaseIds.SeatOnePanelId;
    private const string SharedPanel = DualSeatPanelsShowcaseIds.SharedPanelId;

    [Test]
    public void DualSeatEntry_TwoPerSeatPanelsAndOneShared_PerSeatDataAdmissionAndRotation()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(DualSeatLaunch());
        Tick(engine, 8);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        // ── two seats, two schemes, distinct reps ──
        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine);
        Assert.That(seats.Count, Is.EqualTo(2));
        ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
            ?? throw new InvalidOperationException("ClientLocalSeatInputRuntime service is missing.");
        Assert.That(seatInput.TryGetChannel(DualSeatPanelsShowcaseIds.SeatZero, out ClientLocalSeatInputChannel channelZero), Is.True);
        Assert.That(seatInput.TryGetChannel(DualSeatPanelsShowcaseIds.SeatOne, out ClientLocalSeatInputChannel channelOne), Is.True);
        Entity repZero = seats.Require(DualSeatPanelsShowcaseIds.SeatZero).PossessedRep;
        Entity repOne = seats.Require(DualSeatPanelsShowcaseIds.SeatOne).PossessedRep;
        Assert.That(repZero, Is.Not.EqualTo(repOne));
        Assert.That(engine.World.IsAlive(repZero) && engine.World.IsAlive(repOne), Is.True);

        // ── three template panels: two per-seat + one shared ──
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost service is missing.");
        Assert.That(panelHost.Count, Is.EqualTo(3), "two per-seat panels plus one shared panel instance");
        PanelInstanceHandle seatZeroHandle = FindPanel(panelHost, SeatZeroPanel, repZero);
        PanelInstanceHandle seatOneHandle = FindPanel(panelHost, SeatOnePanel, repOne);
        PanelInstanceHandle sharedHandle = FindPanel(panelHost, SharedPanel, repZero);

        // ── per-seat data isolation through one shared graph (panel constitution: graph is the only source) ──
        Assert.That(panelHost.TryGetValues(seatZeroHandle, out PanelVariableSet valuesZero), Is.True);
        Assert.That(valuesZero.Get("hp"), Is.EqualTo(120f).Within(0.001f));
        Assert.That(valuesZero.Get("hpMax"), Is.EqualTo(120f).Within(0.001f));
        Assert.That(valuesZero.Get("supply"), Is.EqualTo(30f).Within(0.001f));
        Assert.That(panelHost.TryGetValues(seatOneHandle, out PanelVariableSet valuesOne), Is.True);
        Assert.That(valuesOne.Get("hp"), Is.EqualTo(90f).Within(0.001f));
        Assert.That(valuesOne.Get("hpMax"), Is.EqualTo(90f).Within(0.001f));
        Assert.That(valuesOne.Get("supply"), Is.EqualTo(50f).Within(0.001f));
        Assert.That(panelHost.TryGetValues(sharedHandle, out PanelVariableSet sharedValues), Is.True);
        Assert.That(sharedValues.Get("fieldHp"), Is.EqualTo(210f).Within(0.001f), "graph aggregates both heroes' Health");
        Assert.That(sharedValues.Get("heroes"), Is.EqualTo(2f).Within(0.001f));
        Assert.That(sharedValues.Get("charges"), Is.EqualTo(0f).Within(0.001f));
        Assert.That(sharedValues.Get("boosts"), Is.EqualTo(0f).Within(0.001f));

        // ── audience admission contract on the loaded templates ──
        PanelTemplateRegistry templates = engine.GetService(CoreServiceKeys.PanelTemplateRegistry)
            ?? throw new InvalidOperationException("PanelTemplateRegistry service is missing.");
        UiPanelActivationStore activation = engine.GetService(CoreServiceKeys.PanelActivationStore)
            ?? throw new InvalidOperationException("PanelActivationStore service is missing.");
        var seatZeroDispatcher = new PanelEventDispatcher(templates.Require(SeatZeroPanel), static (_, _) => { }, activation);
        Assert.That(seatZeroDispatcher.FireFromSeat(
            DualSeatPanelsShowcaseIds.ModifyEventId, ModifyArgs(), DualSeatPanelsShowcaseIds.SeatZero).Admitted, Is.True);
        PanelEventFireResult crossSeat = seatZeroDispatcher.FireFromSeat(
            DualSeatPanelsShowcaseIds.ModifyEventId, ModifyArgs(), DualSeatPanelsShowcaseIds.SeatOne);
        Assert.That(crossSeat.Admitted, Is.False, "seat.1 operating seat.0's panel is an audience violation");
        Assert.That(crossSeat.Reason, Does.Contain(SeatZeroPanel));
        Assert.That(crossSeat.Reason, Does.Contain(DualSeatPanelsShowcaseIds.SeatOne));
        Assert.That(crossSeat.Reason, Does.Contain(DualSeatPanelsShowcaseIds.SeatZero));

        var sharedDispatcher = new PanelEventDispatcher(templates.Require(SharedPanel), static (_, _) => { }, activation);
        Assert.That(sharedDispatcher.FireFromSeat(
            DualSeatPanelsShowcaseIds.ChargeEventId, ChargeArgs(), DualSeatPanelsShowcaseIds.SeatZero).Admitted, Is.True);
        Assert.That(sharedDispatcher.FireFromSeat(
            DualSeatPanelsShowcaseIds.ChargeEventId, ChargeArgs(), DualSeatPanelsShowcaseIds.SeatOne).Admitted, Is.True,
            "the shared audience admits both seats");

        // ── live loop through the real chain: per-seat channel → FireFromSeat → custom event → settlement ──
        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.StrikeAction);
        Assert.That(CurrentHealth(engine, repZero), Is.EqualTo(110f).Within(0.001f),
            "seat.0's admitted strike settles on seat.0's own rep");
        Assert.That(CurrentHealth(engine, repOne), Is.EqualTo(90f).Within(0.001f),
            "seat.0's operation never touches seat.1's rep");

        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.BoostAction);
        Assert.That(CurrentHealth(engine, repZero), Is.EqualTo(120f).Within(0.001f),
            "the admitted boost heals back toward the clamp (base)");

        // The refusal: seat.0 pokes seat.1's panel — no state change, reason flows back.
        MapVariableStore? variables = engine.CurrentMapSession?.Variables;
        Assert.That(variables, Is.Not.Null);
        int boostsAfterTwoAdmitted = variables!.ReadInt("dsp_boost_total");
        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.PokeAction);
        Assert.That(CurrentHealth(engine, repOne), Is.EqualTo(90f).Within(0.001f),
            "the refused cross-seat operation never reaches gameplay");
        Assert.That(variables.ReadInt("dsp_boost_total"), Is.EqualTo(boostsAfterTwoAdmitted),
            "a refused operation never increments the admitted-event audit counter");

        // Shared panel: one instance, both seats operate the same counter.
        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.ChargeAction);
        PressAndTick(engine, channelOne, DualSeatPanelsShowcaseIds.ChargeAction);
        Assert.That(variables!.ReadInt("dsp_shared_charges"), Is.EqualTo(2), "both seats accumulate the one shared counter");
        Assert.That(panelHost.TryGetValues(sharedHandle, out PanelVariableSet sharedAfterCharge), Is.True);
        Assert.That(sharedAfterCharge.Get("charges"), Is.EqualTo(2f).Within(0.001f));

        // ── per-seat surface mounting: each panel lives inside its own seat's half ──
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing.");
        Assert.That(root.Scene, Is.Not.Null);
        root.Scene!.Layout(root.Width, root.Height);
        float half = root.Width * 0.5f;
        UiNode? seatZeroMount = FindNodeByClass(root.Scene.Root!, "panel-dsp-seat0");
        UiNode? seatOneMount = FindNodeByClass(root.Scene.Root!, "panel-dsp-seat1");
        Assert.That(seatZeroMount, Is.Not.Null, "seat.0's panel mounts on the seat table's surfaces");
        Assert.That(seatOneMount, Is.Not.Null);
        Assert.That(seatZeroMount!.LayoutRect.X, Is.LessThan(half), "seat.0's panel stays in the left PresentBinding rect");
        Assert.That(seatOneMount!.LayoutRect.X, Is.GreaterThanOrEqualTo(half), "seat.1's panel stays in the right PresentBinding rect");
        Assert.That(CountNodesByClass(root.Scene.Root!, "panel-dsp-shared"), Is.EqualTo(2),
            "the shared instance mounts one presentation copy per audience seat");

        IReadOnlyList<string> uiText = AcceptanceUiEvidenceWriter.ExtractUiText(root);
        Assert.That(uiText, Has.Some.Contains("拒绝"), "the refusal reason is visible UI feedback");
        Assert.That(uiText, Has.Some.Contains(SeatOnePanel));

        // ── hotseat rotation: audience override narrows the shared panel to one seat ──
        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.RotateTurnAction);
        Assert.That(activation.TryGetAudienceOverride(SharedPanel, out PanelAudience narrowed), Is.True);
        Assert.That(narrowed.SeatIds, Is.EqualTo(new[] { DualSeatPanelsShowcaseIds.SeatZero }));
        root.Scene.Layout(root.Width, root.Height);
        Assert.That(CountNodesByClass(root.Scene.Root!, "panel-dsp-shared"), Is.EqualTo(1),
            "the waiting seat's half drops the shared panel mount");
        int chargesBeforeRefusal = variables.ReadInt("dsp_shared_charges");
        PressAndTick(engine, channelOne, DualSeatPanelsShowcaseIds.ChargeAction);
        Assert.That(variables.ReadInt("dsp_shared_charges"), Is.EqualTo(chargesBeforeRefusal),
            "the seat outside the rotated audience is refused on the shared panel");

        PressAndTick(engine, channelZero, DualSeatPanelsShowcaseIds.RotateTurnAction);
        PressAndTick(engine, channelOne, DualSeatPanelsShowcaseIds.RotateTurnAction);
        Assert.That(activation.TryGetAudienceOverride(SharedPanel, out _), Is.False,
            "rotating through both seats restores the declared audience");
        root.Scene.Layout(root.Width, root.Height);
        Assert.That(CountNodesByClass(root.Scene.Root!, "panel-dsp-shared"), Is.EqualTo(2));
        PressAndTick(engine, channelOne, DualSeatPanelsShowcaseIds.ChargeAction);
        Assert.That(variables.ReadInt("dsp_shared_charges"), Is.EqualTo(chargesBeforeRefusal + 1),
            "back on the declared audience, seat.1 operates the shared panel again");
    }

    private static JsonObject ModifyArgs() => new() { ["amount"] = 10 };
    private static JsonObject ChargeArgs() => new() { ["amount"] = 1 };

    private static MapLoadRequest DualSeatLaunch()
    {
        return new MapLoadRequest(
            new MapId(ShowcaseMapId),
            MapLaunchContext.Create(new[]
            {
                new LocalSeatLaunchBinding(DualSeatPanelsShowcaseIds.SeatZero, 1, "scheme.dsp.left"),
                new LocalSeatLaunchBinding(DualSeatPanelsShowcaseIds.SeatOne, 2, "scheme.dsp.right"),
            }));
    }

    private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "DualSeatPanelsShowcaseMod" }),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine, backend);
        AcceptanceUiHostInstaller.Install(engine);
        engine.SetService(CoreServiceKeys.ViewController, (Ludots.Core.Presentation.Camera.IViewController)new HeadlessViewController(1920f, 1080f));
        engine.Start();
        return engine;
    }

    private static void InstallInput(GameEngine engine, TestInputBackend backend)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void PressAndTick(GameEngine engine, ClientLocalSeatInputChannel channel, string actionId)
    {
        channel.Handler.InjectButtonPress(actionId);
        Tick(engine, 2);
        channel.Handler.InjectButtonRelease(actionId);
        Tick(engine, 1);
    }

    private static float CurrentHealth(GameEngine engine, Entity rep)
    {
        int healthId = AttributeRegistry.GetId("Health");
        return engine.World.Get<AttributeBuffer>(rep).GetCurrent(healthId);
    }

    private static PanelInstanceHandle FindPanel(PanelHost host, string templateId, Entity scope)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.TemplateId == templateId && info.Scope == scope)
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException($"No panel '{templateId}' scoped at {scope}.");
    }

    private static UiNode? FindNodeByClass(UiNode node, string className)
    {
        foreach (string token in node.ClassNames)
        {
            if (string.Equals(token, className, StringComparison.Ordinal))
            {
                return node;
            }
        }

        foreach (UiNode child in node.Children)
        {
            if (FindNodeByClass(child, className) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static int CountNodesByClass(UiNode node, string className)
    {
        int count = 0;
        foreach (string token in node.ClassNames)
        {
            if (string.Equals(token, className, StringComparison.Ordinal))
            {
                count++;
            }
        }

        foreach (UiNode child in node.Children)
        {
            count += CountNodesByClass(child, className);
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "mods")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    /// <summary>
    /// Headless stand-in for the host loop's view controller: the seat table resolves
    /// PresentBinding rects from its resolution, exactly like the Raylib/Web hosts.
    /// </summary>
    private sealed class HeadlessViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public HeadlessViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 50f;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

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
    }
}
