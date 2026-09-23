using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using EntityCommandPanelMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using MinimapControlMod;
using MinimapControlMod.Runtime;
using NUnit.Framework;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ThreeKingdomsScenarioPlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string ArtifactFolderName = "three-kingdoms-siege";
    private const string MapId = "road_network_showcase_chunked";
    private const string PlayableTestInputBackendKey = "Tests.ThreeKingdomsScenario.InputBackend";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "EntityCommandPanelMod",
        "EntityInfoPanelsMod",
        "MinimapControlMod",
        "RtsDemoMod",
        "RoadNetworkShowcaseMod",
        "ThreeKingdomsScenarioMod",
    };

    [Test]
    public void ThreeKingdomsScenario_WritesAcceptanceArtifacts_And_ValidatesCoreSiegeLoops()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
        string screensDir = Path.Combine(artifactDir, "screens");
        AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

        var frames = new List<UiAcceptanceEvidenceFrame>();
        var trace = new List<object>();
        var timeline = new List<string>();
        var frameTimesMs = new List<double>();

        ValidateSc2HudAndRoadMove(frames, trace, timeline, frameTimesMs, screensDir);
        ValidateTunnelTrapFillAndTransit(frames, trace, timeline, frameTimesMs, screensDir);
        ValidateGateWallAndLadderAssault(frames, trace, timeline, frameTimesMs, screensDir);

        File.WriteAllText(
            Path.Combine(artifactDir, "trace.jsonl"),
            string.Join(Environment.NewLine, trace.Select(item => JsonSerializer.Serialize(item))),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "battle-report.md"),
            BuildBattleReport(timeline, frameTimesMs),
            Encoding.UTF8);
        AcceptanceUiEvidenceWriter.WriteTimelineSheet(frames, screensDir, Path.Combine(artifactDir, "timeline.png"), "Three Kingdoms Siege Acceptance");
        AcceptanceUiEvidenceWriter.WriteFiveWOneHMarkdown(ArtifactFolderName, frames, Path.Combine(artifactDir, "5w1h.md"));
    }

    private static void ValidateSc2HudAndRoadMove(
        List<UiAcceptanceEvidenceFrame> frames,
        List<object> trace,
        List<string> timeline,
        List<double> frameTimesMs,
        string screensDir)
    {
        using var engine = CreatePlayableEngine();
        LoadMap(engine, frameTimesMs);

        var uiRoot = RequireUiRoot(engine);
        var panelSource = ResolveGasPanelSource(engine);
        var backend = GetInputBackend(engine);
        MinimapControlRuntime minimap = RequireMinimapRuntime(engine);
        Entity roadVanguard = FindEntity(engine.World, "Road Vanguard");

        Assert.That(
            engine.GlobalContext.TryGetValue(EntityCommandPanelShowcaseTheme.ContextKey, out object? themeObj) &&
            string.Equals(themeObj as string, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal),
            Is.True,
            "Siege scenario should force the shared EntityCommandPanel theme to SC2.");

        SelectEntity(engine, roadVanguard);
        Tick(engine, 4, frameTimesMs);
        Assert.That(minimap.Visible, Is.True, "Siege scenario should enable the shared minimap capability on load.");
        Assert.That(minimap.SignalCount, Is.GreaterThan(0), "Minimap should publish battlefield signals once the siege scene is live.");
        Assert.That(minimap.SelectedLabel, Is.EqualTo("Road Vanguard"), "Minimap perspective should track the selected siege detachment.");
        string[] slotLabels = ReadSlotLabels(panelSource, roadVanguard);
        Assert.That(slotLabels, Has.Some.EqualTo("Board"));
        Assert.That(slotLabels, Has.Some.EqualTo("Capture"));
        Assert.That(slotLabels, Has.Some.EqualTo("Climb"));
        Assert.That(uiRoot.Scene, Is.Not.Null, "SC2 command deck should mount into the shared UI root once a siege unit is selected.");
        SceneLayout(uiRoot);

        Vector2 firstSlotCenter = GetFirstInteractiveSlotCenter(uiRoot);
        MovePointer(uiRoot, firstSlotCenter);
        Assert.That(
            uiRoot.Scene!.QuerySelectorAll(".ecp-showcase-slot--interactive:hover").Count,
            Is.GreaterThan(0),
            "Hovering a siege command card should activate the shared hover pseudo state.");
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "command_hover_feedback",
            "T+001A",
            "Road Vanguard",
            "Hovering a SC2 command card lights the shared hover feedback state.",
            "road_network_showcase_chunked",
            "Validate pointer hover feedback on the formal EntityCommandPanel runtime.",
            "selector=.ecp-showcase-slot--interactive:hover"));
        trace.Add(CaptureTrace(engine, "command_hover_feedback", "Hover pseudo-state applied to the first interactive siege slot.", "Road Vanguard"));
        timeline.Add("[T+001A] Hover over the first SC2 command card activates the shared hover pseudo-state.");

        PointerDown(uiRoot, firstSlotCenter);
        Assert.That(
            uiRoot.Scene.QuerySelectorAll(".ecp-showcase-slot--interactive:active").Count,
            Is.GreaterThan(0),
            "Holding the pointer down on a siege command card should activate the shared active pseudo state.");
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "command_click_feedback",
            "T+001B",
            "Road Vanguard",
            "Pressing a SC2 command card shows immediate click/active feedback before order commit.",
            "road_network_showcase_chunked",
            "Validate mouse-down feedback on the formal EntityCommandPanel runtime.",
            "selector=.ecp-showcase-slot--interactive:active"));
        trace.Add(CaptureTrace(engine, "command_click_feedback", "Active pseudo-state applied while pressing the first interactive siege slot.", "Road Vanguard"));
        timeline.Add("[T+001B] Mouse-down on the first SC2 command card activates the shared active pseudo-state.");
        PointerUp(uiRoot, firstSlotCenter);

        PressButton(engine, backend, "<Keyboard>/q", frameTimesMs, holdFrames: 2);
        Tick(engine, 1, frameTimesMs);
        Assert.That(
            uiRoot.Scene.QuerySelectorAll(".ecp-showcase-slot--hotkey").Count,
            Is.GreaterThan(0),
            "Holding the mapped hotkey should mark a siege command card with hotkey feedback.");
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "command_hotkey_feedback",
            "T+001C",
            "Road Vanguard",
            "Holding the mapped hotkey lights the shared SC2 command card feedback state.",
            "road_network_showcase_chunked",
            "Validate keyboard feedback on the formal EntityCommandPanel runtime.",
            "selector=.ecp-showcase-slot--hotkey"));
        trace.Add(CaptureTrace(engine, "command_hotkey_feedback", "Hotkey feedback applied to the first interactive siege slot.", "Road Vanguard"));
        timeline.Add("[T+001C] Holding the mapped hotkey activates the shared hotkey feedback state on the SC2 command card.");
        ReleaseButton(engine, backend, "<Keyboard>/q", frameTimesMs);

        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "load_sc2_hud",
            "T+001",
            "Road Vanguard",
            "Scenario map loaded with SC2-aligned command deck and siege slots.",
            "road_network_showcase_chunked",
            "Validate formal HUD ownership and shared command-panel composition.",
            $"slots={string.Join(", ", slotLabels)}"));
        trace.Add(CaptureTrace(engine, "load_sc2_hud", "SC2 HUD and command slots ready.", "Road Vanguard", "Main Gate", "City Wall"));
        timeline.Add("[T+001] Siege map loaded | SC2 command deck mounted | Road Vanguard exposes Board/Capture/Climb.");

        Vector2 beforeMove = ReadWorldPosition(engine.World, roadVanguard);
        Vector2 moveTargetScreen = GetScreenPositionForWorld(engine, new Vector3(0f, 0f, 0f));
        RightClickWorld(engine, backend, moveTargetScreen, frameTimesMs);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<RoadMoveOrderRuntime>(roadVanguard),
            maxFrames: 24,
            "Road Vanguard should receive shared road-follow runtime after RMB move.",
            () => BuildRoadDiagnostics(engine, roadVanguard));
        Tick(engine, 180, frameTimesMs);

        Vector2 afterMove = ReadWorldPosition(engine.World, roadVanguard);
        Assert.That(Vector2.Distance(afterMove, beforeMove), Is.GreaterThan(500f), BuildRoadDiagnostics(engine, roadVanguard));
        Assert.That(engine.World.Get<RoadMoveOrderRuntime>(roadVanguard).LifecycleState, Is.Not.EqualTo(RoadMoveLifecycleState.None));

        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "road_move_runtime",
            "T+002",
            "Road Vanguard",
            "Shared road move expander converted RMB into road-follow runtime.",
            "siege field lane",
            "Prove the mod composes the existing road graph instead of a bespoke march stack.",
            BuildRoadDiagnostics(engine, roadVanguard)));
        trace.Add(CaptureTrace(engine, "road_move_runtime", "Road Vanguard advanced under road-follow runtime.", "Road Vanguard", "Siege Train"));
        timeline.Add("[T+002] Road Vanguard accepted RMB move, entered shared road-follow runtime, and advanced along the siege lane.");
    }

    private static void ValidateTunnelTrapFillAndTransit(
        List<UiAcceptanceEvidenceFrame> frames,
        List<object> trace,
        List<string> timeline,
        List<double> frameTimesMs,
        string screensDir)
    {
        using var engine = CreatePlayableEngine();
        LoadMap(engine, frameTimesMs);

        var uiRoot = RequireUiRoot(engine);
        var panelSource = ResolveGasPanelSource(engine);
        Entity sapper = FindEntity(engine.World, "Sapper Team");
        Entity tunnelEntrance = FindEntity(engine.World, "Tunnel Entrance");
        Entity tunnelExit = FindEntity(engine.World, "Tunnel Exit");
        Entity trench = FindEntity(engine.World, "Forward Trench");

        SelectEntity(engine, tunnelEntrance);
        Tick(engine, 2, frameTimesMs);
        string[] tunnelSlots = ReadSlotLabels(panelSource, tunnelEntrance);
        Assert.That(tunnelSlots, Has.Some.EqualTo("Set Exit"));
        Assert.That(tunnelSlots, Has.Some.EqualTo("Summon"));
        Assert.That(tunnelSlots, Has.Some.EqualTo("Evacuate"));

        PlaceEntity(engine.World, sapper, -4200f, -2400f);
        PlaceEntity(engine.World, tunnelExit, 2400f, -950f);
        Tick(engine, 2, frameTimesMs);

        CastAbility(engine, tunnelEntrance, tunnelExit, slot: 1);
        Tick(engine, 6, frameTimesMs);

        CastAbility(engine, sapper, tunnelEntrance, slot: 1);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(sapper) && engine.World.Get<ChildOf>(sapper).Parent == tunnelEntrance,
            maxFrames: 90,
            "Sapper should board the tunnel entrance.");

        CastAbility(engine, tunnelEntrance, tunnelEntrance, slot: 2);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(sapper) && engine.World.Get<ChildOf>(sapper).Parent == trench,
            maxFrames: 180,
            "Unfilled trench should trap the first tunnel transfer.",
            () => JsonSerializer.Serialize(CaptureTrace(engine, "tunnel_trap_debug", "Waiting for trench trap.", "Sapper Team", "Forward Trench", "Tunnel Entrance")));
        Assert.That(engine.World.Get<SelectionSelectableState>(sapper).Enabled, Is.False, "Trench-captured infantry should be temporarily unselectable.");

        SelectEntity(engine, trench);
        Tick(engine, 2, frameTimesMs);
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "tunnel_trench_trap",
            "T+003",
            "Sapper Team + Tunnel Entrance",
            "First tunnel transfer was intercepted and captured by the forward trench.",
            "Forward Trench",
            "Validate trench block/capture before fill is complete.",
            "Sapper Team boarded entrance, Summon fired, trench captured traveler."));
        trace.Add(CaptureTrace(engine, "tunnel_trench_trap", "First tunnel summon trapped in trench.", "Sapper Team", "Forward Trench", "Tunnel Entrance", "Tunnel Exit"));
        timeline.Add("[T+003] Tunnel exit linked | first summon intercepted by Forward Trench and trapped Sapper Team.");

        CastAbility(engine, sapper, trench, slot: 2);
        Tick(engine, 220, frameTimesMs);

        if (engine.World.Has<ChildOf>(sapper))
        {
            RelationOps.RemoveParent(engine.World, sapper);
        }

        PlaceEntity(engine.World, sapper, -4200f, -2400f);
        Tick(engine, 2, frameTimesMs);

        CastAbility(engine, sapper, tunnelEntrance, slot: 1);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(sapper) && engine.World.Get<ChildOf>(sapper).Parent == tunnelEntrance,
            maxFrames: 90,
            "Sapper Team should be able to re-board the tunnel entrance after the trench fill completes.");

        CastAbility(engine, tunnelEntrance, tunnelEntrance, slot: 2);
        TickUntil(
            engine,
            frameTimesMs,
            () => !engine.World.Has<ChildOf>(sapper) &&
                  Vector2.Distance(ReadWorldPosition(engine.World, sapper), ReadWorldPosition(engine.World, tunnelExit)) < 650f,
            maxFrames: 260,
            "Filled trench should allow the re-routed transfer to emerge near the exit.",
            () => BuildTransitDiagnostics(engine, sapper, tunnelEntrance, tunnelExit, trench));

        SelectEntity(engine, tunnelEntrance);
        Tick(engine, 2, frameTimesMs);
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "tunnel_fill_and_transit",
            "T+004",
            "Sapper Team",
            "Trench fill cleared the underground route and the re-routed transfer emerged by the exit.",
            "Tunnel network",
            "Validate trench fill, tunnel transport, and no-fallback underground routing.",
            BuildTransitDiagnostics(engine, sapper, tunnelEntrance, tunnelExit, trench)));
        trace.Add(CaptureTrace(engine, "tunnel_fill_and_transit", "Filled trench allowed successful tunnel emergence.", "Sapper Team", "Forward Trench", "Tunnel Exit"));
        timeline.Add("[T+004] Sapper Team filled the trench | rerouted summon emerged by Tunnel Exit.");
    }

    private static void ValidateGateWallAndLadderAssault(
        List<UiAcceptanceEvidenceFrame> frames,
        List<object> trace,
        List<string> timeline,
        List<double> frameTimesMs,
        string screensDir)
    {
        using var engine = CreatePlayableEngine();
        LoadMap(engine, frameTimesMs);

        var uiRoot = RequireUiRoot(engine);
        Entity roadVanguard = FindEntity(engine.World, "Road Vanguard");
        Entity sapper = FindEntity(engine.World, "Sapper Team");
        Entity ladderSquad = FindEntity(engine.World, "Ladder Squad");
        Entity climber = FindEntity(engine.World, "Climber Team");
        Entity ladder = FindEntity(engine.World, "Assault Ladder");
        Entity wall = FindEntity(engine.World, "City Wall");
        Entity gate = FindEntity(engine.World, "Main Gate");

        EnsureDetached(engine.World, ladderSquad);
        EnsureDetached(engine.World, climber);
        EnsureDetached(engine.World, roadVanguard);
        EnsureDetached(engine.World, ladder);
        PlaceEntity(engine.World, ladderSquad, 2050f, 1180f);
        PlaceEntity(engine.World, ladder, 2300f, 1300f);
        PlaceEntity(engine.World, climber, 2200f, 1300f);
        PlaceEntity(engine.World, roadVanguard, 2800f, -250f);
        Tick(engine, 4, frameTimesMs);

        CastAbility(engine, ladderSquad, ladder, slot: 1);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(ladderSquad) && engine.World.Get<ChildOf>(ladderSquad).Parent == ladder,
            maxFrames: 90,
            "Ladder Squad should board the assault ladder.",
            () => BuildAttachmentDiagnostics(engine, ladderSquad, ladder, wall));

        CastAbility(engine, ladder, wall, slot: 1);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(ladder) && engine.World.Get<ChildOf>(ladder).Parent == wall,
            maxFrames: 160,
            "Assault Ladder should latch onto the wall.",
            () => BuildAttachmentDiagnostics(engine, ladder, wall, ladderSquad));

        CastAbility(engine, ladder, ladder, slot: 2);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(ladderSquad) && engine.World.Get<ChildOf>(ladderSquad).Parent == wall,
            maxFrames: 160,
            "Deploy One should transfer a boarded squad from ladder to wall slots.",
            () => BuildAttachmentDiagnostics(engine, ladderSquad, wall, ladder));

        CastAbility(engine, climber, wall, slot: 3);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(climber) && engine.World.Get<ChildOf>(climber).Parent == wall,
            maxFrames: 320,
            "Climber Team should complete direct wall climb.",
            () => BuildAttachmentDiagnostics(engine, climber, wall, ladder));

        CastAbility(engine, roadVanguard, gate, slot: 2);
        TickUntil(
            engine,
            frameTimesMs,
            () => engine.World.Has<ChildOf>(roadVanguard) &&
                  engine.World.Get<ChildOf>(roadVanguard).Parent == gate &&
                  engine.World.Get<Team>(gate).Id == 1,
            maxFrames: 260,
            "Road Vanguard should capture the gate and re-parent into the gate host.",
            () => BuildGateDiagnostics(engine, roadVanguard, gate, wall, ladder));

        SelectEntity(engine, roadVanguard);
        Tick(engine, 2, frameTimesMs);
        frames.Add(AcceptanceUiEvidenceWriter.CaptureCompositeFrame(
            engine,
            uiRoot,
            screensDir,
            frames.Count + 1,
            "wall_ladder_gate_assault",
            "T+005",
            "Road Vanguard + Ladder Squad + Climber Team",
            "Ladder latched, one squad deployed, one infantry squad climbed directly, and Main Gate flipped to attacker control.",
            "City Wall / Main Gate",
            "Validate ladder attach/deploy, wall climb, and gate seizure in the formal mod runtime.",
            BuildGateDiagnostics(engine, roadVanguard, gate, wall, ladder)));
        trace.Add(CaptureTrace(engine, "wall_ladder_gate_assault", "Ladder, wall climb, and gate capture all succeeded.", "Road Vanguard", "Ladder Squad", "Climber Team", "Assault Ladder", "City Wall", "Main Gate"));
        timeline.Add("[T+005] Assault Ladder latched to City Wall | Ladder Squad deployed | Climber Team climbed | Road Vanguard captured Main Gate.");
    }

    private static GameEngine CreatePlayableEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

        InstallPlayableInput(engine);
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());

        var view = new StubViewController(1920f, 1080f);
        engine.SetService(CoreServiceKeys.ViewController, view);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, new CoreScreenRayProvider(engine.GameSession.Camera, view));
        engine.SetService(CoreServiceKeys.ScreenProjector, new CoreScreenProjector(engine.GameSession.Camera, view));

        engine.Start();

        var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
        engine.RegisterPresentationSystem(culling);
        engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
        return engine;
    }

    private static void LoadMap(GameEngine engine, List<double> frameTimesMs)
    {
        engine.LoadMap(MapId);
        Tick(engine, 8, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Siege scenario should load without trigger errors.");
    }

    private static void InstallPlayableInput(GameEngine engine)
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
        engine.GlobalContext[PlayableTestInputBackendKey] = backend;
    }

    private static UIRoot RequireUiRoot(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot was not installed.");
    }

    private static IEntityCommandPanelSource ResolveGasPanelSource(GameEngine engine)
    {
        var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
            ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
        Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
        return source;
    }

    private static MinimapControlRuntime RequireMinimapRuntime(GameEngine engine)
    {
        return engine.GetService(MinimapControlServiceKeys.Runtime)
            ?? throw new InvalidOperationException("MinimapControlRuntime service is missing.");
    }

    private static string[] ReadSlotLabels(IEntityCommandPanelSource source, Entity target)
    {
        var slots = new EntityCommandPanelSlotView[8];
        int slotCount = source.CopySlots(target, 0, slots);
        return slots.Take(slotCount)
            .Select(slot => slot.DisplayLabel ?? string.Empty)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
    }

    private static void SelectEntity(GameEngine engine, Entity target)
    {
        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("SelectionRuntime service is missing.");
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(engine.World.IsAlive(owner), Is.True, "Local player selection owner should exist.");
        Assert.That(engine.World.IsAlive(target), Is.True, "Selection target should exist.");

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, next);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.Ambient);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
    {
        var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
            ?? throw new InvalidOperationException("OrderQueue service is missing.");

        bool enqueued = orderQueue.TryEnqueue(new Order
        {
            OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
            PlayerId = 1,
            Actor = actor,
            Target = target,
            Args = new OrderArgs
            {
                I0 = slot
            },
            SubmitMode = OrderSubmitMode.Immediate
        });

        Assert.That(enqueued, Is.True, "Ability order should enqueue.");
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            var stopwatch = Stopwatch.StartNew();
            engine.Tick(DeltaTime);
            stopwatch.Stop();
            frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static void TickUntil(
        GameEngine engine,
        List<double> frameTimesMs,
        Func<bool> condition,
        int maxFrames,
        string because,
        Func<string>? onTimeout = null)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return;
            }

            Tick(engine, 1, frameTimesMs);
        }

        string failureMessage = because;
        if (onTimeout != null)
        {
            failureMessage += Environment.NewLine + onTimeout();
        }

        Assert.That(condition(), Is.True, failureMessage);
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext[PlayableTestInputBackendKey] as TestInputBackend
            ?? throw new InvalidOperationException("Scenario playable test input backend is missing.");
    }

    private static void RightClickWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
    {
        backend.SetMousePosition(screenPosition);
        Tick(engine, 1, frameTimesMs);
        backend.SetButton("<Mouse>/RightButton", true);
        Tick(engine, 2, frameTimesMs);
        backend.SetButton("<Mouse>/RightButton", false);
        Tick(engine, 3, frameTimesMs);
    }

    private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs, int holdFrames = 1)
    {
        backend.SetButton(path, true);
        Tick(engine, Math.Max(1, holdFrames), frameTimesMs);
    }

    private static void ReleaseButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
    {
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

        Assert.That(result, Is.Not.EqualTo(Entity.Null), $"Missing entity '{entityName}'.");
        return result;
    }

    private static Vector2 ReadWorldPosition(World world, Entity entity)
    {
        ref readonly var position = ref world.Get<WorldPositionCm>(entity);
        return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
    }

    private static Vector2 GetScreenPositionForWorld(GameEngine engine, Vector3 worldMeters)
    {
        var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
            ?? throw new InvalidOperationException("ScreenProjector was not installed.");
        return projector.WorldToScreen(worldMeters);
    }

    private static Vector2 GetFirstInteractiveSlotCenter(UIRoot uiRoot)
    {
        UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("UI scene must be mounted before slot probing.");
        SceneLayout(uiRoot);
        UiNode slot = scene.QuerySelectorAll(".ecp-showcase-slot--interactive").FirstOrDefault()
            ?? throw new InvalidOperationException("Interactive siege slot node was not found in the mounted command deck.");
        return new Vector2(
            slot.LayoutRect.X + (slot.LayoutRect.Width * 0.5f),
            slot.LayoutRect.Y + (slot.LayoutRect.Height * 0.5f));
    }

    private static void MovePointer(UIRoot uiRoot, Vector2 position)
    {
        HandlePointer(uiRoot, PointerAction.Move, position);
    }

    private static void PointerDown(UIRoot uiRoot, Vector2 position)
    {
        HandlePointer(uiRoot, PointerAction.Down, position);
    }

    private static void PointerUp(UIRoot uiRoot, Vector2 position)
    {
        HandlePointer(uiRoot, PointerAction.Up, position);
    }

    private static void HandlePointer(UIRoot uiRoot, PointerAction action, Vector2 position)
    {
        uiRoot.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = action,
            X = position.X,
            Y = position.Y
        });
        SceneLayout(uiRoot);
    }

    private static void SceneLayout(UIRoot uiRoot)
    {
        UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("UI scene must be mounted before layout.");
        scene.Layout(uiRoot.Width, uiRoot.Height);
    }

    private static void PlaceEntity(World world, Entity entity, float xCm, float yCm)
    {
        var position = Fix64Vec2.FromFloat(xCm, yCm);
        world.Set(entity, new WorldPositionCm { Value = position });
        if (world.Has<PreviousWorldPositionCm>(entity))
        {
            world.Set(entity, new PreviousWorldPositionCm { Value = position });
        }

        if (world.Has<Position2D>(entity))
        {
            world.Set(entity, new Position2D { Value = position });
        }
    }

    private static void EnsureDetached(World world, Entity entity)
    {
        if (world.Has<ChildOf>(entity))
        {
            RelationOps.RemoveParent(world, entity);
        }
    }

    private static object CaptureTrace(GameEngine engine, string step, string note, params string[] focusEntities)
    {
        var world = engine.World;
        return new
        {
            Step = step,
            Note = note,
            Focus = focusEntities.Select(name =>
            {
                Entity entity = FindEntity(world, name);
                Entity parent = world.Has<ChildOf>(entity) ? world.Get<ChildOf>(entity).Parent : Entity.Null;
                int teamId = world.Has<Team>(entity) ? world.Get<Team>(entity).Id : 0;
                bool selectable = world.Has<SelectionSelectableState>(entity) && world.Get<SelectionSelectableState>(entity).Enabled;
                Vector2 position = ReadWorldPosition(world, entity);
                return new
                {
                    Name = name,
                    EntityId = entity.Id,
                    PositionCm = new { X = position.X, Y = position.Y },
                    Parent = parent == Entity.Null ? string.Empty : (world.Has<Name>(parent) ? world.Get<Name>(parent).Value : $"#{parent.Id}"),
                    TeamId = teamId,
                    Selectable = selectable
                };
            }).ToArray()
        };
    }

    private static string BuildRoadDiagnostics(GameEngine engine, Entity actor)
    {
        ref readonly var runtime = ref engine.World.Get<RoadMoveOrderRuntime>(actor);
        Vector2 position = ReadWorldPosition(engine.World, actor);
        string lastOrder = engine.GlobalContext.TryGetValue("CoreInputMod.Debug.LastOrder", out object? orderObj) && orderObj is string order
            ? order
            : "<none>";
        string submit = engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out object? submitObj) && submitObj is string submitStatus
            ? submitStatus
            : "<none>";
        return $"pos=({position.X:0.##},{position.Y:0.##}) lifecycle={runtime.LifecycleState}/{runtime.FailureReason} activeOrder={runtime.ActiveOrderId} lastOrder={lastOrder} submit={submit}";
    }

    private static string BuildTransitDiagnostics(GameEngine engine, Entity actor, Entity entrance, Entity exit, Entity trench)
    {
        Vector2 actorPos = ReadWorldPosition(engine.World, actor);
        Vector2 exitPos = ReadWorldPosition(engine.World, exit);
        string parent = engine.World.Has<ChildOf>(actor)
            ? (engine.World.Has<Name>(engine.World.Get<ChildOf>(actor).Parent) ? engine.World.Get<Name>(engine.World.Get<ChildOf>(actor).Parent).Value : $"#{engine.World.Get<ChildOf>(actor).Parent.Id}")
            : "<none>";
        return $"actor=({actorPos.X:0.##},{actorPos.Y:0.##}) exit=({exitPos.X:0.##},{exitPos.Y:0.##}) parent={parent} entranceChildren={GetChildCount(engine.World, entrance)} trenchChildren={GetChildCount(engine.World, trench)}";
    }

    private static string BuildGateDiagnostics(GameEngine engine, Entity vanguard, Entity gate, Entity wall, Entity ladder)
    {
        string vanguardParent = engine.World.Has<ChildOf>(vanguard)
            ? engine.World.Get<Name>(engine.World.Get<ChildOf>(vanguard).Parent).Value
            : "<none>";
        return $"gateTeam={engine.World.Get<Team>(gate).Id} vanguardParent={vanguardParent} wallChildren={GetChildCount(engine.World, wall)} ladderChildren={GetChildCount(engine.World, ladder)}";
    }

    private static string BuildAttachmentDiagnostics(GameEngine engine, Entity actor, Entity host, Entity related)
    {
        static string DescribeEntity(World world, Entity entity)
        {
            string name = world.Has<Name>(entity) ? world.Get<Name>(entity).Value : $"#{entity.Id}";
            Vector2 position = ReadWorldPosition(world, entity);
            string parent = world.Has<ChildOf>(entity)
                ? (world.Has<Name>(world.Get<ChildOf>(entity).Parent) ? world.Get<Name>(world.Get<ChildOf>(entity).Parent).Value : $"#{world.Get<ChildOf>(entity).Parent.Id}")
                : "<none>";
            bool selectable = world.Has<SelectionSelectableState>(entity) && world.Get<SelectionSelectableState>(entity).Enabled;
            string roadRuntime = world.Has<RoadMoveOrderRuntime>(entity)
                ? world.Get<RoadMoveOrderRuntime>(entity).LifecycleState.ToString()
                : "<none>";
            return $"{name}: pos=({position.X:0.##},{position.Y:0.##}) parent={parent} selectable={selectable} children={GetChildCount(world, entity)} road={roadRuntime}";
        }

        return string.Join(" | ",
            DescribeEntity(engine.World, actor),
            DescribeEntity(engine.World, host),
            DescribeEntity(engine.World, related));
    }

    private static int GetChildCount(World world, Entity entity)
    {
        return world.Has<ChildrenBuffer>(entity) ? world.Get<ChildrenBuffer>(entity).Count : 0;
    }

    private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<double> frameTimesMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: three-kingdoms-siege");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: validate the formal Ludots siege mod, not a prototype translation.");
        sb.AppendLine("- Composition: shared SC2 command deck, RTS relation runtime, road graph runtime, minimap, info panels, and scenario-specific siege interaction systems.");
        sb.AppendLine();
        sb.AppendLine("## Covered Interactions");
        sb.AppendLine("1. SC2-themed HUD ownership and shared GAS command slots.");
        sb.AppendLine("2. Road graph marching via RMB input and shared road-follow runtime.");
        sb.AppendLine("3. Tunnel exit binding, trench interception, trench fill, and successful post-fill underground transport.");
        sb.AppendLine("4. Ladder boarding, ladder latch, one-by-one deployment, direct wall climb, and gate seizure.");
        sb.AppendLine();
        sb.AppendLine("## Determinism");
        sb.AppendLine("- Fixed-step simulation at 60 FPS.");
        sb.AppendLine($"- Map: `{MapId}`.");
        sb.AppendLine($"- Average frame time ms: {frameTimesMs.DefaultIfEmpty(0d).Average():F3}");
        sb.AppendLine($"- Peak frame time ms: {frameTimesMs.DefaultIfEmpty(0d).Max():F3}");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        for (int i = 0; i < timeline.Count; i++)
        {
            sb.AppendLine($"- {timeline[i]}");
        }
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine("- `artifacts/acceptance/three-kingdoms-siege/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/three-kingdoms-siege/battle-report.md`");
        sb.AppendLine("- `artifacts/acceptance/three-kingdoms-siege/5w1h.md`");
        sb.AppendLine("- `artifacts/acceptance/three-kingdoms-siege/timeline.png`");
        sb.AppendLine("- `artifacts/acceptance/three-kingdoms-siege/screens/*.png`");
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "assets")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition;

        public void SetButton(string path, bool isDown)
        {
            _buttons[path] = isDown;
        }

        public void SetMousePosition(Vector2 position)
        {
            _mousePosition = position;
        }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
        public Vector2 GetMousePosition() => _mousePosition;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class StubViewController : IViewController
    {
        public StubViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 60f;
        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }
}
