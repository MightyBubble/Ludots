using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC.Acceptance
{
    [TestFixture]
    public sealed class CameraAcceptanceModTests
    {
        private const int BlendSettleFrames = 40;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_ClickGround_SpawnsConfiguredRandomBatch_AndEmitsCueMarkerThenExpires()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            AssertProjectionViewportState(engine);

            var backend = GetInputBackend(engine);
            int spawnBatch = GetProjectionSpawnCount(engine);
            Assert.That(spawnBatch, Is.EqualTo(CameraAcceptanceIds.ProjectionSpawnCountDefault));

            int beforeDummyCount = CountEntitiesByName(engine.World, "Dummy");
            ClickGround(engine, backend, new Vector2(3200f, 2000f));

            int afterDummyCount = CountEntitiesByName(engine.World, "Dummy");
            Assert.That(afterDummyCount, Is.EqualTo(beforeDummyCount + spawnBatch), "Ground click should enqueue the configured runtime spawn batch.");

            List<WorldCmInt2> positions = GetNamedEntityPositions(engine.World, "Dummy");
            Assert.That(positions.Count, Is.EqualTo(spawnBatch));
            Assert.That(CountDistinctPositions(positions), Is.GreaterThan(spawnBatch / 2), "Projection batch should distribute entities across many distinct positions.");
            Assert.That(HasNamedEntityAt(engine.World, "Dummy", new WorldCmInt2(3200, 2000)), Is.True, "Random scatter should still stay anchored to the clicked point.");
            Assert.That(AllPositionsWithinRadius(positions, new WorldCmInt2(3200, 2000), 1800), Is.True, "Projection scatter should remain near the clicked point.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);
            Assert.That(primitives!.Count, Is.GreaterThan(0), "Ground click should emit a transient performer marker.");
            var cueMarkerPosition = WorldUnits.WorldCmToVisualMeters(new WorldCmInt2(3200, 2000), yMeters: 0.15f);
            bool foundCueMarker = false;
            foreach (ref readonly var primitive in primitives.GetSpan())
            {
                if (primitive.Position == cueMarkerPosition)
                {
                    foundCueMarker = true;
                    break;
                }
            }
            Assert.That(foundCueMarker, Is.True, "Ground click should emit a transient cue marker at the raycast point.");

            Tick(engine, 60);
            foundCueMarker = false;
            foreach (ref readonly var primitive in primitives.GetSpan())
            {
                if (primitive.Position == cueMarkerPosition)
                {
                    foundCueMarker = true;
                    break;
                }
            }
            Assert.That(foundCueMarker, Is.False, "Transient marker should expire after its configured lifetime.");
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_DrawsBoardBoundsOverlay_AndGuidanceText()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var ground = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            Assert.That(ground, Is.Not.Null);
            Assert.That(CountGroundOverlays(ground!, GroundOverlayShape.Line), Is.EqualTo(4),
                "Projection acceptance should draw the current board boundary explicitly.");

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            Assert.That(overlay, Is.Not.Null);
            var overlayText = ExtractOverlayText(overlay!);
            Assert.That(overlayText, Has.Some.Contains("Projection bounds"));
            Assert.That(overlayText, Has.Some.Contains("clamped by Core"));
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_OutOfBoundsClick_SnapsToNearestBoundary_ThroughCoreProjectionGuard()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var backend = GetInputBackend(engine);
            int beforeDummyCount = CountEntitiesByName(engine.World, "Dummy");
            var (outsideScreen, expectedWorldCm) = FindClampedGroundClick(engine);

            ClickScreen(engine, backend, outsideScreen);
            Tick(engine, 1);

            int afterDummyCount = CountEntitiesByName(engine.World, "Dummy");
            Assert.That(afterDummyCount, Is.EqualTo(beforeDummyCount + 1), "Out-of-bounds projection clicks should still resolve to a bounded spawn.");
            Assert.That(HasNamedEntityAt(engine.World, "Dummy", expectedWorldCm), Is.True,
                "Out-of-bounds projection clicks should snap to the nearest board boundary.");

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            Assert.That(overlay, Is.Not.Null);
            var overlayText = ExtractOverlayText(overlay!);
            Assert.That(overlayText, Has.Some.Contains("clamped by Core"));
            Assert.That(string.Join(Environment.NewLine, overlayText), Does.Not.Contain("Fuse explicit-degrade"));
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_QEAdjustsSpawnBatch_ClampsAtZero_AndUpdatesReactivePanel()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            Assert.That(GetProjectionSpawnCount(engine), Is.EqualTo(CameraAcceptanceIds.ProjectionSpawnCountDefault));

            var backend = GetInputBackend(engine);
            PressButton(engine, backend, "<Keyboard>/q");
            Assert.That(GetProjectionSpawnCount(engine), Is.EqualTo(0), "Spawn batch should clamp to zero when decreased below the floor.");

            PressButton(engine, backend, "<Keyboard>/q");
            Assert.That(GetProjectionSpawnCount(engine), Is.EqualTo(0), "Spawn batch should remain at zero when already clamped.");

            PressButton(engine, backend, "<Keyboard>/e");
            PressButton(engine, backend, "<Keyboard>/e");
            Assert.That(GetProjectionSpawnCount(engine), Is.EqualTo(200), "Each E press should increase the spawn batch by 100.");

            string sceneText = ExtractUiSceneText(scene);
            Assert.That(sceneText, Does.Contain("Projection Spawn Batch: 200"));
            Assert.That(sceneText, Does.Contain("Q/E adjusts the batch by 100"));

            int beforeDummyCount = CountEntitiesByName(engine.World, "Dummy");
            ClickGround(engine, backend, new Vector2(3200f, 2000f));
            Assert.That(CountEntitiesByName(engine.World, "Dummy"), Is.EqualTo(beforeDummyCount + 200));
        }

        [Test]
        public void CameraAcceptanceMod_AcceptanceMaps_DrawFpsOverlayInTopRight()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(TryFindOverlayTextItem(overlay!, text => text.Contains("FPS=", StringComparison.Ordinal), out ScreenOverlayItem fpsItem, out string? fpsText), Is.True,
                "Acceptance overlay should expose FPS telemetry.");
            Assert.That(fpsText, Does.Contain("Camera Acceptance"));
            Assert.That(fpsItem.X, Is.GreaterThanOrEqualTo(1400), "FPS telemetry should render in the right-side HUD region.");
            Assert.That(fpsItem.Y, Is.LessThanOrEqualTo(24), "FPS telemetry should render near the top edge.");
            Assert.That(TryFindOverlayTextItem(overlay, text => text.Contains("Core cull=", StringComparison.Ordinal), out _, out _), Is.True,
                "Acceptance overlay should expose core camera/culling timing diagnostics.");
            Assert.That(TryFindOverlayTextItem(overlay, text => text.Contains("Terrain render=", StringComparison.Ordinal), out _, out _), Is.True,
                "Acceptance overlay should expose terrain render/build timing diagnostics.");
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_DisablesEntityHudBarsAndNumbers_InWorldHudPipeline()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            Assert.That(worldHud, Is.Not.Null);

            int barCount = 0;
            int textCount = 0;
            foreach (ref readonly var item in worldHud!.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                }
            }

            Assert.That(barCount, Is.EqualTo(0),
                "Camera acceptance should disable entity health bars at performer-config level so WorldHud bar items are not emitted.");
            Assert.That(textCount, Is.EqualTo(0),
                "Camera acceptance should disable entity health numbers at performer-config level so WorldHud text items are not emitted.");
        }

        [Test]
        public void CameraAcceptanceMod_DiagnosticsHotkeys_TogglePanelHud_AndSelectionText()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(renderDebug, Is.Not.Null);
            Assert.That(uiRoot.Scene, Is.Not.Null, "Projection acceptance should mount the reactive panel while Skia UI is enabled.");

            var backend = GetInputBackend(engine);

            PressButton(engine, backend, "<Keyboard>/f7");
            overlay!.Clear();
            Tick(engine, 1);
            Assert.That(TryFindOverlayTextItem(overlay, text => text.Contains("FPS=", StringComparison.Ordinal), out _, out _), Is.False,
                "F7 should disable the acceptance HUD overlay.");

            PressButton(engine, backend, "<Keyboard>/f6");
            Assert.That(renderDebug!.DrawSkiaUi, Is.False, "F6 should disable the Skia panel path.");
            Assert.That(uiRoot.Scene, Is.Null, "Disabling the panel should clear the mounted CameraAcceptance scene.");

            PressButton(engine, backend, "<Keyboard>/f6");
            Assert.That(renderDebug.DrawSkiaUi, Is.True, "Pressing F6 again should restore the Skia panel path.");
            Assert.That(uiRoot.Scene, Is.Not.Null, "Re-enabling the panel should remount the reactive scene.");

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Entity captain = FindEntityByName(engine.World, CameraAcceptanceIds.CaptainName);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            Assert.That(projector, Is.Not.Null);

            Vector2 heroScreen = ProjectEntity(engine, projector!, hero);
            Vector2 captainScreen = ProjectEntity(engine, projector, captain);
            DragMouse(engine, backend, "<Mouse>/LeftButton", heroScreen - new Vector2(24f, 24f), captainScreen + new Vector2(24f, 24f));

            overlay.Clear();
            Tick(engine, 1);
            var overlayText = ExtractOverlayText(overlay);
            Assert.That(overlayText, Does.Contain($"#{hero.Id}"), "Selection overlay text should be present before toggling it off.");

            PressButton(engine, backend, "<Keyboard>/f8");
            overlay.Clear();
            Tick(engine, 1);
            overlayText = ExtractOverlayText(overlay);
            Assert.That(overlayText, Does.Not.Contain($"#{hero.Id}"), "F8 should disable selection text overlays.");
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_ScreenSpaceBoxSelect_UpdatesSelectionBuffer_AndSelectionLabels()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Entity scout = FindEntityByName(engine.World, CameraAcceptanceIds.ScoutName);
            Entity captain = FindEntityByName(engine.World, CameraAcceptanceIds.CaptainName);
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));
            Assert.That(scout, Is.Not.EqualTo(Entity.Null));
            Assert.That(captain, Is.Not.EqualTo(Entity.Null));

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            Assert.That(projector, Is.Not.Null);

            Vector2 heroScreen = ProjectEntity(engine, projector!, hero);
            Vector2 captainScreen = ProjectEntity(engine, projector!, captain);

            var backend = GetInputBackend(engine);
            DragMouse(engine, backend, "<Mouse>/LeftButton", heroScreen - new Vector2(24f, 24f), captainScreen + new Vector2(24f, 24f));
            Tick(engine, 1);

            Entity local = GetLocalPlayer(engine);
            ref var selection = ref engine.World.Get<SelectionBuffer>(local);
            Assert.That(selection.Count, Is.EqualTo(3));
            Assert.That(selection.Contains(hero), Is.True);
            Assert.That(selection.Contains(scout), Is.True);
            Assert.That(selection.Contains(captain), Is.True);
            Assert.That(engine.World.Has<SelectedTag>(hero), Is.True);
            Assert.That(engine.World.Has<SelectedTag>(scout), Is.True);
            Assert.That(engine.World.Has<SelectedTag>(captain), Is.True);

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            Assert.That(overlay, Is.Not.Null);
            var overlayText = ExtractOverlayText(overlay!);
            Assert.That(overlayText, Does.Contain($"#{hero.Id}"));
            Assert.That(overlayText, Does.Contain($"#{scout.Id}"));
            Assert.That(overlayText, Does.Contain($"#{captain.Id}"));
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_BoxSelect_UsesVisualTransformHeightForScreenSpaceHitTesting()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Entity scout = FindEntityByName(engine.World, CameraAcceptanceIds.ScoutName);
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));
            Assert.That(scout, Is.Not.EqualTo(Entity.Null));

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            Assert.That(projector, Is.Not.Null);

            var sharedWorldCm = new WorldCmInt2(2600, 1600);
            ApplyEntityProjectionOverride(engine, hero, sharedWorldCm, WorldUnits.WorldCmToVisualMeters(sharedWorldCm, yMeters: 0f));
            ApplyEntityProjectionOverride(engine, scout, sharedWorldCm, WorldUnits.WorldCmToVisualMeters(sharedWorldCm, yMeters: 4.5f));

            Vector2 heroScreen = ProjectEntity(engine, projector!, hero);
            Vector2 scoutScreen = ProjectEntity(engine, projector!, scout);
            Assert.That(Vector2.Distance(heroScreen, scoutScreen), Is.GreaterThan(40f),
                "Regression setup must create a visible screen-space separation from VisualTransform height alone.");

            var backend = GetInputBackend(engine);
            Action<GameEngine> reapplyVisualOverrides = runtime =>
            {
                ApplyEntityProjectionOverride(runtime, hero, sharedWorldCm, WorldUnits.WorldCmToVisualMeters(sharedWorldCm, yMeters: 0f));
                ApplyEntityProjectionOverride(runtime, scout, sharedWorldCm, WorldUnits.WorldCmToVisualMeters(sharedWorldCm, yMeters: 4.5f));
            };

            DragMouse(
                engine,
                backend,
                "<Mouse>/LeftButton",
                scoutScreen - new Vector2(18f, 18f),
                scoutScreen + new Vector2(18f, 18f),
                reapplyVisualOverrides);

            Entity local = GetLocalPlayer(engine);
            ref var selection = ref engine.World.Get<SelectionBuffer>(local);
            Assert.That(selection.Count, Is.EqualTo(1));
            Assert.That(selection.Contains(scout), Is.True);
            Assert.That(selection.Contains(hero), Is.False);
            Assert.That(engine.World.Has<SelectedTag>(scout), Is.True);
            Assert.That(engine.World.Has<SelectedTag>(hero), Is.False);
        }

        [Test]
        public void CameraAcceptanceMod_Panel_DoesNotCaptureWorldClicksOutsideCard()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            Assert.That(uiRoot.Scene, Is.Not.Null, "Acceptance panel should mount when a UIRoot service is present.");

            bool handledOutside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 900f,
                Y = 420f
            });

            bool handledInside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 80f,
                Y = 80f
            });

            Assert.That(handledOutside, Is.False, "World clicks outside the panel card must pass through to gameplay input.");
            Assert.That(handledInside, Is.True, "Clicks on the panel card should remain UI-interactive.");
        }

        [Test]
        public void CameraAcceptanceMod_Panel_ReusesSameSceneReferenceAcrossPresentationTicks()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            long initialVersion = scene.Version;

            Tick(engine, 3);

            Assert.That(ReferenceEquals(scene, uiRoot.Scene), Is.True,
                "Camera acceptance panel must keep one mounted UiScene and update it reactively instead of remounting every presentation tick.");
            Assert.That(scene.Version, Is.EqualTo(initialVersion),
                "Idle presentation ticks should not trigger unnecessary panel recomposition.");
        }

        [Test]
        public void CameraAcceptanceMod_Panel_DoesNotRecompose_WhenOnlyViewportTelemetryChanges()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            long initialVersion = scene.Version;
            var culling = engine.GetService(CoreServiceKeys.CameraCullingDebugState);
            Assert.That(culling, Is.Not.Null);

            culling!.VisibleEntityCount += 17;
            Tick(engine, 1);

            Assert.That(scene.Version, Is.EqualTo(initialVersion),
                "Volatile viewport telemetry must stay out of the reactive acceptance panel so camera movement does not force panel recomposition.");
            Assert.That(ExtractUiSceneText(scene), Does.Contain("Viewport telemetry: top-right HUD"));
        }

        [Test]
        public void CameraAcceptanceMod_Panel_SelectionStateUpdatesWithinMountedReactiveScene()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            long initialVersion = scene.Version;

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Entity scout = FindEntityByName(engine.World, CameraAcceptanceIds.ScoutName);
            Entity captain = FindEntityByName(engine.World, CameraAcceptanceIds.CaptainName);
            SetSelectionBuffer(engine, hero, scout, captain);
            Tick(engine, 1);

            Assert.That(ReferenceEquals(scene, uiRoot.Scene), Is.True,
                "Reactive panel updates must preserve the mounted scene instance so pointer capture and focus are not reset.");
            Assert.That(scene.Version, Is.GreaterThan(initialVersion),
                "Selection changes should advance the reactive panel scene version.");

            string sceneText = ExtractUiSceneText(scene);
            Assert.That(sceneText, Does.Contain("Selection Buffer"));
            Assert.That(sceneText, Does.Contain($"#{hero.Id}"));
            Assert.That(sceneText, Does.Contain($"#{scout.Id}"));
            Assert.That(sceneText, Does.Contain($"#{captain.Id}"));
        }

        [Test]
        public void CameraAcceptanceMod_Panel_VirtualizesLargeSelectionList_AndReportsIncrementalDiffMetrics()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var backend = GetInputBackend(engine);
            ClickGround(engine, backend, new Vector2(3200f, 2000f));

            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            Tick(engine, 1);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            Assert.That(scene.FindByElementId("camera-selection-buffer-list"), Is.Not.Null);

            UiReactiveUpdateMetrics before = scene.LastReactiveUpdateMetrics;
            Entity[] selection = CreateAliveEntities(engine.World, SelectionBuffer.CAPACITY);
            SetSelectionBuffer(engine, selection);

            Tick(engine, 1);

            UiReactiveUpdateMetrics after = scene.LastReactiveUpdateMetrics;
            Assert.That(after.SceneVersion, Is.GreaterThan(before.SceneVersion));
            Assert.That(after.FullRemount, Is.False,
                "Large selection updates should stay on the retained incremental patch path instead of remounting the whole scene.");
            Assert.That(after.PatchedNodes, Is.GreaterThan(0));
            Assert.That(after.ReusedNodes, Is.GreaterThan(after.PatchedNodes));
            Assert.That(after.VirtualizedWindowCount, Is.EqualTo(1));
            Assert.That(after.VirtualizedTotalItems, Is.EqualTo(SelectionBuffer.CAPACITY));
            Assert.That(after.VirtualizedComposedItems, Is.GreaterThan(0));
            Assert.That(after.VirtualizedComposedItems, Is.LessThan(SelectionBuffer.CAPACITY),
                "The reactive panel should compose only the visible slice of a large selection list.");

            string sceneText = ExtractUiSceneText(scene);
            Assert.That(sceneText, Does.Contain("Visible:"));
            Assert.That(sceneText, Does.Contain($"#{selection[0].Id}"));
        }

        [Test]
        public void CameraAcceptanceMod_RtsMap_ComposesKeyboardEdgeGrabDragAndZoom()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.RtsMapId);

            var backend = GetInputBackend(engine);
            var start = engine.GameSession.Camera.State.TargetCm;
            float startDistance = engine.GameSession.Camera.State.DistanceCm;

            HoldButton(engine, backend, "<Keyboard>/w", frames: 3);
            var afterKeyboard = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterKeyboard, Is.Not.EqualTo(start), "WASD pan should move the RTS camera target.");

            backend.SetMousePosition(new Vector2(1919f, 540f));
            Tick(engine, 3);
            var afterEdge = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterEdge, Is.Not.EqualTo(afterKeyboard), "Edge pan should move the RTS camera target.");

            DragMouse(engine, backend, "<Mouse>/MiddleButton", new Vector2(960f, 540f), new Vector2(1060f, 540f));
            var afterDrag = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterDrag, Is.Not.EqualTo(afterEdge), "Middle-button grab drag should move the RTS camera target.");

            ScrollWheel(engine, backend, 5f);
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.LessThan(startDistance), "Mouse wheel should zoom the RTS camera.");
        }

        [Test]
        public void CameraAcceptanceMod_TpsMap_UsesRightMouseAimLookAndZoom()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.TpsMapId);
            Tick(engine, BlendSettleFrames);

            var backend = GetInputBackend(engine);
            float startYaw = engine.GameSession.Camera.State.Yaw;
            float startDistance = engine.GameSession.Camera.State.DistanceCm;

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.TpsCameraId));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));

            DragMouse(engine, backend, "<Mouse>/RightButton", new Vector2(960f, 540f), new Vector2(1010f, 500f));
            Assert.That(engine.GameSession.Camera.State.Yaw, Is.Not.EqualTo(startYaw), "Right-button aim look should rotate the TPS camera.");

            ScrollWheel(engine, backend, -3f);
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.GreaterThan(startDistance), "Mouse wheel should zoom the TPS camera.");
        }

        [TestCase("<Keyboard>/1", CameraAcceptanceIds.BlendCutCameraId, CameraBlendCurve.Cut, false)]
        [TestCase("<Keyboard>/2", CameraAcceptanceIds.BlendLinearCameraId, CameraBlendCurve.Linear, true)]
        [TestCase("<Keyboard>/3", CameraAcceptanceIds.BlendSmoothCameraId, CameraBlendCurve.SmoothStep, true)]
        public void CameraAcceptanceMod_BlendMap_ClickGround_ActivatesRequestedCurve(
            string curveKey,
            string expectedCameraId,
            CameraBlendCurve expectedCurve,
            bool expectedBlending)
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.BlendMapId);

            var backend = GetInputBackend(engine);
            PressButton(engine, backend, curveKey);
            ClickGround(engine, backend, new Vector2(4800f, 2600f));

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(expectedCameraId));
            Assert.That(brain.ActiveDefinition?.BlendCurve, Is.EqualTo(expectedCurve));
            Assert.That(brain.IsBlending, Is.EqualTo(expectedBlending));

            Tick(engine, BlendSettleFrames);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(4800f, 2600f)));
        }

        [Test]
        public void CameraAcceptanceMod_FollowMap_TracksSelectionAndStopsWithoutFallback()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.FollowMapId);

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.FollowCloseCameraId));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(1600f, 1200f)));

            Entity hero = FindEntityByName(engine.World, CameraAcceptanceIds.HeroName);
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            Assert.That(projector, Is.Not.Null);
            Vector2 heroScreen = ProjectEntity(engine, projector!, hero);

            var backend = GetInputBackend(engine);
            ClickScreen(engine, backend, heroScreen);
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1600f, 1200f)));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(1600f, 1200f)));

            ref var position = ref engine.World.Get<WorldPositionCm>(hero);
            position = WorldPositionCm.FromCm(2200, 1800);
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(2200f, 1800f)));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(2200f, 1800f)));

            ClickGround(engine, backend, new Vector2(1800f, 1300f));
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Null);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(2200f, 1800f)), "Losing the follow target should leave the camera in place.");
        }

        [Test]
        public void CameraAcceptanceMod_MapLoad_PreservesSpatialQueryServiceIdentity_ForPreRegisteredSystems()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var beforeLoad = engine.SpatialQueries;

            LoadMap(engine, CameraAcceptanceIds.FollowMapId);

            Assert.That(ReferenceEquals(beforeLoad, engine.SpatialQueries), Is.True,
                "Map load must hot-swap the spatial query backend on the shared service instead of replacing the service instance.");
            Assert.That(ReferenceEquals(beforeLoad, engine.GetService(CoreServiceKeys.SpatialQueryService)), Is.True,
                "GlobalContext must continue exposing the same shared spatial query service instance after map load.");
        }

        [Test]
        public void CameraAcceptanceMod_StackMap_ClearWalksBackThroughHigherPriorityShots()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.StackMapId);

            var backend = GetInputBackend(engine);
            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.TpsCameraId));
            Assert.That(brain.IsActive(CameraAcceptanceIds.TpsCameraId), Is.True);

            PressButton(engine, backend, "<Keyboard>/r");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackRevealShotId);
            Assert.That(brain.IsActive(CameraAcceptanceIds.TpsCameraId), Is.True);

            PressButton(engine, backend, "<Keyboard>/t");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackAlertShotId);
            Assert.That(brain.IsActive(CameraAcceptanceIds.StackRevealShotId), Is.True);

            PressButton(engine, backend, "<Keyboard>/enter");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackRevealShotId);

            PressButton(engine, backend, "<Keyboard>/enter");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.TpsCameraId);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            var view = new StubViewController(1920, 1080);
            engine.SetService(CoreServiceKeys.ViewController, view);
            var cameraAdapter = new StubCameraAdapter();
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);
            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.GlobalContext["Tests.CameraAcceptanceMod.HeadlessCamera"] = new HeadlessCameraRuntime(
                cameraPresenter,
                engine.GetService(CoreServiceKeys.PresentationFrameSetup));
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession?.PrimaryBoard, Is.Not.Null, $"{mapId} must declare a primary board.");
            Tick(engine, frames);
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
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            backend.SetMousePosition(new Vector2(960f, 540f));
            engine.GlobalContext["Tests.CameraAcceptanceMod.InputBackend"] = backend;
        }

        private static void Tick(GameEngine engine, int frames, Action<GameEngine>? beforeFrame = null)
        {
            for (int i = 0; i < frames; i++)
            {
                beforeFrame?.Invoke(engine);
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                UpdateHeadlessCamera(engine);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames = 60, Action<GameEngine>? beforeFrame = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, beforeFrame);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static void HoldButton(GameEngine engine, TestInputBackend backend, string path, int frames)
        {
            backend.SetButton(path, true);
            Tick(engine, frames);
            backend.SetButton(path, false);
            Tick(engine, 1);
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
        {
            backend.SetButton(path, true);
            Tick(engine, 2);
            backend.SetButton(path, false);
            Tick(engine, 2);
        }

        private static void ScrollWheel(GameEngine engine, TestInputBackend backend, float delta)
        {
            backend.SetMouseWheel(delta);
            Tick(engine, 1);
            backend.SetMouseWheel(0f);
            Tick(engine, 2);
        }

        private static void DragMouse(GameEngine engine, TestInputBackend backend, string holdPath, Vector2 from, Vector2 to, Action<GameEngine>? beforeFrame = null)
        {
            backend.SetMousePosition(from);
            Tick(engine, 1, beforeFrame);
            backend.SetButton(holdPath, true);
            Tick(engine, 2, beforeFrame);
            backend.SetMousePosition(to);
            Tick(engine, 2, beforeFrame);
            backend.SetButton(holdPath, false);
            Tick(engine, 2, beforeFrame);
        }

        private static void ClickGround(GameEngine engine, TestInputBackend backend, Vector2 worldPointCm)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            Assert.That(projector, Is.Not.Null, "Headless acceptance runtime must expose a screen projector.");
            Vector2 screenPosition = ProjectWorldPoint(engine, projector!, worldPointCm);
            ClickScreen(engine, backend, screenPosition);
        }

        private static void ClickScreen(GameEngine engine, TestInputBackend backend, Vector2 screenPoint)
        {
            backend.SetMousePosition(screenPoint);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 2);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 2);
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static int CountEntitiesByName(World world, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });
            return count;
        }

        private static Entity[] CreateAliveEntities(World world, int count)
        {
            var entities = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                entities[i] = world.Create();
            }
            return entities;
        }

        private static bool HasNamedEntityAt(World world, string name, in WorldCmInt2 worldCm)
        {
            bool found = false;
            int targetX = worldCm.X;
            int targetY = worldCm.Y;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            world.Query(in query, (ref Name entityName, ref WorldPositionCm position) =>
            {
                if (found)
                {
                    return;
                }

                if (!string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var positionCm = position.Value.ToWorldCmInt2();
                found = positionCm.X == targetX && positionCm.Y == targetY;
            });
            return found;
        }

        private static List<WorldCmInt2> GetNamedEntityPositions(World world, string name)
        {
            var positions = new List<WorldCmInt2>();
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            world.Query(in query, (ref Name entityName, ref WorldPositionCm position) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    positions.Add(position.Value.ToWorldCmInt2());
                }
            });

            return positions;
        }

        private static int CountDistinctPositions(List<WorldCmInt2> positions)
        {
            var distinct = new HashSet<long>();
            for (int i = 0; i < positions.Count; i++)
            {
                WorldCmInt2 position = positions[i];
                distinct.Add(((long)position.X << 32) | (uint)position.Y);
            }

            return distinct.Count;
        }

        private static bool AllPositionsWithinRadius(List<WorldCmInt2> positions, in WorldCmInt2 center, int radiusCm)
        {
            long radiusSquared = (long)radiusCm * radiusCm;
            for (int i = 0; i < positions.Count; i++)
            {
                int dx = positions[i].X - center.X;
                int dy = positions[i].Y - center.Y;
                long distanceSquared = ((long)dx * dx) + ((long)dy * dy);
                if (distanceSquared > radiusSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private static Entity GetLocalPlayer(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                   localObj is Entity local &&
                   engine.World.IsAlive(local)
                ? local
                : throw new InvalidOperationException("LocalPlayerEntity is missing.");
        }

        private static void SetSelectionBuffer(GameEngine engine, params Entity[] entities)
        {
            Entity local = GetLocalPlayer(engine);
            ref var selection = ref engine.World.Get<SelectionBuffer>(local);
            selection.Clear();
            for (int i = 0; i < entities.Length && i < SelectionBuffer.CAPACITY; i++)
            {
                if (entities[i] != Entity.Null)
                {
                    selection.Add(entities[i]);
                }
            }
        }

        private static Vector2 ProjectEntity(GameEngine engine, IScreenProjector projector, Entity entity)
        {
            if (engine.World.TryGet(entity, out VisualTransform transform))
            {
                Vector2 visualScreen = projector.WorldToScreen(transform.Position);
                Assert.That(float.IsFinite(visualScreen.X) && float.IsFinite(visualScreen.Y), Is.True,
                    $"Projected screen position for entity #{entity.Id} must stay finite.");
                return visualScreen;
            }

            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return ProjectWorldPoint(engine, projector, position.Value.ToVector2());
        }

        private static Vector2 ProjectWorldPoint(GameEngine engine, IScreenProjector projector, Vector2 worldPointCm)
        {
            var worldCm = new WorldCmInt2((int)MathF.Round(worldPointCm.X), (int)MathF.Round(worldPointCm.Y));
            Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(worldCm, yMeters: 0f));
            Assert.That(float.IsFinite(screen.X) && float.IsFinite(screen.Y), Is.True,
                $"World point {worldPointCm} must project to a finite screen coordinate in the acceptance camera frustum.");
            return screen;
        }

        private static WorldCmInt2 ResolveGroundPoint(GameEngine engine, Vector2 screenPoint, out bool wasClamped)
        {
            var rayProvider = engine.GetService(CoreServiceKeys.ScreenRayProvider);
            Assert.That(rayProvider, Is.Not.Null, "Headless acceptance runtime must expose a screen ray provider.");

            var bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            bool hit = GroundRaycastUtil.TryGetGroundWorldCmBounded(
                rayProvider!.GetRay(screenPoint),
                bounds,
                out var worldCm,
                out wasClamped);
            Assert.That(hit, Is.True, $"Screen point {screenPoint} must intersect the ground plane for acceptance input.");
            return worldCm;
        }

        private static (Vector2 screenPoint, WorldCmInt2 worldCm) FindClampedGroundClick(GameEngine engine)
        {
            var rayProvider = engine.GetService(CoreServiceKeys.ScreenRayProvider);
            Assert.That(rayProvider, Is.Not.Null, "Headless acceptance runtime must expose a screen ray provider.");

            var bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            var candidateXs = new[]
            {
                -50000f,
                -20000f,
                -10000f,
                -5000f,
                -2000f,
                -1000f,
                -400f,
                0f,
                960f,
                1920f,
                2320f,
                5000f,
                10000f,
                20000f,
                50000f
            };
            var candidateYs = new[] { 540f, 900f, 1200f, 1600f, 2400f, 5000f, 10000f, 20000f };

            for (int yIndex = 0; yIndex < candidateYs.Length; yIndex++)
            {
                for (int xIndex = 0; xIndex < candidateXs.Length; xIndex++)
                {
                    Vector2 candidate = new(candidateXs[xIndex], candidateYs[yIndex]);
                    bool hit = GroundRaycastUtil.TryGetGroundWorldCmBounded(
                        rayProvider!.GetRay(candidate),
                        bounds,
                        out var worldCm,
                        out bool wasClamped);
                    if (hit && wasClamped)
                    {
                        return (candidate, worldCm);
                    }
                }
            }

            throw new InvalidOperationException("Failed to find a deterministic screen-space click that exercises the core boundary clamp path.");
        }

        private static void ApplyEntityProjectionOverride(GameEngine engine, Entity entity, in WorldCmInt2 worldCm, Vector3 visualPosition)
        {
            var sharedWorldPosition = WorldPositionCm.FromCm(worldCm.X, worldCm.Y);
            ref var current = ref engine.World.Get<WorldPositionCm>(entity);
            current = sharedWorldPosition;

            ref var previous = ref engine.World.Get<PreviousWorldPositionCm>(entity);
            previous.Value = sharedWorldPosition.Value;

            ref var visual = ref engine.World.Get<VisualTransform>(entity);
            visual.Position = visualPosition;
        }

        private static void UpdateHeadlessCamera(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue("Tests.CameraAcceptanceMod.HeadlessCamera", out var runtimeObj) ||
                runtimeObj is not HeadlessCameraRuntime runtime)
            {
                return;
            }

            float alpha = runtime.PresentationFrameSetup?.GetInterpolationAlpha() ?? 1f;
            runtime.CameraPresenter.Update(engine.GameSession.Camera, alpha);
        }

        private static List<string> ExtractOverlayText(ScreenOverlayBuffer overlay)
        {
            var lines = new List<string>();
            foreach (var item in overlay.GetSpan())
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

            return lines;
        }

        private static int CountGroundOverlays(GroundOverlayBuffer overlays, GroundOverlayShape shape)
        {
            int count = 0;
            foreach (ref readonly var item in overlays.GetSpan())
            {
                if (item.Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryFindOverlayTextItem(
            ScreenOverlayBuffer overlay,
            Func<string, bool> predicate,
            out ScreenOverlayItem itemResult,
            out string? textResult)
        {
            foreach (ScreenOverlayItem item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (text == null || !predicate(text))
                {
                    continue;
                }

                itemResult = item;
                textResult = text;
                return true;
            }

            itemResult = default;
            textResult = null;
            return false;
        }

        private static string ExtractUiSceneText(UiScene scene)
        {
            if (scene.Root == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            AppendUiNodeText(scene.Root, sb);
            return sb.ToString();
        }

        private static void AppendUiNodeText(UiNode node, StringBuilder sb)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(node.TextContent);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                AppendUiNodeText(node.Children[i], sb);
            }
        }

        private static void AssertProjectionViewportState(GameEngine engine)
        {
            var culling = engine.GetService(CoreServiceKeys.CameraCullingDebugState);
            Assert.That(culling, Is.Not.Null, "Projection acceptance should surface core culling debug state.");
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(2500f, 1700f)),
                "Projection acceptance should start from the map-defined target so launcher adapters share the same initial framing.");

            bool hasCullTrackedNamedEntity = false;
            var query = new QueryDescription().WithAll<Name, CullState>();
            engine.World.Query(in query, (Entity entity, ref Name name, ref CullState cull) =>
            {
                hasCullTrackedNamedEntity = true;
            });

            Assert.That(hasCullTrackedNamedEntity, Is.True, "Projection acceptance should attach core CullState tracking to scenario entities.");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext["Tests.CameraAcceptanceMod.InputBackend"] as TestInputBackend
                ?? throw new InvalidOperationException("Test input backend is missing.");
        }

        private static int GetProjectionSpawnCount(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ProjectionSpawnCountKey, out var value) &&
                   value is int count
                ? count
                : CameraAcceptanceIds.ProjectionSpawnCountDefault;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;
            private float _mouseWheel;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public void SetMouseWheel(float wheel)
            {
                _mouseWheel = wheel;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => _mouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class HeadlessCameraRuntime
        {
            public HeadlessCameraRuntime(CameraPresenter cameraPresenter, PresentationFrameSetupSystem? presentationFrameSetup)
            {
                CameraPresenter = cameraPresenter;
                PresentationFrameSetup = presentationFrameSetup;
            }

            public CameraPresenter CameraPresenter { get; }
            public PresentationFrameSetupSystem? PresentationFrameSetup { get; }
        }

        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }

            public void UpdateCamera(in CameraRenderState3D state)
            {
                LastState = state;
            }
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
}
