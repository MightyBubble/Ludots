using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class MassFlowNavPlaygroundPlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "mass_flow_nav_playground";
    private const string ControllerName = "MassFlowNavController";
    private const string LeftMousePath = "<Mouse>/LeftButton";
    private const string RightMousePath = "<Mouse>/RightButton";
    private const int ExternalObstacleCount = 5;

    private static readonly string[] ShowcaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "MassFlowNavPlaygroundMod",
    };

    private static readonly QueryDescription AgentQuery = new QueryDescription()
        .WithAll<NavAgent2D, Team>()
        .WithNone<NavObstacle2D>();

    private static readonly QueryDescription FriendlySelectableQuery = new QueryDescription()
        .WithAll<NavAgent2D, Team, WorldPositionCm, SelectionSelectableTag>()
        .WithNone<NavObstacle2D>();

    private static readonly QueryDescription FlowGoalQuery = new QueryDescription()
        .WithAll<NavFlowGoal2D>();

    [Test]
    public void MassFlowNavPlayground_LoadsPanel_BindsSelection_Spawns20kAgents_AndSupportsBoxSelectAndHud()
    {
        string repoRoot = FindRepoRoot();
        string artifactRoot = Path.Combine(repoRoot, "artifacts", "acceptance", "mass-flow-nav-playground");
        string screenRoot = Path.Combine(artifactRoot, "screens");
        Directory.CreateDirectory(screenRoot);

        using var engine = CreateEngine(ShowcaseMods);
        var inputBackend = (TestInputBackend)engine.GetService(CoreServiceKeys.InputBackend)!;
        var mapping = (WorldScreenMapping)engine.GetService(CoreServiceKeys.ScreenProjector)!;
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1600f, 900f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

        LoadMap(engine, MapId, frames: 8);
        Tick(engine, 2);

        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("SelectionRuntime should be available.");
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer should be available.");
        GroundOverlayBuffer groundOverlay = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("GroundOverlayBuffer should be available.");
        PrimitiveDrawBuffer drawBuffer = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer should be available.");
        PrimitiveDrawBuffer snapshotBuffer = engine.GetService(CoreServiceKeys.PresentationVisualSnapshotBuffer)
            ?? throw new InvalidOperationException("PresentationVisualSnapshotBuffer should be available.");
        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(engine.World.IsAlive(localPlayer), Is.True, "Mass flow playground should install a local controller entity.");
        Assert.That(engine.World.TryGet(localPlayer, out Name localName), Is.True);
        Assert.That(localName.Value, Is.EqualTo(ControllerName));
        Assert.That(selection.GetSelectionCount(localPlayer, SelectionSetKeys.Ambient), Is.EqualTo(0));
        Assert.That(engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name], Is.EqualTo(SelectionViewKeys.Primary));

        int totalAgents = 0;
        int friendlyAgents = 0;
        int enemyAgents = 0;
        int selectableFriendlies = 0;
        foreach (ref var chunk in engine.World.Query(in AgentQuery))
        {
            var teams = chunk.GetSpan<Team>();
            foreach (var entityIndex in chunk)
            {
                totalAgents++;
                if (teams[entityIndex].Id == 1)
                {
                    friendlyAgents++;
                }
                else if (teams[entityIndex].Id == 2)
                {
                    enemyAgents++;
                }

                Entity entity = chunk.Entity(entityIndex);
                if (teams[entityIndex].Id == 1 &&
                    engine.World.Has<SelectionSelectableTag>(entity) &&
                    engine.World.TryGet(entity, out SelectionSelectableState selectable) &&
                    selectable.Enabled)
                {
                    selectableFriendlies++;
                }
            }
        }

        int flowGoalCount = 0;
        bool hasFlow0Goal = false;
        bool hasFlow1Goal = false;
        foreach (ref var chunk in engine.World.Query(in FlowGoalQuery))
        {
            var flowGoals = chunk.GetSpan<NavFlowGoal2D>();
            foreach (int entityIndex in chunk)
            {
                flowGoalCount++;
                if (flowGoals[entityIndex].FlowId == 0)
                {
                    hasFlow0Goal = true;
                }
                else if (flowGoals[entityIndex].FlowId == 1)
                {
                    hasFlow1Goal = true;
                }
            }
        }

        Assert.That(totalAgents, Is.EqualTo(20000));
        Assert.That(friendlyAgents, Is.EqualTo(10000));
        Assert.That(enemyAgents, Is.EqualTo(10000));
        Assert.That(selectableFriendlies, Is.EqualTo(10000));
        Assert.That(flowGoalCount, Is.EqualTo(2));
        Assert.That(hasFlow0Goal, Is.True);
        Assert.That(hasFlow1Goal, Is.True);
        Assert.That(drawBuffer.Count, Is.EqualTo(totalAgents + ExternalObstacleCount));
        Assert.That(snapshotBuffer.Count, Is.EqualTo(0));

        UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Mass flow playground panel should mount a UI scene.");
        scene.Layout(uiRoot.Width, uiRoot.Height);
        string sceneText = ExtractUiSceneText(scene);
        Assert.That(sceneText, Does.Contain("Mass Flow Nav Playground"));
        Assert.That(sceneText, Does.Contain("Respawn 20K"));
        Assert.That(sceneText, Does.Contain("Shared Flow"));
        Assert.That(sceneText, Does.Contain("Current Ludots Nav Gaps"));

        Vector2 selectedWorldPoint = GetFirstFriendlyWorldPoint(engine);
        Vector2 selectionScreen = mapping.WorldToScreen(WorldUnits.WorldCmToVisualMeters(new WorldCmInt2((int)selectedWorldPoint.X, (int)selectedWorldPoint.Y), yMeters: 0f));
        Vector2 dragFrom = selectionScreen - new Vector2(64f, 64f);
        Vector2 dragTo = selectionScreen + new Vector2(64f, 64f);
        DragSelect(inputBackend, dragFrom, dragTo, engine);

        int selectedCount = SelectionContextRuntime.GetCurrentCount(engine.World, engine.GlobalContext);
        int ambientCount = selection.GetSelectionCount(localPlayer, SelectionSetKeys.Ambient);
        string selectionDiag =
            $"selectionScreen={selectionScreen} dragFrom={dragFrom} dragTo={dragTo} ambientCount={ambientCount} currentCount={selectedCount}";
        TestContext.WriteLine(selectionDiag);
        Assert.That(selectedCount, Is.GreaterThan(0), $"CoreInput box selection should select at least one friendly unit. {selectionDiag}");
        Assert.That(CountGroundOverlayShape(groundOverlay, GroundOverlayShape.Ring), Is.GreaterThan(0), "Selected units should emit visible ground-ring feedback.");

        Entity commandedEntity = GetFirstSelectedEntity(engine);
        Vector2 commandedStartWorldCm = engine.World.Get<WorldPositionCm>(commandedEntity).Value.ToVector2();
        Vector2 commandTargetWorldCm = selectedWorldPoint + new Vector2(700f, 480f);
        RightClickCommand(
            inputBackend,
            commandTargetWorldCm,
            mapping.WorldToScreen(WorldUnits.WorldCmToVisualMeters(new WorldCmInt2((int)commandTargetWorldCm.X, (int)commandTargetWorldCm.Y), yMeters: 0f)),
            engine,
            () => engine.World.IsAlive(commandedEntity) &&
                  engine.World.TryGet(commandedEntity, out NavGoal2D goal) &&
                  goal.Kind == NavGoalKind2D.Point &&
                  !engine.World.Has<NavFlowBinding2D>(commandedEntity));
        TickUntil(
            engine,
            () => engine.World.IsAlive(commandedEntity) &&
                  engine.World.TryGet(commandedEntity, out NavGoal2D goal) &&
                  goal.Kind == NavGoalKind2D.Point &&
                  !engine.World.Has<NavFlowBinding2D>(commandedEntity),
            maxTicks: 12);

        Assert.That(engine.World.IsAlive(commandedEntity), Is.True);
        Assert.That(engine.World.TryGet(commandedEntity, out NavGoal2D commandedGoal), Is.True, "Selected unit should receive a point goal after RMB command.");
        Assert.That(commandedGoal.Kind, Is.EqualTo(NavGoalKind2D.Point));
        Assert.That(engine.World.Has<NavFlowBinding2D>(commandedEntity), Is.False, "Manual RMB command should detach the selected unit from shared flow binding.");
        TickUntil(
            engine,
            () => engine.World.IsAlive(commandedEntity) &&
                  Vector2.Distance(
                      engine.World.Get<WorldPositionCm>(commandedEntity).Value.ToVector2(),
                      commandedStartWorldCm) > 40f,
            maxTicks: 24);
        Vector2 commandedCurrentWorldCm = engine.World.Get<WorldPositionCm>(commandedEntity).Value.ToVector2();
        Assert.That(
            Vector2.Distance(commandedCurrentWorldCm, commandedStartWorldCm),
            Is.GreaterThan(40f),
            "Manual RMB command should advance the selected unit in world space, not only write NavGoal2D.");

        string[] overlayText = ExtractOverlayText(overlay);
        Assert.That(string.Join(Environment.NewLine, overlayText), Does.Contain("FPS"));
        Assert.That(string.Join(Environment.NewLine, overlayText), Does.Contain("Selected"));
        Assert.That(string.Join(Environment.NewLine, overlayText), Does.Contain("Ground"));

        string screenshotPath = Path.Combine(screenRoot, "mass-flow-nav-playground-panel.png");
        new SkiaUiRenderer().ExportPng(scene, screenshotPath, 1600, 900);
        Assert.That(File.Exists(screenshotPath), Is.True);

        File.WriteAllText(
            Path.Combine(artifactRoot, "battle-report.md"),
            string.Join(Environment.NewLine, new[]
            {
                "# Mass Flow Nav Playground Battle Report",
                string.Empty,
                "## Outcome",
                "- success: yes",
                "- verdict: the new mass-flow playground boots through formal launcher/mod wiring, spawns the 20k-agent external-reference scenario, binds the formal selection view to the local controller, and mounts the Ludots UI panel.",
                "- map: `mass_flow_nav_playground`",
                "- agents: `10000` friendly + `10000` enemy",
                "- flow goals: `2`",
                $"- playable selection: `{selectedCount}` units selected through CoreInput box selection",
                $"- screenshot: `screens/{Path.GetFileName(screenshotPath)}`",
            }));
    }

    private static GameEngine CreateEngine(params string[] modIds)
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
        var mapping = new WorldScreenMapping(
            screenCenter: new Vector2(800f, 450f),
            worldCenterCm: new Vector2(5000f, 5000f),
            pixelsPerCm: 0.10f);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1600f, 900f));
        engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)mapping);
        engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)mapping);
        engine.SetService(CoreServiceKeys.VisualHeightmap, CreateFlatHeightmap());
        engine.SetService(CoreServiceKeys.WorldSizeSpec, new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100));
        engine.Start();
        return engine;
    }

    private static void LoadMap(GameEngine engine, string mapId, int frames)
    {
        engine.LoadMap(mapId);
        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(mapId));
        Tick(engine, frames);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
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

        if (inputHandler.HasContext("Default_Gameplay"))
        {
            inputHandler.PushContext("Default_Gameplay");
        }

        if (inputHandler.HasContext("MassFlowNavPlayground.Controls"))
        {
            inputHandler.PushContext("MassFlowNavPlayground.Controls");
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, backend);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxTicks)
    {
        if (predicate())
        {
            return;
        }

        for (int i = 0; i < maxTicks; i++)
        {
            Tick(engine, 1);
            if (predicate())
            {
                return;
            }
        }
    }

    private static void DragSelect(TestInputBackend backend, Vector2 from, Vector2 to, GameEngine engine)
    {
        var input = (PlayerInputHandler)engine.GetService(CoreServiceKeys.InputHandler)!;

        backend.SetMousePosition(from);
        input.InjectAction("PointerPos", new Vector3(from.X, from.Y, 0f));
        Tick(engine, 1);

        backend.SetButton(LeftMousePath, true);
        input.InjectAction("PointerPos", new Vector3(from.X, from.Y, 0f));
        Tick(engine, 2);

        backend.SetMousePosition(to);
        input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
        Tick(engine, 2);

        backend.SetButton(LeftMousePath, false);
        backend.SetMousePosition(to);
        input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
        Tick(engine, 2);
    }

    private static void RightClickCommand(TestInputBackend backend, Vector2 targetWorldCm, Vector2 targetScreen, GameEngine engine, Func<bool>? completion = null)
    {
        var input = (PlayerInputHandler)engine.GetService(CoreServiceKeys.InputHandler)!;
        backend.SetMousePosition(targetScreen);
        input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y));
        input.InjectAction("PointerPos", new Vector3(targetScreen.X, targetScreen.Y, 0f));
        Tick(engine, 1);
        int attempts = completion == null ? 1 : 4;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            backend.SetMousePosition(targetScreen);
            input.InjectAction("PointerPos", new Vector3(targetScreen.X, targetScreen.Y, 0f));
            backend.SetButton(RightMousePath, true);
            input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y));
            Tick(engine, 2);

            backend.SetMousePosition(targetScreen);
            input.InjectAction("PointerPos", new Vector3(targetScreen.X, targetScreen.Y, 0f));
            backend.SetButton(RightMousePath, false);
            input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y));
            Tick(engine, 2);
            if (completion == null || completion())
            {
                return;
            }
        }
    }

    private static Vector2 GetFirstFriendlyWorldPoint(GameEngine engine)
    {
        foreach (ref var chunk in engine.World.Query(in FriendlySelectableQuery))
        {
            var teams = chunk.GetSpan<Team>();
            var positions = chunk.GetSpan<WorldPositionCm>();
            foreach (int entityIndex in chunk)
            {
                if (teams[entityIndex].Id == 1)
                {
                    return positions[entityIndex].Value.ToVector2();
                }
            }
        }

        throw new InvalidOperationException("No selectable friendly was spawned.");
    }

    private static Entity GetFirstSelectedEntity(GameEngine engine)
    {
        Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
        if (selected.Length <= 0)
        {
            throw new InvalidOperationException("Selection snapshot is empty.");
        }

        return selected[0];
    }

    private static int CountGroundOverlayShape(GroundOverlayBuffer overlay, GroundOverlayShape shape)
    {
        int count = 0;
        foreach (GroundOverlayItem item in overlay.GetSpan())
        {
            if (item.Shape == shape)
            {
                count++;
            }
        }

        return count;
    }

    private static void SetPointerButtonSnapshot(GameEngine engine, string actionId, Vector2 pointer, bool pressedThisFrame, bool isDown, bool releasedThisFrame)
    {
        var pointerButtons = (AuthoritativePointerButtonSnapshot)engine.GetService(CoreServiceKeys.AuthoritativePointerButtons)!;
        pointerButtons.SetState(
            actionId,
            new PointerButtonState(
                pointer,
                pointer,
                pointer,
                pointer,
                isDown: isDown,
                pressedThisFrame: pressedThisFrame,
                releasedThisFrame: releasedThisFrame,
                hasPressPointer: pressedThisFrame,
                hasReleasePointer: releasedThisFrame,
                hasLastDownPointer: isDown || releasedThisFrame));
    }

    private static string[] ExtractOverlayText(ScreenOverlayBuffer overlay)
    {
        var lines = new List<string>();
        foreach (ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines.ToArray();
    }

    private static IVisualHeightmap CreateFlatHeightmap()
    {
        return new VisualHeightmapRuntime(
            VisualHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(-10_000, -10_000, 20_000, 20_000),
                sampleColumns: 2,
                sampleRows: 2,
                new short[]
                {
                    0, 0,
                    0, 0,
                }));
    }

    private static string ExtractUiSceneText(UiScene scene)
    {
        if (scene.Root == null)
        {
            return string.Empty;
        }

        var writer = new StringBuilder();
        AppendUiNodeText(scene.Root, writer);
        return writer.ToString();
    }

    private static void AppendUiNodeText(UiNode node, StringBuilder writer)
    {
        if (!string.IsNullOrWhiteSpace(node.TextContent))
        {
            if (writer.Length > 0)
            {
                writer.AppendLine();
            }

            writer.Append(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            AppendUiNodeText(node.Children[i], writer);
        }
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
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
        public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool value) && value;
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

    private sealed class WorldScreenMapping : IScreenProjector, IScreenRayProvider
    {
        private readonly Vector2 _screenCenter;
        private readonly Vector2 _worldCenterCm;
        private readonly float _pixelsPerCm;

        public WorldScreenMapping(Vector2 screenCenter, Vector2 worldCenterCm, float pixelsPerCm)
        {
            _screenCenter = screenCenter;
            _worldCenterCm = worldCenterCm;
            _pixelsPerCm = pixelsPerCm;
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            float worldXcm = worldPosition.X * 100f;
            float worldYcm = worldPosition.Z * 100f;
            return new Vector2(
                _screenCenter.X + ((worldXcm - _worldCenterCm.X) * _pixelsPerCm),
                _screenCenter.Y + ((worldYcm - _worldCenterCm.Y) * _pixelsPerCm));
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            float worldX = ((screenPosition.X - _screenCenter.X) / _pixelsPerCm) + _worldCenterCm.X;
            float worldY = ((screenPosition.Y - _screenCenter.Y) / _pixelsPerCm) + _worldCenterCm.Y;
            return new ScreenRay(new Vector3(worldX / 100f, 10f, worldY / 100f), -Vector3.UnitY);
        }
    }
}
