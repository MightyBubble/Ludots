using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Launcher.Backend;
using Ludots.Presentation.Skia;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.ThreeC.Acceptance
{
    [TestFixture]
    [NonParallelizable]
    public sealed class CameraAcceptanceModTests
    {
        private const int BlendSettleFrames = 40;
        private const string CameraAcceptanceDiagnosticsServiceName = "CameraAcceptance.DiagnosticsState";

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
            Assert.That(afterDummyCount, Is.EqualTo(beforeDummyCount + GetProjectionSpawnCount(engine)),
                "Out-of-bounds projection clicks should still resolve to the configured bounded spawn batch.");
            Assert.That(HasNamedEntityAt(engine.World, "Dummy", expectedWorldCm), Is.True,
                "Out-of-bounds projection clicks should snap to the nearest board boundary.");
            WorldAabbCm bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            Assert.That(
                AllPositionsWithinBounds(GetNamedEntityPositions(engine.World, "Dummy"), bounds),
                Is.True,
                "Projection scatter must stay inside the active board boundary after Core clamps an out-of-bounds click.");

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
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Acceptance panel should mount when a UIRoot service is present.");
            scene.Layout(uiRoot.Width, uiRoot.Height);

            UiNode diagnosticsCard = scene.FindByElementId("camera-diagnostics-card")
                ?? throw new InvalidOperationException("Acceptance diagnostics card should be mounted inside the retained UI scene.");
            string sceneText = ExtractUiSceneText(scene);

            Assert.That(sceneText, Does.Contain("FPS="),
                "Acceptance diagnostics should expose FPS telemetry through the retained UI path.");
            Assert.That(sceneText, Does.Contain("Core cull="),
                "Acceptance diagnostics should expose core camera/culling timing diagnostics.");
            Assert.That(sceneText, Does.Contain("Terrain render="),
                "Acceptance diagnostics should expose terrain render/build timing diagnostics.");
            Assert.That(diagnosticsCard.Style.BackgroundColor.Alpha, Is.GreaterThan((byte)200),
                "Diagnostics telemetry should render on an opaque retained card instead of floating directly on the world.");
            Assert.That(diagnosticsCard.LayoutRect.Width, Is.GreaterThanOrEqualTo(440f),
                "Diagnostics telemetry should reserve a stable right-side card width so dense stats remain readable.");
            Assert.That(diagnosticsCard.LayoutRect.X, Is.GreaterThanOrEqualTo(1400f),
                "Diagnostics telemetry should render in the right-side HUD region.");
            Assert.That(diagnosticsCard.LayoutRect.Y, Is.LessThanOrEqualTo(24f),
                "Diagnostics telemetry should render near the top edge.");
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
            using var hudProjection = CreateHeadlessHudProjection(engine);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(screenHud, Is.Not.Null);
            Assert.That(textCatalog, Is.Not.Null);
            Assert.That(renderDebug, Is.Not.Null);
            Assert.That(uiRoot.Scene, Is.Not.Null, "Projection acceptance should mount the reactive panel while Skia UI is enabled.");
            Assert.That(uiRoot.Scene!.FindByElementId("camera-diagnostics-card"), Is.Not.Null,
                "Projection acceptance should mount the retained diagnostics card while the acceptance HUD is enabled.");

            var backend = GetInputBackend(engine);

            PressButton(engine, backend, "<Keyboard>/f7");
            Tick(engine, 1);
            Assert.That(uiRoot.Scene!.FindByElementId("camera-diagnostics-card"), Is.Null,
                "F7 should disable the retained acceptance diagnostics card.");

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
            TickWithHudProjection(engine, hudProjection, 1);
            var overlayText = ExtractOverlayText(overlay);
            Assert.That(overlayText, Does.Not.Contain($"#{hero.Id}"),
                "Selection labels should stay out of the top-most overlay lane so UI remains above them.");
            Assert.That(ContainsScreenHudEntityLabel(screenHud!, textCatalog!, hero.Id), Is.True,
                "Selection labels should be projected through the retained UnderUi HUD lane before toggling them off.");

            PressButton(engine, backend, "<Keyboard>/f8");
            overlay.Clear();
            TickWithHudProjection(engine, hudProjection, 1);
            overlayText = ExtractOverlayText(overlay);
            Assert.That(overlayText, Does.Not.Contain($"#{hero.Id}"), "F8 should disable selection text overlays.");
            Assert.That(ContainsScreenHudEntityLabel(screenHud, textCatalog, hero.Id), Is.False,
                "F8 should disable selection labels in the UnderUi HUD lane.");
        }

        [Test]
        [NonParallelizable]
        public void CameraAcceptanceMod_HotpathHarness_TogglesPresentationLanes_AndWritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "presentation-hotpath-harness");
            Directory.CreateDirectory(artifactDir);

            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            using var hudProjection = CreateHeadlessHudProjection(engine);
            using var nativeOverlay = CreateHeadlessNativeOverlayHarness(engine);

            LoadMap(engine, CameraAcceptanceIds.HotpathMapId);
            TickWithHudProjection(engine, hudProjection, 12);

            var backend = GetInputBackend(engine);
            var snapshots = new List<HotpathHarnessSnapshot>();

            HotpathHarnessSnapshot baseline = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "baseline_hotpath_defaults");
            snapshots.Add(baseline);

            Assert.That(uiRoot.Scene, Is.Not.Null, "Hotpath harness should keep the reactive panel mounted while panel rendering is enabled.");
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Presentation Hotpath"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Crowd/Culling ON"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Visible Entities"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Visible on screen:"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Terrain OFF"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Guides OFF"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Cam Crowd"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Cam Empty"));
            Assert.That(ExtractUiSceneText(uiRoot.Scene!), Does.Contain("Respawn 10k"));
            Assert.That(uiRoot.Scene!.FindByElementId("camera-visible-entity-list"), Is.Not.Null);
            Assert.That(uiRoot.Scene.TryGetVirtualWindow("camera-visible-entity-list", out UiVirtualWindow visibleWindow), Is.True);
            Assert.That(visibleWindow.TotalCount, Is.GreaterThan(0));
            Assert.That(visibleWindow.VisibleCount, Is.GreaterThan(0));
            Assert.That(baseline.CrowdCount, Is.EqualTo(CameraAcceptanceIds.HotpathCrowdTargetCount));
            Assert.That(baseline.CrowdCount, Is.GreaterThanOrEqualTo(10000));
            Assert.That(baseline.VisibleCrowdCount, Is.GreaterThan(0));
            Assert.That(baseline.WorldBarCount, Is.EqualTo(baseline.VisibleCrowdCount));
            Assert.That(baseline.SelectionLabelCount, Is.EqualTo(CameraAcceptanceIds.HotpathSelectionLabelLimit));
            Assert.That(baseline.WorldTextCount, Is.EqualTo(baseline.VisibleCrowdCount + baseline.SelectionLabelCount));
            Assert.That(baseline.ScreenBarCount, Is.GreaterThan(0));
            Assert.That(baseline.ScreenBarCount, Is.LessThanOrEqualTo(baseline.WorldBarCount));
            Assert.That(baseline.ScreenTextCount, Is.GreaterThan(0));
            Assert.That(baseline.ScreenTextCount, Is.LessThanOrEqualTo(baseline.WorldTextCount));
            Assert.That(CountWorldHudItemsWithoutStableIdentity(engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)!), Is.EqualTo(0),
                "Hotpath HUD world items must carry stable ids and dirty serials.");
            Assert.That(CountScreenHudItemsWithoutStableIdentity(engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)!), Is.EqualTo(0),
                "Projected hotpath HUD items must preserve stable ids and dirty serials.");
            Assert.That(baseline.DiagnosticsHudVisible, Is.True);
            Assert.That(baseline.OverlayDirtyLanes, Is.GreaterThan(0));
            Assert.That(baseline.OverlayRebuiltLanes, Is.GreaterThanOrEqualTo(0));
            Assert.That(baseline.OverlayTextLayoutCacheCount, Is.GreaterThan(0));
            Assert.That(engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)?.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)?.DroppedSinceClear, Is.EqualTo(0));
            Assert.That(baseline.TerrainEnabled, Is.False, "Hotpath map should start with terrain disabled so camera pan isolates HUD/primitives/culling cost.");
            Assert.That(baseline.GuidesEnabled, Is.False, "Hotpath map should start with reference guides disabled so grid/axis draw does not pollute the pan budget.");

            TickWithHudProjection(engine, hudProjection, 1);
            HotpathHarnessSnapshot steadyState = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "steady_state_same_view");
            snapshots.Add(steadyState);
            Assert.That(steadyState.OverlayDirtyLanes, Is.LessThan(baseline.OverlayDirtyLanes),
                "The retained overlay scene should shrink invalidation after the first full build on a stable camera view.");
            Assert.That(steadyState.OverlayRebuiltLanes, Is.LessThanOrEqualTo(baseline.OverlayRebuiltLanes),
                "The Skia overlay renderer should not rebuild more retained lanes once the stable baseline has been built.");

            PressButton(engine, backend, "<Keyboard>/f7");
            HotpathHarnessSnapshot hudOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "diag_hud_off");
            snapshots.Add(hudOff);
            Assert.That(hudOff.DiagnosticsHudVisible, Is.False, "F7 should disable the diagnostics HUD while leaving the hotpath scene alive.");
            Assert.That(hudOff.WorldBarCount, Is.EqualTo(baseline.WorldBarCount));
            Assert.That(hudOff.WorldTextCount, Is.EqualTo(baseline.WorldTextCount));

            PressButton(engine, backend, "<Keyboard>/f7");
            TickWithHudProjection(engine, hudProjection, 1);

            PressButton(engine, backend, "<Keyboard>/f8");
            HotpathHarnessSnapshot selectionOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "selection_labels_off");
            snapshots.Add(selectionOff);
            Assert.That(selectionOff.SelectionLabelCount, Is.EqualTo(0), "F8 should isolate selection-label overlay cost.");

            PressButton(engine, backend, "<Keyboard>/f9");
            HotpathHarnessSnapshot barsOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "bars_off");
            snapshots.Add(barsOff);
            Assert.That(barsOff.WorldBarCount, Is.EqualTo(0), "F9 should disable the hotpath HUD bar lane.");
            Assert.That(barsOff.WorldTextCount, Is.GreaterThan(0), "Disabling bars must not implicitly disable HUD text.");

            PressButton(engine, backend, "<Keyboard>/f10");
            HotpathHarnessSnapshot hudTextOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "hud_text_off");
            snapshots.Add(hudTextOff);
            Assert.That(hudTextOff.WorldTextCount, Is.EqualTo(0), "F10 should disable the hotpath HUD text lane.");
            Assert.That(hudTextOff.ScreenTextCount, Is.EqualTo(0));

            PressButton(engine, backend, "<Keyboard>/f11");
            HotpathHarnessSnapshot terrainOn = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "terrain_on");
            snapshots.Add(terrainOn);
            Assert.That(terrainOn.TerrainEnabled, Is.True, "F11 should re-enable terrain rendering inside the hotpath harness.");

            PressButton(engine, backend, "<Keyboard>/g");
            HotpathHarnessSnapshot guidesOn = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "guides_on");
            snapshots.Add(guidesOn);
            Assert.That(guidesOn.GuidesEnabled, Is.True, "G should re-enable reference guides inside the hotpath harness.");

            PressButton(engine, backend, "<Keyboard>/f12");
            HotpathHarnessSnapshot primitivesOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "primitives_off");
            snapshots.Add(primitivesOff);
            Assert.That(primitivesOff.PrimitivesEnabled, Is.False, "F12 should disable primitive rendering for manual adapter-side isolation.");

            PressButton(engine, backend, "<Keyboard>/c");
            TickWithHudProjection(engine, hudProjection, 6);
            HotpathHarnessSnapshot crowdOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "cull_crowd_off");
            snapshots.Add(crowdOff);
            Assert.That(crowdOff.CrowdCount, Is.EqualTo(0), "C should remove the deterministic crowd so culling cost can be isolated.");
            Assert.That(crowdOff.VisibleCrowdCount, Is.EqualTo(0));
            Assert.That(crowdOff.SelectionLabelCount, Is.EqualTo(0));

            PressButton(engine, backend, "<Keyboard>/f6");
            HotpathHarnessSnapshot panelOff = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "panel_off");
            snapshots.Add(panelOff);
            Assert.That(panelOff.PanelMounted, Is.False, "F6 should unmount the reactive panel in the hotpath harness as well.");

            PressButton(engine, backend, "<Keyboard>/f6");
            PressButton(engine, backend, "<Keyboard>/f8");
            PressButton(engine, backend, "<Keyboard>/f9");
            PressButton(engine, backend, "<Keyboard>/f10");
            PressButton(engine, backend, "<Keyboard>/f11");
            PressButton(engine, backend, "<Keyboard>/g");
            PressButton(engine, backend, "<Keyboard>/f12");
            PressButton(engine, backend, "<Keyboard>/c");
            TickWithHudProjection(engine, hudProjection, 12);

            HotpathHarnessSnapshot restored = CaptureHotpathSnapshot(engine, hudProjection, uiRoot, nativeOverlay, "restored_hotpath_defaults");
            snapshots.Add(restored);

            Assert.That(restored.PanelMounted, Is.True);
            Assert.That(restored.DiagnosticsHudVisible, Is.True);
            Assert.That(restored.CrowdCount, Is.EqualTo(CameraAcceptanceIds.HotpathCrowdTargetCount));
            Assert.That(restored.WorldBarCount, Is.EqualTo(restored.VisibleCrowdCount));
            Assert.That(restored.WorldTextCount, Is.EqualTo(restored.VisibleCrowdCount + restored.SelectionLabelCount));
            Assert.That(restored.ScreenBarCount, Is.GreaterThan(0));
            Assert.That(restored.ScreenBarCount, Is.LessThanOrEqualTo(restored.WorldBarCount));
            Assert.That(restored.ScreenTextCount, Is.GreaterThan(0));
            Assert.That(restored.ScreenTextCount, Is.LessThanOrEqualTo(restored.WorldTextCount));
            Assert.That(CountWorldHudItemsWithoutStableIdentity(engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)!), Is.EqualTo(0));
            Assert.That(CountScreenHudItemsWithoutStableIdentity(engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)!), Is.EqualTo(0));
            Assert.That(restored.SelectionLabelCount, Is.EqualTo(CameraAcceptanceIds.HotpathSelectionLabelLimit));
            Assert.That(restored.TerrainEnabled, Is.False);
            Assert.That(restored.GuidesEnabled, Is.False);
            Assert.That(restored.PrimitivesEnabled, Is.True);

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildHotpathTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildHotpathBattleReport(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildHotpathPathMermaid());
        }

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_ScreenSpaceBoxSelect_UpdatesSelectionBuffer_AndSelectionLabels()
        {
            using var engine = CreateEngine(AcceptanceMods);
            using var hudProjection = CreateHeadlessHudProjection(engine);
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
            TickWithHudProjection(engine, hudProjection, 1);

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
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(screenHud, Is.Not.Null);
            Assert.That(textCatalog, Is.Not.Null);
            overlay!.Clear();
            TickWithHudProjection(engine, hudProjection, 2);
            var overlayText = ExtractOverlayText(overlay);
            Assert.That(overlayText, Does.Not.Contain($"#{hero.Id}"));
            Assert.That(overlayText, Does.Not.Contain($"#{scout.Id}"));
            Assert.That(overlayText, Does.Not.Contain($"#{captain.Id}"));
            Assert.That(ContainsScreenHudEntityLabel(screenHud!, textCatalog!, hero.Id), Is.True);
            Assert.That(ContainsScreenHudEntityLabel(screenHud, textCatalog, scout.Id), Is.True);
            Assert.That(ContainsScreenHudEntityLabel(screenHud, textCatalog, captain.Id), Is.True);
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
            Assert.That(scene.FindByElementId("camera-diagnostics-card"), Is.Not.Null,
                "The retained diagnostics card should stay mounted inside the same UiScene across presentation ticks.");
            Assert.That(scene.Version, Is.GreaterThanOrEqualTo(initialVersion),
                "Presentation ticks may patch retained diagnostics content, but they must not remount a different UiScene.");
        }

        [Test]
        public void CameraAcceptanceMod_Panel_RecomposesWithinMountedScene_WhenViewportTelemetryChanges()
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

            Assert.That(ReferenceEquals(scene, uiRoot.Scene), Is.True,
                "Viewport telemetry changes should stay within the mounted retained scene instead of remounting a new one.");
            Assert.That(scene.Version, Is.GreaterThanOrEqualTo(initialVersion),
                "Retained diagnostics may throttle volatile text updates, but they must stay inside the mounted scene.");
            Assert.That(ExtractUiSceneText(scene), Does.Contain("Viewport telemetry: retained top-right diagnostics"));
            Assert.That(ExtractUiSceneText(scene), Does.Contain("vis="),
                "Retained diagnostics should surface culling telemetry inside the mounted scene.");
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
            var diagnostics = GetCameraAcceptanceDiagnostics(engine);
            Assert.That(scene.FindByElementId("camera-selection-buffer-list"), Is.Not.Null);
            Assert.That(scene.TryGetVirtualWindow("camera-selection-buffer-list", out UiVirtualWindow initialWindow), Is.True);
            Assert.That(initialWindow.TotalCount, Is.EqualTo(SelectionBuffer.CAPACITY));
            Assert.That(initialWindow.VisibleCount, Is.GreaterThan(0));
            Assert.That(initialWindow.VisibleCount, Is.LessThan(initialWindow.TotalCount));
            Assert.That(scene.LastReactiveUpdateMetrics.VirtualizedWindowCount, Is.EqualTo(1));
            Assert.That(scene.LastReactiveUpdateMetrics.VirtualizedTotalItems, Is.EqualTo(SelectionBuffer.CAPACITY));
            Assert.That(scene.LastReactiveUpdateMetrics.VirtualizedComposedItems, Is.EqualTo(initialWindow.VisibleCount));

            UiReactiveUpdateMetrics before = scene.LastReactiveUpdateMetrics;
            Entity[] selection = CreateAliveEntities(engine.World, SelectionBuffer.CAPACITY);
            SetSelectionBuffer(engine, selection);

            Tick(engine, 1);

            UiReactiveUpdateMetrics after = scene.LastReactiveUpdateMetrics;
            Assert.That(scene.TryGetVirtualWindow("camera-selection-buffer-list", out UiVirtualWindow selectionWindow), Is.True);
            Assert.That(after.SceneVersion, Is.GreaterThan(before.SceneVersion));
            Assert.That(after.FullRemount, Is.False,
                "Large selection updates should stay on the retained incremental patch path instead of remounting the whole scene.");
            Assert.That(after.PatchedNodes, Is.GreaterThan(0));
            Assert.That(after.ReusedNodes, Is.GreaterThan(after.PatchedNodes));
            Assert.That(after.VirtualizedWindowCount, Is.EqualTo(1));
            Assert.That(after.VirtualizedTotalItems, Is.EqualTo(SelectionBuffer.CAPACITY));
            Assert.That(after.VirtualizedComposedItems, Is.EqualTo(selectionWindow.VisibleCount));
            Assert.That(after.VirtualizedComposedItems, Is.LessThan(SelectionBuffer.CAPACITY),
                "The reactive panel should compose only the visible slice of a large selection list.");

            string sceneText = ExtractUiSceneText(scene);
            Assert.That(sceneText, Does.Contain("Visible:"));
            Assert.That(sceneText, Does.Contain($"#{selection[0].Id}"));

            scene.Layout(1920f, 1080f);
            UiNode scrollHost = scene.FindByElementId("camera-selection-buffer-list") ?? throw new InvalidOperationException("Selection buffer host should exist.");
            long incrementalBeforeScroll = diagnostics.PanelIncrementalPatchCount;
            bool handledScroll = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Scroll,
                X = scrollHost.LayoutRect.X + 12f,
                Y = scrollHost.LayoutRect.Y + 12f,
                DeltaY = 120f
            });

            Assert.That(handledScroll, Is.True, "The selection buffer should consume scroll input while the pointer is inside the list viewport.");

            Tick(engine, 1);

            Assert.That(scene.LastReactiveUpdateMetrics.Reason, Is.EqualTo(UiReactiveUpdateReason.RuntimeWindowChange));
            Assert.That(diagnostics.PanelLastSelectionRowsTouched, Is.EqualTo(0));
            Assert.That(diagnostics.PanelIncrementalPatchCount, Is.EqualTo(incrementalBeforeScroll + 1));
            Assert.That(scene.TryGetVirtualWindow("camera-selection-buffer-list", out UiVirtualWindow scrolledWindow), Is.True);
            Assert.That(scrolledWindow.StartIndex, Is.GreaterThan(0));
        }

        [Test]
        public void CameraAcceptanceMod_HotpathPanel_ReportsVisibleEntities_AndUpdatesWhenCameraViewChanges()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.HotpathMapId);
            Tick(engine, 12);

            UiScene scene = uiRoot.Scene ?? throw new InvalidOperationException("Hotpath panel should mount when a UIRoot service is present.");
            Assert.That(scene.FindByElementId("camera-visible-entity-list"), Is.Not.Null);
            Assert.That(scene.TryGetVirtualWindow("camera-visible-entity-list", out UiVirtualWindow initialWindow), Is.True);
            Assert.That(initialWindow.TotalCount, Is.GreaterThan(0));

            UiNode summaryNode = scene.FindByElementId("camera-visible-entity-summary") ?? throw new InvalidOperationException("Visible entity summary node should exist.");
            string beforeSummary = summaryNode.TextContent ?? string.Empty;
            string beforeFirstRow = scene.FindByElementId("camera-visible-row-0000")?.TextContent ?? string.Empty;
            long beforeVersion = scene.Version;

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                TargetCm = new Vector2(8400f, 6200f)
            });

            Tick(engine, 3);

            Assert.That(scene.Version, Is.GreaterThan(beforeVersion), "Changing the camera view should refresh the hotpath visible-entity panel.");
            Assert.That(scene.TryGetVirtualWindow("camera-visible-entity-list", out UiVirtualWindow updatedWindow), Is.True);
            Assert.That(updatedWindow.TotalCount, Is.GreaterThan(0));

            string afterSummary = (scene.FindByElementId("camera-visible-entity-summary")?.TextContent) ?? string.Empty;
            string afterFirstRow = scene.FindByElementId("camera-visible-row-0000")?.TextContent ?? string.Empty;
            Assert.That(afterSummary != beforeSummary || afterFirstRow != beforeFirstRow, Is.True,
                "Visible entity summary or first visible row should change after the camera view moves across the hotpath crowd.");
        }

        [Test]
        public void CameraAcceptanceHotpathEntryMod_LauncherResolve_UsesHotpathStartupMap()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"ludots-launcher-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var service = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var state = service.GetState();
                Assert.That(state.Bindings.Any(binding => string.Equals(binding.Name, "camera_acceptance_hotpath", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Repository launcher config should expose a dedicated hotpath binding.");
                Assert.That(state.Presets.Any(preset => string.Equals(preset.Id, "camera_acceptance_hotpath_raylib", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Repository launcher presets should expose a dedicated Raylib hotpath preset.");

                var result = service.Resolve(new[] { "$camera_acceptance_hotpath" }, LauncherPlatformIds.Raylib, LauncherBuildMode.Never);
                var startupSetting = result.Plan.Diagnostics.Settings.Single(setting => string.Equals(setting.Key, "startupMapId", StringComparison.OrdinalIgnoreCase));

                Assert.That(result.Plan.RootModIds, Is.EqualTo(new[] { "CameraAcceptanceHotpathEntryMod" }));
                Assert.That(result.Plan.OrderedModIds, Does.Contain("CameraAcceptanceMod"),
                    "Hotpath entry mod should compose the existing camera acceptance mod instead of replacing it.");
                Assert.That(startupSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(CameraAcceptanceIds.HotpathMapId),
                    "Launcher startup diagnostics should resolve directly to the hotpath map.");
                Assert.That(startupSetting.EffectiveSource, Does.Contain("CameraAcceptanceHotpathEntryMod").IgnoreCase,
                    "Hotpath entry mod game.json should be the winning startup source.");
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
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
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, view);
            var screenRayProvider = new CoreScreenRayProvider(engine.GameSession.Camera, view);
            screenProjector.BindPresenter(cameraPresenter);
            screenRayProvider.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, screenRayProvider);
            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view, timingDiagnostics);
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

        private static WorldHudToScreenSystem CreateHeadlessHudProjection(GameEngine engine)
        {
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var strings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector);
            var view = engine.GetService(CoreServiceKeys.ViewController);
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            Assert.That(worldHud, Is.Not.Null, "Headless hotpath acceptance requires WorldHudBatchBuffer.");
            Assert.That(screenHud, Is.Not.Null, "Headless hotpath acceptance requires ScreenHudBatchBuffer.");
            Assert.That(projector, Is.Not.Null, "Headless hotpath acceptance requires a screen projector.");
            Assert.That(view, Is.Not.Null, "Headless hotpath acceptance requires a view controller.");

            return new WorldHudToScreenSystem(engine.World, worldHud!, strings, projector!, view!, screenHud!, timings);
        }

        private static void TickWithHudProjection(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            int frames,
            Action<GameEngine>? beforeFrame = null)
        {
            for (int i = 0; i < frames; i++)
            {
                beforeFrame?.Invoke(engine);
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(1f / 60f);
                UpdateHeadlessCamera(engine);
                hudProjection.Update(1f / 60f);
            }
        }

        private static void TickUntilWithHudProjection(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            Func<bool> predicate,
            int maxFrames = 60,
            Action<GameEngine>? beforeFrame = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                TickWithHudProjection(engine, hudProjection, 1, beforeFrame);
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

        private static bool AllPositionsWithinBounds(List<WorldCmInt2> positions, in WorldAabbCm bounds)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                WorldCmInt2 position = positions[i];
                if (position.X < bounds.Left || position.X > bounds.Right || position.Y < bounds.Top || position.Y > bounds.Bottom)
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

        private static HotpathHarnessSnapshot CaptureHotpathSnapshot(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            UIRoot uiRoot,
            HeadlessNativeOverlayHarness nativeOverlay,
            string step)
        {
            TickWithHudProjection(engine, hudProjection, 1);

            if (engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is ScreenOverlayBuffer overlayBuffer)
            {
                overlayBuffer.Clear();
            }

            if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHudBuffer)
            {
                worldHudBuffer.Clear();
            }

            if (engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is ScreenHudBatchBuffer screenHudBuffer)
            {
                screenHudBuffer.Clear();
            }

            TickWithHudProjection(engine, hudProjection, 1);
            nativeOverlay.Capture();

            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var diagnostics = GetCameraAcceptanceDiagnostics(engine);
            var renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            Assert.That(overlay, Is.Not.Null);
            Assert.That(worldHud, Is.Not.Null);
            Assert.That(screenHud, Is.Not.Null);
            Assert.That(renderDebug, Is.Not.Null);
            Assert.That(timings, Is.Not.Null);

            var overlayText = ExtractOverlayText(overlay!);
            string panelText = uiRoot.Scene != null ? ExtractUiSceneText(uiRoot.Scene) : string.Empty;
            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            bool diagnosticsHudVisible = uiRoot.Scene?.FindByElementId("camera-diagnostics-card") != null;

            return new HotpathHarnessSnapshot(
                Step: step,
                Tick: engine.GameSession?.CurrentTick ?? 0,
                CrowdCount: CountEntitiesByNameOnMap(engine.World, currentMapId, "Dummy"),
                VisibleCrowdCount: CountVisibleEntitiesByNameOnMap(engine.World, currentMapId, "Dummy"),
                WorldBarCount: CountWorldHudItems(worldHud!, WorldHudItemKind.Bar),
                WorldTextCount: CountWorldHudItems(worldHud, WorldHudItemKind.Text),
                ScreenBarCount: CountScreenHudItems(screenHud!, WorldHudItemKind.Bar),
                ScreenTextCount: CountScreenHudItems(screenHud, WorldHudItemKind.Text),
                SelectionLabelCount: diagnostics.HotpathSelectionLabelCount,
                PanelMounted: uiRoot.Scene != null,
                DiagnosticsHudVisible: diagnosticsHudVisible,
                PanelEnabled: renderDebug!.DrawSkiaUi,
                TerrainEnabled: renderDebug.DrawTerrain,
                GuidesEnabled: renderDebug.DrawDebugDraw,
                PrimitivesEnabled: renderDebug.DrawPrimitives,
                CameraCullingMs: timings!.CameraCullingMs,
                HudProjectionMs: timings.WorldHudProjectionMs,
                OverlayBuildMs: timings.ScreenOverlayBuildMs,
                OverlayDrawMs: timings.ScreenOverlayDrawMs,
                OverlayDirtyLanes: timings.ScreenOverlayDirtyLanesLastFrame,
                OverlayRebuiltLanes: timings.ScreenOverlayRebuiltLanesLastFrame,
                OverlayTextLayoutCacheCount: timings.ScreenOverlayTextLayoutCacheCount,
                DiagnosticsSummary: FindLineContaining(overlayText, "Build panel=") ?? FindLineContaining(panelText, "Build panel=") ?? "Build panel=unavailable",
                HotpathSummary: FindLineContaining(overlayText, "Hotpath crowd=") ?? FindLineContaining(panelText, "Crowd=") ?? "Hotpath summary unavailable");
        }

        private static HeadlessNativeOverlayHarness CreateHeadlessNativeOverlayHarness(GameEngine engine)
        {
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var worldHudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            var textCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);
            var screenOverlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            var timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            Assert.That(screenHud, Is.Not.Null, "Headless native overlay capture requires ScreenHudBatchBuffer.");
            Assert.That(screenOverlay, Is.Not.Null, "Headless native overlay capture requires ScreenOverlayBuffer.");
            Assert.That(timings, Is.Not.Null, "Headless native overlay capture requires PresentationTimingDiagnostics.");

            return new HeadlessNativeOverlayHarness(
                new PresentationOverlaySceneBuilder(screenHud!, worldHudStrings, textCatalog, localeSelection, screenOverlay),
                new PresentationOverlayScene(screenHud!.Capacity + ScreenOverlayBuffer.MaxItems),
                new SkiaOverlayRenderer(),
                SKSurface.Create(new SKImageInfo(1920, 1080))!,
                timings!);
        }

        private static int CountVisibleEntitiesByName(World world, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name, CullState>();
            world.Query(in query, (ref Name entityName, ref CullState cull) =>
            {
                if (cull.IsVisible && string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountEntitiesByNameOnMap(World world, MapId mapId, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name, MapEntity>();
            world.Query(in query, (ref Name entityName, ref MapEntity mapEntity) =>
            {
                if (string.Equals(mapEntity.MapId.Value, mapId.Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountVisibleEntitiesByNameOnMap(World world, MapId mapId, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name, CullState, MapEntity>();
            world.Query(in query, (ref Name entityName, ref CullState cull, ref MapEntity mapEntity) =>
            {
                if (string.Equals(mapEntity.MapId.Value, mapId.Value, StringComparison.OrdinalIgnoreCase) &&
                    cull.IsVisible &&
                    string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountWorldHudItems(WorldHudBatchBuffer worldHud, WorldHudItemKind kind)
        {
            int count = 0;
            foreach (ref readonly var item in worldHud.GetSpan())
            {
                if (item.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountScreenHudItems(ScreenHudBatchBuffer screenHud, WorldHudItemKind kind)
        {
            int count = 0;
            foreach (ref readonly var item in screenHud.GetSpan())
            {
                if (item.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountWorldHudItemsWithoutStableIdentity(WorldHudBatchBuffer worldHud)
        {
            int count = 0;
            foreach (ref readonly var item in worldHud.GetSpan())
            {
                if (item.StableId <= 0 || item.DirtySerial <= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountScreenHudItemsWithoutStableIdentity(ScreenHudBatchBuffer screenHud)
        {
            int count = 0;
            foreach (ref readonly var item in screenHud.GetSpan())
            {
                if (item.StableId <= 0 || item.DirtySerial <= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsScreenHudEntityLabel(ScreenHudBatchBuffer screenHud, PresentationTextCatalog textCatalog, int entityId)
        {
            int tokenId = textCatalog.GetTokenId(WellKnownHudTextKeys.EntityId);
            foreach (ref readonly var item in screenHud.GetSpan())
            {
                if (item.Kind != WorldHudItemKind.Text ||
                    item.Text.TokenId != tokenId ||
                    item.Text.ArgCount == 0 ||
                    item.Text.Arg0.Type != PresentationTextArgType.Int32)
                {
                    continue;
                }

                if (item.Text.Arg0.AsInt32() == entityId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountScreenHudEntityLabels(ScreenHudBatchBuffer screenHud, PresentationTextCatalog textCatalog)
        {
            int tokenId = textCatalog.GetTokenId(WellKnownHudTextKeys.EntityId);
            int count = 0;
            foreach (ref readonly var item in screenHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Text &&
                    item.Text.TokenId == tokenId &&
                    item.Text.ArgCount > 0 &&
                    item.Text.Arg0.Type == PresentationTextArgType.Int32)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOverlayEntityLabelLines(ScreenOverlayBuffer overlay)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            List<string> lines = ExtractOverlayText(overlay);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("#", StringComparison.Ordinal))
                {
                    unique.Add(lines[i]);
                }
            }

            return unique.Count;
        }

        private static bool ContainsOverlayText(IReadOnlyList<string> overlayLines, string token)
        {
            for (int i = 0; i < overlayLines.Count; i++)
            {
                if (overlayLines[i].Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? FindLineContaining(IReadOnlyList<string> lines, string token)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains(token, StringComparison.Ordinal))
                {
                    return lines[i];
                }
            }

            return null;
        }

        private static string? FindLineContaining(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(token, StringComparison.Ordinal))
                {
                    return lines[i];
                }
            }

            return null;
        }

        private static string BuildHotpathTraceJsonl(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                HotpathHarnessSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"camera-hotpath-{i + 1:000}",
                    step = snapshot.Step,
                    tick = snapshot.Tick,
                    crowd = snapshot.CrowdCount,
                    visible_crowd = snapshot.VisibleCrowdCount,
                    world_bars = snapshot.WorldBarCount,
                    world_text = snapshot.WorldTextCount,
                    screen_bars = snapshot.ScreenBarCount,
                    screen_text = snapshot.ScreenTextCount,
                    selection_labels = snapshot.SelectionLabelCount,
                    panel_mounted = snapshot.PanelMounted,
                    diagnostics_hud = snapshot.DiagnosticsHudVisible,
                    panel_enabled = snapshot.PanelEnabled,
                    terrain_enabled = snapshot.TerrainEnabled,
                    guides_enabled = snapshot.GuidesEnabled,
                    primitives_enabled = snapshot.PrimitivesEnabled,
                    camera_culling_ms = Math.Round(snapshot.CameraCullingMs, 4),
                    hud_projection_ms = Math.Round(snapshot.HudProjectionMs, 4),
                    overlay_build_ms = Math.Round(snapshot.OverlayBuildMs, 4),
                    overlay_draw_ms = Math.Round(snapshot.OverlayDrawMs, 4),
                    overlay_dirty_lanes = snapshot.OverlayDirtyLanes,
                    overlay_rebuilt_lanes = snapshot.OverlayRebuiltLanes,
                    overlay_text_layout_cache = snapshot.OverlayTextLayoutCacheCount,
                    diagnostics = snapshot.DiagnosticsSummary,
                    hotpath = snapshot.HotpathSummary,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildHotpathBattleReport(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            HotpathHarnessSnapshot baseline = snapshots[0];
            HotpathHarnessSnapshot restored = snapshots[^1];

            var timeline = new StringBuilder();
            for (int i = 0; i < snapshots.Count; i++)
            {
                HotpathHarnessSnapshot snapshot = snapshots[i];
                timeline.AppendLine(
                    $"- [T+{snapshot.Tick:000}] {snapshot.Step} | Crowd={snapshot.CrowdCount}/{snapshot.VisibleCrowdCount} | Bars={snapshot.WorldBarCount}->{snapshot.ScreenBarCount} | Text={snapshot.WorldTextCount}->{snapshot.ScreenTextCount} | Labels={snapshot.SelectionLabelCount} | Panel={(snapshot.PanelMounted ? "ON" : "OFF")} | HUD={(snapshot.DiagnosticsHudVisible ? "ON" : "OFF")} | Terr={(snapshot.TerrainEnabled ? "ON" : "OFF")} | Guides={(snapshot.GuidesEnabled ? "ON" : "OFF")} | Prims={(snapshot.PrimitivesEnabled ? "ON" : "OFF")} | Cull={snapshot.CameraCullingMs:F2}ms | HudProj={snapshot.HudProjectionMs:F2}ms | OverlayBuild={snapshot.OverlayBuildMs:F2}ms | OverlayDraw={snapshot.OverlayDrawMs:F2}ms | Dirty={snapshot.OverlayDirtyLanes} | Rebuilt={snapshot.OverlayRebuiltLanes}");
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: presentation-hotpath-harness");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: move the camera across a 10k+ hotpath crowd, read the live visible-entity panel, and isolate presentation lanes without leaving the shared acceptance scene.");
            sb.AppendLine("- Gameplay domain: CameraAcceptanceMod diagnostics / visible-entity panel / HUD bar / HUD text / selection label / primitive / culling crowd lanes.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_hotpath.json`");
            sb.AppendLine($"- Crowd: `{CameraAcceptanceIds.HotpathCrowdTargetCount}` deterministic Dummy entities from the runtime spawn queue.");
            sb.AppendLine("- Clock profile: fixed `1/60s` headless acceptance ticks with explicit `WorldHudToScreenSystem` projection.");
            sb.AppendLine("- Controls: RTS manual camera movement plus `F6 panel`, `F7 diagnostics HUD`, `F8 selection labels`, `F9 bars`, `F10 HUD text`, `F11 terrain`, `G guides`, `F12 primitives`, `C crowd`.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the shared hotpath map and wait for the deterministic crowd to spawn.");
            sb.AppendLine("2. Verify the panel exposes the current visible entities for the active camera view.");
            sb.AppendLine("3. Capture the baseline with all presentation lanes enabled.");
            sb.AppendLine("4. Toggle diagnostics HUD, selection labels, bars, HUD text, terrain, guides, primitives, crowd, and panel one by one.");
            sb.AppendLine("5. Restore the hotpath defaults and verify the same scene returns to the baseline shape.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: the panel prints the visible entities for the current camera view, and each live toggle changes only its target lane or render gate while the rest of the scene remains stable.");
            sb.AppendLine("- Failure branch condition: visible-entity panel stays stale after view changes, bars/text/selection survive after their toggle, terrain/guides gates fail to flip, panel fails to unmount/remount, or crowd removal does not collapse the culling workload inputs.");
            sb.AppendLine("- Key metrics: crowd count, visible crowd count, world/screen HUD item counts, selection-label count, HUD buffer drops, culling timing, HUD projection timing, native overlay build/draw timing, dirty-lane count, and rebuilt-lane count.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/presentation-hotpath-harness/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/presentation-hotpath-harness/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/presentation-hotpath-harness/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: shared presentation hotpath harness keeps a 10k+ crowd, prints visible entities in the panel, and toggles diagnostics/bars/HUD text/terrain/guides/primitives/culling gates without changing the underlying scene contract.");
            sb.AppendLine($"- reason: baseline crowd `{baseline.CrowdCount}` and restored crowd `{restored.CrowdCount}` match, the panel keeps a visible-entity window alive, the hotpath defaults keep terrain/guides disabled for pan verification, and toggle snapshots show bars/text/labels/crowd collapsing to zero exactly when their lane is disabled.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshot count: `{snapshots.Count}`");
            sb.AppendLine($"- baseline visible crowd: `{baseline.VisibleCrowdCount}`");
            sb.AppendLine($"- baseline world bars/text: `{baseline.WorldBarCount}` / `{baseline.WorldTextCount}`");
            sb.AppendLine($"- restored world bars/text: `{restored.WorldBarCount}` / `{restored.WorldTextCount}`");
            sb.AppendLine($"- max culling sample: `{MaxCameraCullingMs(snapshots):F2}` ms");
            sb.AppendLine($"- max HUD projection sample: `{MaxHudProjectionMs(snapshots):F2}` ms");
            sb.AppendLine($"- max native overlay build sample: `{MaxOverlayBuildMs(snapshots):F2}` ms");
            sb.AppendLine($"- max native overlay draw sample: `{MaxOverlayDrawMs(snapshots):F2}` ms");
            sb.AppendLine($"- baseline/reused dirty lanes: `{baseline.OverlayDirtyLanes}` -> `{snapshots[1].OverlayDirtyLanes}`");
            sb.AppendLine("- reusable wiring: `CameraAcceptanceHotpathLaneSystem`, `CameraAcceptancePanelController`, `CameraAcceptanceSelectionOverlaySystem`, `WorldHudToScreenSystem`, `PresentationTimingDiagnostics`");
            return sb.ToString();
        }

        private static string BuildHotpathPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load camera_acceptance_hotpath] --> B[Spawn deterministic crowd through RuntimeEntitySpawnQueue]",
                "    B --> C[Panel prints visible entities for the current camera view]",
                "    C --> D[Capture baseline panel + diagnostics + HUD lanes]",
                "    D --> E{Toggle diagnostics HUD / selection / bars / HUD text}",
                "    E -->|lane disabled| F[Expected lane count drops to zero]",
                "    E -->|lane left on| G[Other lane counts stay stable]",
                "    F --> H{Toggle terrain / guides render gates}",
                "    H -->|enabled| I[Terrain and reference guides reappear explicitly]",
                "    I --> J{Toggle primitives render gate}",
                "    J -->|off| K[RenderDebugState primitive gate flips immediately]",
                "    K --> L{Toggle crowd off}",
                "    L -->|crowd=0| M[Visible crowd and label counts collapse]",
                "    M --> N{Toggle panel off then restore hotpath defaults}",
                "    N -->|restored| O[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static float MaxCameraCullingMs(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            float max = 0f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].CameraCullingMs > max)
                {
                    max = snapshots[i].CameraCullingMs;
                }
            }

            return max;
        }

        private static float MaxHudProjectionMs(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            float max = 0f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].HudProjectionMs > max)
                {
                    max = snapshots[i].HudProjectionMs;
                }
            }

            return max;
        }

        private static float MaxOverlayBuildMs(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            float max = 0f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].OverlayBuildMs > max)
                {
                    max = snapshots[i].OverlayBuildMs;
                }
            }

            return max;
        }

        private static float MaxOverlayDrawMs(IReadOnlyList<HotpathHarnessSnapshot> snapshots)
        {
            float max = 0f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].OverlayDrawMs > max)
                {
                    max = snapshots[i].OverlayDrawMs;
                }
            }

            return max;
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

        private static CameraAcceptanceDiagnosticsProbe GetCameraAcceptanceDiagnostics(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CameraAcceptanceDiagnosticsServiceName, out var diagnostics) ||
                diagnostics == null)
            {
                throw new InvalidOperationException("Camera acceptance diagnostics state is missing.");
            }

            return new CameraAcceptanceDiagnosticsProbe(diagnostics);
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

        private readonly record struct HotpathHarnessSnapshot(
            string Step,
            long Tick,
            int CrowdCount,
            int VisibleCrowdCount,
            int WorldBarCount,
            int WorldTextCount,
            int ScreenBarCount,
            int ScreenTextCount,
            int SelectionLabelCount,
            bool PanelMounted,
            bool DiagnosticsHudVisible,
            bool PanelEnabled,
            bool TerrainEnabled,
            bool GuidesEnabled,
            bool PrimitivesEnabled,
            float CameraCullingMs,
            float HudProjectionMs,
            float OverlayBuildMs,
            float OverlayDrawMs,
            int OverlayDirtyLanes,
            int OverlayRebuiltLanes,
            int OverlayTextLayoutCacheCount,
            string DiagnosticsSummary,
            string HotpathSummary);

        private sealed class HeadlessNativeOverlayHarness : IDisposable
        {
            private readonly PresentationOverlaySceneBuilder _builder;
            private readonly PresentationOverlayScene _scene;
            private readonly SkiaOverlayRenderer _renderer;
            private readonly SKSurface _surface;
            private readonly PresentationTimingDiagnostics _timings;

            public HeadlessNativeOverlayHarness(
                PresentationOverlaySceneBuilder builder,
                PresentationOverlayScene scene,
                SkiaOverlayRenderer renderer,
                SKSurface surface,
                PresentationTimingDiagnostics timings)
            {
                _builder = builder;
                _scene = scene;
                _renderer = renderer;
                _surface = surface;
                _timings = timings;
            }

            public void Capture()
            {
                long buildStart = Stopwatch.GetTimestamp();
                _builder.Build(_scene);
                _timings.ObserveScreenOverlayBuild(
                    ToElapsedMs(buildStart),
                    _scene.DirtyLaneCount,
                    _scene.Count);

                _renderer.ResetFrameStats();
                long drawStart = Stopwatch.GetTimestamp();
                _surface.Canvas.Clear(SKColors.Transparent);
                _renderer.Render(_scene, _surface.Canvas, PresentationOverlayLayer.UnderUi);
                _renderer.Render(_scene, _surface.Canvas, PresentationOverlayLayer.TopMost);
                _timings.ObserveScreenOverlayDraw(
                    ToElapsedMs(drawStart),
                    _renderer.RebuiltLaneCountLastFrame,
                    _renderer.CachedTextLayoutCount);
            }

            public void Dispose()
            {
                _surface.Dispose();
                _renderer.Dispose();
            }

            private static double ToElapsedMs(long startTicks)
            {
                return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
            }
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

        private sealed class CameraAcceptanceDiagnosticsProbe
        {
            private readonly object _instance;
            private readonly Type _type;

            public CameraAcceptanceDiagnosticsProbe(object instance)
            {
                _instance = instance;
                _type = instance.GetType();
            }

            public ReactiveApplyMode PanelLastApplyMode => Get<ReactiveApplyMode>(nameof(PanelLastApplyMode));
            public int PanelLastPatchedNodes => Get<int>(nameof(PanelLastPatchedNodes));
            public int PanelLastSelectionRowsTouched => Get<int>(nameof(PanelLastSelectionRowsTouched));
            public int PanelRowPoolSize => Get<int>(nameof(PanelRowPoolSize));
            public long PanelFullRecomposeCount => Get<long>(nameof(PanelFullRecomposeCount));
            public long PanelIncrementalPatchCount => Get<long>(nameof(PanelIncrementalPatchCount));
            public int PanelVirtualizedWindowCount => Get<int>(nameof(PanelVirtualizedWindowCount));
            public int PanelVirtualizedTotalItems => Get<int>(nameof(PanelVirtualizedTotalItems));
            public int PanelVirtualizedComposedItems => Get<int>(nameof(PanelVirtualizedComposedItems));
            public int HotpathSelectionLabelCount => Get<int>(nameof(HotpathSelectionLabelCount));

            private T Get<T>(string propertyName)
            {
                PropertyInfo property = _type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"Camera acceptance diagnostics property '{propertyName}' was not found.");
                object? value = property.GetValue(_instance);
                if (value is T typed)
                {
                    return typed;
                }

                throw new InvalidOperationException($"Camera acceptance diagnostics property '{propertyName}' did not return {typeof(T).Name}.");
            }
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
