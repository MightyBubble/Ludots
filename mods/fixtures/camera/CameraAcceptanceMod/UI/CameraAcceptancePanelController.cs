using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using CameraAcceptanceMod.Runtime;
using CameraAcceptanceMod.Systems;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Systems;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace CameraAcceptanceMod.UI
{
    internal sealed class CameraAcceptancePanelController
    {
        private const float DiagnosticsAverageGlyphWidthPx = 6.25f;
        private const float DiagnosticsCardTop = 16f;
        private const float DiagnosticsCardMargin = 16f;
        private const string DiagnosticsCardId = "camera-diagnostics-card";
        private const int DiagnosticsRefreshIntervalTicks = 6;
        private const float PanelWidth = 500f;
        private const float SelectionBufferHeight = 180f;
        private const float SelectionRowHeight = 22f;
        private const int SelectionRowPoolSize = SelectionBuffer.CAPACITY;
        private const string SelectionBufferHostId = "camera-selection-buffer-list";
        private const float VisibleEntityBufferHeight = 220f;
        private const float VisibleEntityRowHeight = 20f;
        private const string VisibleEntityBufferHostId = "camera-visible-entity-list";
        private static readonly Vector2 CaptainOriginCm = new(3400f, 2200f);
        private static readonly Vector2 CaptainMovedCm = new(4200f, 2800f);
        private static readonly QueryDescription VisibleEntityQuery = new QueryDescription()
            .WithAll<Name, MapEntity, CullState, VisualTransform>();

        private readonly ReactivePage<CameraAcceptancePanelState> _page;
        private CameraAcceptancePanelState _lastState = CameraAcceptancePanelState.Empty;
        private GameEngine? _engine;
        private int _lastSelectionRowsTouched;
        private int _lastRowPoolSize = SelectionRowPoolSize;
        private string[] _cachedDiagnosticsLines = Array.Empty<string>();
        private string _cachedDiagnosticsMapId = string.Empty;
        private long _cachedDiagnosticsTick = -1;
        private int _cachedVisibleEntities = int.MinValue;
        private int _cachedProjectionSpawnCount = int.MinValue;
        private int _cachedHotpathCrowdCount = int.MinValue;
        private int _cachedHotpathVisibleCrowdCount = int.MinValue;
        private bool _cachedPanelEnabled = true;
        private bool _cachedHudEnabled = true;
        private bool _cachedSelectionTextEnabled = true;
        private bool _cachedHotpathBarsEnabled = true;
        private bool _cachedHotpathHudTextEnabled = true;
        private bool _cachedPrimitivesEnabled = true;
        private bool _cachedHotpathCullCrowdEnabled = true;

        public CameraAcceptancePanelController()
        {
            _page = new ReactivePage<CameraAcceptancePanelState>(CameraAcceptancePanelState.Empty, BuildRoot);
        }

        public UiScene Scene => _page.Scene;
        public ReactiveUpdateStats LastUpdateStats => _page.LastUpdateStats;
        public UiReactiveUpdateMetrics LastUpdateMetrics => _page.LastUpdateMetrics;
        public long FullRecomposeCount => _page.FullRecomposeCount;
        public long IncrementalPatchCount => _page.IncrementalPatchCount;
        public int LastSelectionRowsTouched => _lastSelectionRowsTouched;
        public int RowPoolSize => _lastRowPoolSize;

        public bool MountOrSync(UIRoot root, GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;

            bool changed = false;
            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                root.MountScene(_page.Scene);
                root.IsDirty = true;
                changed = true;
            }

            if (ApplyStateSnapshot(engine))
            {
                root.IsDirty = true;
                changed = true;
            }

            return changed;
        }

        public void ClearIfOwned(UIRoot root)
        {
            ArgumentNullException.ThrowIfNull(root);

            if (ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
            }

            _lastState = CameraAcceptancePanelState.Empty;
            _lastSelectionRowsTouched = 0;
            _lastRowPoolSize = SelectionRowPoolSize;
            _page.SetState(_ => CameraAcceptancePanelState.Empty);
            _engine = null;
            ResetDiagnosticsCache();
        }

        private UiElementBuilder BuildRoot(ReactiveContext<CameraAcceptancePanelState> context)
        {
            CameraAcceptancePanelState state = context.State;
            UiElementBuilder mainPanel;

            if (string.IsNullOrWhiteSpace(state.MapId))
            {
                mainPanel = Ui.Card(
                        Ui.Text("Camera Acceptance").FontSize(22f).Bold().Color("#F7FAFF"),
                        Ui.Text("No active acceptance map.").FontSize(13f).Color("#8EA2BD"))
                    .Width(PanelWidth)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(18f)
                    .Background("#101A29")
                    .Absolute(16f, DiagnosticsCardTop)
                    .ZIndex(20);
            }
            else
            {
                var children = new List<UiElementBuilder>
                {
                    Ui.Text("Camera Acceptance").FontSize(22f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.MapDescription).Id("camera-panel-map-description").FontSize(14f).Color("#D0D8E6").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Map: {state.MapId}").Id("camera-panel-map-id").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Camera: {state.ActiveCameraId}").Id("camera-panel-camera-id").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Mode: {state.ActiveModeId}").Id("camera-panel-mode-id").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Selection: {state.SelectedName}").Id("camera-panel-selection-name").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Selected IDs: {state.SelectedIdsSummary}").Id("camera-panel-selected-summary").FontSize(13f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Follow Target: {state.FollowTarget}").Id("camera-panel-follow-target").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text("Viewport telemetry: retained top-right diagnostics").Id("camera-panel-diagnostics-mode").FontSize(13f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Projection Spawn Batch: {state.ProjectionSpawnCount}").Id("camera-panel-projection-spawn").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text("Scenarios").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                            BuildMapButton("Proj", state.MapId == CameraAcceptanceIds.ProjectionMapId, CameraAcceptanceIds.ProjectionMapId),
                            BuildMapButton("Hotpath", state.MapId == CameraAcceptanceIds.HotpathMapId, CameraAcceptanceIds.HotpathMapId),
                            BuildMapButton("RTS", state.MapId == CameraAcceptanceIds.RtsMapId, CameraAcceptanceIds.RtsMapId),
                            BuildMapButton("TPS", state.MapId == CameraAcceptanceIds.TpsMapId, CameraAcceptanceIds.TpsMapId),
                            BuildMapButton("Blend", state.MapId == CameraAcceptanceIds.BlendMapId, CameraAcceptanceIds.BlendMapId),
                            BuildMapButton("Follow", state.MapId == CameraAcceptanceIds.FollowMapId, CameraAcceptanceIds.FollowMapId),
                            BuildMapButton("Stack", state.MapId == CameraAcceptanceIds.StackMapId, CameraAcceptanceIds.StackMapId))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Actions").FontSize(12f).Bold().Color("#F4C77D"),
                    BuildScenarioActions(state),
                    Ui.Text("How To Verify").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text(state.ControlsDescription).FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal)
                };

                if (string.Equals(state.MapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
                {
                    children.Add(BuildHotpathControls(state));
                    children.Add(BuildVisibleEntitiesSection(context, state.VisibleEntityRows, state.VisibleEntitySummary));
                }
                else
                {
                    children.Add(BuildSelectedIdsSection(context, state.SelectedIds));
                }

                mainPanel = Ui.Card(children.ToArray()).Width(PanelWidth)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(18f)
                    .Background("#101A29")
                    .Absolute(16f, DiagnosticsCardTop)
                    .ZIndex(20);
            }

            if (state.DiagnosticsLines.Length != 0)
            {
                mainPanel.Child(
                    BuildDiagnosticsSection(state)
                        .Absolute(MathF.Max(0f, state.DiagnosticsCardLeft - DiagnosticsCardMargin), -2f)
                        .ZIndex(50));
            }

            return mainPanel;
        }

        private static UiElementBuilder BuildDiagnosticsSection(CameraAcceptancePanelState state)
        {
            var children = new List<UiElementBuilder>
            {
                Ui.Text("Native Diagnostics").FontSize(14f).Bold().Color("#F7FAFF")
            };

            for (int i = 0; i < state.DiagnosticsLines.Length; i++)
            {
                children.Add(
                    Ui.Text(state.DiagnosticsLines[i])
                        .Id($"camera-diagnostics-line-{i:00}")
                        .FontSize(i == 0 ? 14f : 12f)
                        .Color(i == 0 ? "#F7FAFF" : "#C7D3E1")
                        .WhiteSpace(UiWhiteSpace.Normal));
            }

            return Ui.Card(children.ToArray())
                .Id(DiagnosticsCardId)
                .Padding(14f)
                .Gap(6f)
                .Radius(16f)
                .Background("#08111BE8")
                .ZIndex(30);
        }

        private static UiElementBuilder BuildSelectedIdsSection(ReactiveContext<CameraAcceptancePanelState> context, IReadOnlyList<string> selectedIds)
        {
            UiVirtualWindow window = context.GetVerticalVirtualWindow(
                SelectionBufferHostId,
                SelectionRowPoolSize,
                SelectionRowHeight,
                SelectionBufferHeight,
                overscan: 2);

            var rows = new List<UiElementBuilder>();
            if (window.LeadingSpacerExtent > 0.01f)
            {
                rows.Add(BuildSelectionSpacer(window.LeadingSpacerExtent));
            }

            for (int i = window.StartIndex; i < window.EndIndexExclusive; i++)
            {
                string? selectedId = i < selectedIds.Count ? selectedIds[i] : null;
                rows.Add(BuildSelectionRow(i, selectedId));
            }

            if (window.TrailingSpacerExtent > 0.01f)
            {
                rows.Add(BuildSelectionSpacer(window.TrailingSpacerExtent));
            }

            return Ui.Column(
                    Ui.Text("Selection Buffer").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text($"Selected Slots: {selectedIds.Count}/{SelectionRowPoolSize} | Visible: {FormatVisibleRange(window)}").Id("camera-selection-buffer-summary").FontSize(11f).Color("#8EA2BD"),
                    Ui.ScrollView(rows.ToArray())
                        .Id(SelectionBufferHostId)
                        .Height(SelectionBufferHeight)
                        .Padding(8f)
                        .Gap(4f)
                        .Radius(12f)
                        .Background("#0C1420"))
                .Gap(6f);
        }

        private static UiElementBuilder BuildSelectionRow(int index, string? selectedId)
        {
            bool occupied = !string.IsNullOrWhiteSpace(selectedId);
            return Ui.Row(
                    Ui.Text($"{index + 1:00}").FontSize(11f).Color("#587189"),
                    Ui.Text(occupied ? selectedId! : "empty")
                        .Id(GetSelectionRowId(index))
                        .FontSize(12f)
                        .Color(occupied ? "#D0D8E6" : "#62758C"))
                .Gap(8f);
        }

        private static UiElementBuilder BuildSelectionSpacer(float height)
        {
            return Ui.Spacer(height);
        }

        private static UiElementBuilder BuildVisibleEntitiesSection(
            ReactiveContext<CameraAcceptancePanelState> context,
            IReadOnlyList<string> visibleEntityRows,
            string summary)
        {
            UiVirtualWindow window = context.GetVerticalVirtualWindow(
                VisibleEntityBufferHostId,
                visibleEntityRows.Count,
                VisibleEntityRowHeight,
                VisibleEntityBufferHeight,
                overscan: 2);

            var rows = new List<UiElementBuilder>();
            if (window.LeadingSpacerExtent > 0.01f)
            {
                rows.Add(Ui.Spacer(window.LeadingSpacerExtent));
            }

            for (int i = window.StartIndex; i < window.EndIndexExclusive; i++)
            {
                rows.Add(
                    Ui.Text(visibleEntityRows[i])
                        .Id(GetVisibleEntityRowId(i))
                        .FontSize(12f)
                        .Color("#D0D8E6"));
            }

            if (window.TrailingSpacerExtent > 0.01f)
            {
                rows.Add(Ui.Spacer(window.TrailingSpacerExtent));
            }

            if (rows.Count == 0)
            {
                rows.Add(Ui.Text("none").FontSize(12f).Color("#62758C"));
            }

            return Ui.Column(
                    Ui.Text("Visible Entities").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text($"{summary} | Visible: {FormatVisibleRange(window)}").Id("camera-visible-entity-summary").FontSize(11f).Color("#8EA2BD"),
                    Ui.ScrollView(rows.ToArray())
                        .Id(VisibleEntityBufferHostId)
                        .Height(VisibleEntityBufferHeight)
                        .Padding(8f)
                        .Gap(4f)
                        .Radius(12f)
                        .Background("#0C1420"))
                .Gap(6f);
        }

        private static string GetSelectionRowId(int index)
        {
            return $"camera-selection-row-{index:00}";
        }

        private static string FormatVisibleRange(UiVirtualWindow window)
        {
            return window.VisibleCount <= 0
                ? "empty"
                : $"{window.StartIndex + 1}-{window.EndIndexExclusive}";
        }

        private UiElementBuilder BuildScenarioActions(CameraAcceptancePanelState state)
        {
            return state.MapId switch
            {
                CameraAcceptanceIds.ProjectionMapId => Ui.Text(
                        $"Left click ground to spawn a random scatter batch. Q/E adjusts the batch by {CameraAcceptanceIds.ProjectionSpawnCountStep}; current batch = {state.ProjectionSpawnCount}.")
                    .FontSize(12f)
                    .Color("#8EA2BD")
                    .WhiteSpace(UiWhiteSpace.Normal),
                CameraAcceptanceIds.HotpathMapId => Ui.Text(
                        $"This scene auto-builds a deterministic {CameraAcceptanceIds.HotpathCrowdTargetCount} crowd. Move the camera manually, verify the visible-entity list changes with culling, and toggle lanes live to isolate panel, HUD, text, primitive, and culling costs.")
                    .FontSize(12f)
                    .Color("#8EA2BD")
                    .WhiteSpace(UiWhiteSpace.Normal),
                CameraAcceptanceIds.BlendMapId => Ui.Row(
                        BuildActionButton("Cut", state.ActiveBlendCameraId == CameraAcceptanceIds.BlendCutCameraId, () => SetBlendCamera(CameraAcceptanceIds.BlendCutCameraId)),
                        BuildActionButton("Linear", state.ActiveBlendCameraId == CameraAcceptanceIds.BlendLinearCameraId, () => SetBlendCamera(CameraAcceptanceIds.BlendLinearCameraId)),
                        BuildActionButton("Smooth", state.ActiveBlendCameraId == CameraAcceptanceIds.BlendSmoothCameraId, () => SetBlendCamera(CameraAcceptanceIds.BlendSmoothCameraId)))
                    .Wrap()
                    .Gap(8f),
                CameraAcceptanceIds.FollowMapId => Ui.Column(
                        Ui.Row(
                                BuildActionButton("Close", state.ActiveModeId == CameraAcceptanceIds.FollowCloseModeId, () => SwitchViewMode(CameraAcceptanceIds.FollowCloseModeId)),
                                BuildActionButton("Wide", state.ActiveModeId == CameraAcceptanceIds.FollowWideModeId, () => SwitchViewMode(CameraAcceptanceIds.FollowWideModeId)))
                            .Wrap()
                            .Gap(8f),
                        Ui.Row(BuildActionButton("Move Captain", false, ToggleCaptainPosition))
                            .Wrap()
                            .Gap(8f))
                    .Gap(8f),
                CameraAcceptanceIds.StackMapId => Ui.Row(
                        BuildActionButton("Reveal", false, () => RequestVirtualCamera(CameraAcceptanceIds.StackRevealShotId, clear: false)),
                        BuildActionButton("Alert", false, () => RequestVirtualCamera(CameraAcceptanceIds.StackAlertShotId, clear: false)),
                        BuildActionButton("Clear", false, () => RequestVirtualCamera(id: null, clear: true)))
                    .Wrap()
                    .Gap(8f),
                CameraAcceptanceIds.RtsMapId => Ui.Row(BuildActionButton("RTS Mode", state.ActiveModeId == CameraAcceptanceIds.RtsModeId, () => SwitchViewMode(CameraAcceptanceIds.RtsModeId)))
                    .Wrap()
                    .Gap(8f),
                CameraAcceptanceIds.TpsMapId => Ui.Row(BuildActionButton("TPS Mode", state.ActiveModeId == CameraAcceptanceIds.TpsModeId, () => SwitchViewMode(CameraAcceptanceIds.TpsModeId)))
                    .Wrap()
                    .Gap(8f),
                _ => Ui.Text("Interact directly in world view for this scenario.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal)
            };
        }

        private UiElementBuilder BuildHotpathControls(CameraAcceptancePanelState state)
        {
            const string hotpathCameraGuide = "Camera probes: Crowd = dense view, Center = traversal midpoint, Empty = empty-frustum verification.";
            return Ui.Column(
                    Ui.Text("Presentation Hotpath").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text(state.VisibleEntitySummary)
                        .FontSize(12f)
                        .Color("#8EA2BD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(
                            $"HUD {OnOff(state.DiagnosticsHudEnabled)} | Selection {OnOff(state.SelectionTextEnabled)} | Bars {OnOff(state.HotpathBarsEnabled)} | HUD Text {OnOff(state.HotpathHudTextEnabled)}")
                        .FontSize(12f)
                        .Color("#8EA2BD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(
                            $"Primitives {OnOff(state.PrimitivesEnabled)} | Crowd/Culling {OnOff(state.HotpathCullCrowdEnabled)}")
                        .FontSize(12f)
                        .Color("#8EA2BD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(hotpathCameraGuide)
                        .FontSize(12f)
                        .Color("#8EA2BD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildActionButton("Panel", state.PanelEnabled, TogglePanel),
                            BuildActionButton("HUD", state.DiagnosticsHudEnabled, ToggleDiagnosticsHud),
                            BuildActionButton("Select", state.SelectionTextEnabled, ToggleSelectionText),
                            BuildActionButton("Bars", state.HotpathBarsEnabled, ToggleHotpathBars))
                        .Wrap()
                        .Gap(8f),
                    Ui.Row(
                            BuildActionButton("Text", state.HotpathHudTextEnabled, ToggleHotpathHudText),
                            BuildActionButton("Prims", state.PrimitivesEnabled, TogglePrimitives),
                            BuildActionButton("Crowd", state.HotpathCullCrowdEnabled, ToggleHotpathCullCrowd))
                        .Wrap()
                        .Gap(8f),
                    Ui.Row(
                            BuildActionButton("Cam Crowd", false, MoveHotpathCameraCrowd),
                            BuildActionButton("Cam Center", false, MoveHotpathCameraCenter),
                            BuildActionButton("Cam Empty", false, MoveHotpathCameraEmpty))
                        .Wrap()
                        .Gap(8f),
                    Ui.Row(
                            BuildActionButton("Respawn 10k", false, RespawnHotpathCrowd),
                            BuildActionButton("Clear Crowd", false, ClearHotpathCrowd))
                        .Wrap()
                        .Gap(8f))
                .Gap(8f);
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, Action onClick)
        {
            return Ui.Button(label, _ => onClick())
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5B441A" : "#121B29")
                .Color("#F7FAFF");
        }

        private UiElementBuilder BuildMapButton(string label, bool active, string mapId)
        {
            return Ui.Button(label, _ => LoadAcceptanceMap(mapId))
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#244E66" : "#182436")
                .Color(active ? "#F7FAFF" : "#C7D3E1");
        }

        private bool ApplyStateSnapshot(GameEngine engine)
        {
            CameraAcceptancePanelState next = CaptureState(engine);
            if (StateEquals(_lastState, next))
            {
                _lastSelectionRowsTouched = 0;
                return false;
            }

            _lastSelectionRowsTouched = CountSelectionRowChanges(ResolveTrackedRows(_lastState), ResolveTrackedRows(next));
            _lastRowPoolSize = ResolveTrackedRowPoolSize(next);
            _lastState = next;
            _page.SetState(_ => next);
            return true;
        }

        private CameraAcceptancePanelState CaptureState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!CameraAcceptanceIds.IsAcceptanceMap(mapId))
            {
                return CameraAcceptancePanelState.Empty;
            }

            string[] selectedIds = ResolveSelectedEntityIds(engine);
            string[] visibleEntityRows = string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase)
                ? ResolveVisibleEntityRows(engine, mapId)
                : Array.Empty<string>();
            CameraAcceptanceDiagnosticsState? diagnostics = engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState);
            RenderDebugState? renderDebug = engine.GetService(CoreServiceKeys.RenderDebugState);
            Vector2 viewport = ResolveViewportSize(engine);
            string[] diagnosticsLines = BuildDiagnosticsLines(engine, mapId, diagnostics, renderDebug);
            return new CameraAcceptancePanelState(
                mapId,
                CameraAcceptanceIds.DescribeMap(mapId),
                CameraAcceptanceIds.DescribeControls(mapId),
                engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId ?? "none",
                ResolveActiveModeId(engine),
                ResolveSelectedEntityName(engine) ?? "none",
                SummarizeSelectedIds(selectedIds),
                selectedIds,
                FormatVector(engine.GameSession.Camera.FollowTargetPositionCm),
                ResolveActiveBlendCameraId(engine),
                CameraAcceptanceRuntime.ResolveProjectionSpawnCount(engine),
                BuildVisibleEntitySummary(visibleEntityRows),
                visibleEntityRows,
                viewport.X,
                viewport.Y,
                ComputeDiagnosticsCardLeft(viewport.X, diagnosticsLines),
                diagnosticsLines,
                renderDebug?.DrawSkiaUi ?? true,
                diagnostics?.HudEnabled ?? true,
                diagnostics?.TextEnabled ?? true,
                diagnostics?.HotpathBarsEnabled ?? true,
                diagnostics?.HotpathHudTextEnabled ?? true,
                renderDebug?.DrawPrimitives ?? true,
                diagnostics?.HotpathCullCrowdEnabled ?? true);
        }

        private string[] BuildDiagnosticsLines(
            GameEngine engine,
            string mapId,
            CameraAcceptanceDiagnosticsState? diagnostics,
            RenderDebugState? renderDebug)
        {
            if (diagnostics == null || renderDebug == null || !diagnostics.HudEnabled)
            {
                diagnostics?.ObserveHudBuild(0d);
                ResetDiagnosticsCache();
                return Array.Empty<string>();
            }

            PresentationTimingDiagnostics? timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            long currentTick = engine.GameSession?.CurrentTick ?? 0L;
            int visibleEntities = ResolveVisibleEntityCount(engine, timings);
            int projectionSpawnCount = CameraAcceptanceRuntime.ResolveProjectionSpawnCount(engine);
            if (!ShouldRefreshDiagnosticsSnapshot(
                    mapId,
                    diagnostics,
                    renderDebug,
                    currentTick,
                    visibleEntities,
                    projectionSpawnCount))
            {
                return _cachedDiagnosticsLines;
            }

            long start = Stopwatch.GetTimestamp();
            var lines = new List<string>(12)
            {
                $"Camera Acceptance | FPS={diagnostics.SmoothedFps:F1} | Frame={diagnostics.SmoothedFrameMs:F2}ms",
                $"F6 Panel[{OnOff(renderDebug.DrawSkiaUi)}]  F7 HUD[{OnOff(diagnostics.HudEnabled)}]  F8 Select[{OnOff(diagnostics.TextEnabled)}]",
                $"Build panel={diagnostics.PanelSyncMs:F2}ms  hud={diagnostics.HudBuildMs:F2}ms  text={diagnostics.TextBuildMs:F2}ms",
                $"Panel diff={diagnostics.PanelLastApplyMode}  nodes={diagnostics.PanelLastPatchedNodes}  rows={diagnostics.PanelLastSelectionRowsTouched}/{diagnostics.PanelRowPoolSize}  virt={diagnostics.PanelVirtualizedComposedItems}/{diagnostics.PanelVirtualizedTotalItems}  full={diagnostics.PanelFullRecomposeCount}  incr={diagnostics.PanelIncrementalPatchCount}"
            };

            if (string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Hotpath build bars={diagnostics.HotpathBarBuildMs:F2}ms  hudText={diagnostics.HotpathHudTextBuildMs:F2}ms  prims={diagnostics.HotpathPrimitiveBuildMs:F2}ms");
                lines.Add($"F9 Bars[{OnOff(diagnostics.HotpathBarsEnabled)}]  F10 HudText[{OnOff(diagnostics.HotpathHudTextEnabled)}]  F12 Prim[{OnOff(renderDebug.DrawPrimitives)}]  C Crowd[{OnOff(diagnostics.HotpathCullCrowdEnabled)}]");
                lines.Add($"Hotpath crowd={diagnostics.HotpathCrowdCount}  visible={diagnostics.HotpathVisibleCrowdCount}  bars={diagnostics.HotpathBarItemCount}  hudText={diagnostics.HotpathHudTextItemCount}  prims={diagnostics.HotpathPrimitiveItemCount}  select={diagnostics.HotpathSelectionLabelCount}");
                if (engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud &&
                    engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer) is ScreenHudBatchBuffer screenHud)
                {
                    lines.Add($"HUD buffers world={worldHud.Count}/{worldHud.Capacity} drop={worldHud.DroppedSinceClear}  screen={screenHud.Count}/{screenHud.Capacity} drop={screenHud.DroppedSinceClear}");
                }
            }

            if (timings != null)
            {
                lines.Add($"Adapter uiIn={timings.UiInputMs:F2}ms  uiRender={timings.UiRenderMs:F2}ms  uiUpload={timings.UiUploadMs:F2}ms");
                lines.Add($"Adapter overlayBuild={timings.ScreenOverlayBuildMs:F2}ms  draw={timings.ScreenOverlayDrawMs:F2}ms  dirty={timings.ScreenOverlayDirtyLanesLastFrame}  rebuilt={timings.ScreenOverlayRebuiltLanesLastFrame}  cache={timings.ScreenOverlayTextLayoutCacheCount}");
                lines.Add($"Core cull={timings.CameraCullingMs:F2}ms  vis={visibleEntities}  cam={timings.CameraPresenterMs:F2}ms  hudProj={timings.WorldHudProjectionMs:F2}ms");
                lines.Add($"Terrain render={timings.TerrainRenderMs:F2}ms  build={timings.TerrainChunkBuildMs:F2}ms  chunks={timings.TerrainChunksDrawnLastFrame}  built={timings.TerrainChunksBuiltLastFrame}");
                lines.Add($"Primitive draw={timings.PrimitiveRenderMs:F2}ms  instances={timings.PrimitiveInstancesLastFrame}  batches={timings.PrimitiveBatchesLastFrame}");
            }

            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Spawn Batch={CameraAcceptanceRuntime.ResolveProjectionSpawnCount(engine)} | Q/E +/-{CameraAcceptanceIds.ProjectionSpawnCountStep}");
            }

            diagnostics.ObserveHudBuild((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return CacheDiagnosticsSnapshot(
                mapId,
                diagnostics,
                renderDebug,
                currentTick,
                visibleEntities,
                projectionSpawnCount,
                lines);
        }

        private bool ShouldRefreshDiagnosticsSnapshot(
            string mapId,
            CameraAcceptanceDiagnosticsState diagnostics,
            RenderDebugState renderDebug,
            long currentTick,
            int visibleEntities,
            int projectionSpawnCount)
        {
            if (_cachedDiagnosticsLines.Length == 0 ||
                !string.Equals(_cachedDiagnosticsMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_cachedPanelEnabled != renderDebug.DrawSkiaUi ||
                _cachedHudEnabled != diagnostics.HudEnabled ||
                _cachedSelectionTextEnabled != diagnostics.TextEnabled ||
                _cachedHotpathBarsEnabled != diagnostics.HotpathBarsEnabled ||
                _cachedHotpathHudTextEnabled != diagnostics.HotpathHudTextEnabled ||
                _cachedPrimitivesEnabled != renderDebug.DrawPrimitives ||
                _cachedHotpathCullCrowdEnabled != diagnostics.HotpathCullCrowdEnabled)
            {
                return true;
            }

            if (_cachedVisibleEntities != visibleEntities ||
                _cachedProjectionSpawnCount != projectionSpawnCount ||
                _cachedHotpathCrowdCount != diagnostics.HotpathCrowdCount ||
                _cachedHotpathVisibleCrowdCount != diagnostics.HotpathVisibleCrowdCount)
            {
                return true;
            }

            return currentTick - _cachedDiagnosticsTick >= DiagnosticsRefreshIntervalTicks;
        }

        private static int ResolveVisibleEntityCount(GameEngine engine, PresentationTimingDiagnostics? timings)
        {
            if (engine.GetService(CoreServiceKeys.CameraCullingDebugState) is CameraCullingDebugState culling)
            {
                return culling.VisibleEntityCount;
            }

            return timings?.VisibleEntitiesLastFrame ?? int.MinValue;
        }

        private string[] CacheDiagnosticsSnapshot(
            string mapId,
            CameraAcceptanceDiagnosticsState diagnostics,
            RenderDebugState renderDebug,
            long currentTick,
            int visibleEntities,
            int projectionSpawnCount,
            List<string> lines)
        {
            _cachedDiagnosticsMapId = mapId;
            _cachedDiagnosticsTick = currentTick;
            _cachedVisibleEntities = visibleEntities;
            _cachedProjectionSpawnCount = projectionSpawnCount;
            _cachedHotpathCrowdCount = diagnostics.HotpathCrowdCount;
            _cachedHotpathVisibleCrowdCount = diagnostics.HotpathVisibleCrowdCount;
            _cachedPanelEnabled = renderDebug.DrawSkiaUi;
            _cachedHudEnabled = diagnostics.HudEnabled;
            _cachedSelectionTextEnabled = diagnostics.TextEnabled;
            _cachedHotpathBarsEnabled = diagnostics.HotpathBarsEnabled;
            _cachedHotpathHudTextEnabled = diagnostics.HotpathHudTextEnabled;
            _cachedPrimitivesEnabled = renderDebug.DrawPrimitives;
            _cachedHotpathCullCrowdEnabled = diagnostics.HotpathCullCrowdEnabled;
            _cachedDiagnosticsLines = lines.ToArray();
            return _cachedDiagnosticsLines;
        }

        private void ResetDiagnosticsCache()
        {
            _cachedDiagnosticsLines = Array.Empty<string>();
            _cachedDiagnosticsMapId = string.Empty;
            _cachedDiagnosticsTick = -1;
            _cachedVisibleEntities = int.MinValue;
            _cachedProjectionSpawnCount = int.MinValue;
            _cachedHotpathCrowdCount = int.MinValue;
            _cachedHotpathVisibleCrowdCount = int.MinValue;
            _cachedPanelEnabled = true;
            _cachedHudEnabled = true;
            _cachedSelectionTextEnabled = true;
            _cachedHotpathBarsEnabled = true;
            _cachedHotpathHudTextEnabled = true;
            _cachedPrimitivesEnabled = true;
            _cachedHotpathCullCrowdEnabled = true;
        }

        private void LoadAcceptanceMap(string mapId)
        {
            GameEngine engine = RequireEngine();
            string? currentMapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (CameraAcceptanceIds.IsAcceptanceMap(currentMapId))
            {
                engine.UnloadMap(currentMapId!);
            }

            engine.LoadMap(mapId);
            SyncMountedRoot();
        }

        private void SwitchViewMode(string modeId)
        {
            GameEngine engine = RequireEngine();
            if (ResolveViewModeManager(engine) is ViewModeManager viewModeManager)
            {
                viewModeManager.SwitchTo(modeId);
                SyncMountedRoot();
            }
        }

        private void SetBlendCamera(string cameraId)
        {
            GameEngine engine = RequireEngine();
            engine.GlobalContext[CameraAcceptanceIds.ActiveBlendCameraIdKey] = cameraId;
            SyncMountedRoot();
        }

        private void RequestVirtualCamera(string? id, bool clear)
        {
            GameEngine engine = RequireEngine();
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = id ?? string.Empty,
                Clear = clear
            });
        }

        private void TogglePanel()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                return;
            }

            renderDebug.DrawSkiaUi = !renderDebug.DrawSkiaUi;
            if (!renderDebug.DrawSkiaUi && engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                ClearIfOwned(root);
                return;
            }

            SyncMountedRoot();
        }

        private void ToggleDiagnosticsHud()
        {
            if (TryGetDiagnosticsState(RequireEngine(), out var diagnostics))
            {
                diagnostics.HudEnabled = !diagnostics.HudEnabled;
                SyncMountedRoot();
            }
        }

        private void ToggleSelectionText()
        {
            if (TryGetDiagnosticsState(RequireEngine(), out var diagnostics))
            {
                diagnostics.TextEnabled = !diagnostics.TextEnabled;
                SyncMountedRoot();
            }
        }

        private void ToggleHotpathBars()
        {
            if (TryGetDiagnosticsState(RequireEngine(), out var diagnostics))
            {
                diagnostics.HotpathBarsEnabled = !diagnostics.HotpathBarsEnabled;
                SyncMountedRoot();
            }
        }

        private void ToggleHotpathHudText()
        {
            if (TryGetDiagnosticsState(RequireEngine(), out var diagnostics))
            {
                diagnostics.HotpathHudTextEnabled = !diagnostics.HotpathHudTextEnabled;
                SyncMountedRoot();
            }
        }

        private void TogglePrimitives()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
            {
                renderDebug.DrawPrimitives = !renderDebug.DrawPrimitives;
                SyncMountedRoot();
            }
        }

        private void ToggleHotpathCullCrowd()
        {
            if (TryGetDiagnosticsState(RequireEngine(), out var diagnostics))
            {
                diagnostics.HotpathCullCrowdEnabled = !diagnostics.HotpathCullCrowdEnabled;
                SyncMountedRoot();
            }
        }

        private void MoveHotpathCameraCrowd()
        {
            ApplyHotpathCameraTarget(new Vector2(CameraAcceptanceIds.HotpathSweepLeftX, CameraAcceptanceIds.HotpathSweepCenterY));
        }

        private void MoveHotpathCameraCenter()
        {
            float centerX = (CameraAcceptanceIds.HotpathSweepLeftX + CameraAcceptanceIds.HotpathSweepRightX) * 0.5f;
            ApplyHotpathCameraTarget(new Vector2(centerX, CameraAcceptanceIds.HotpathSweepCenterY));
        }

        private void MoveHotpathCameraEmpty()
        {
            ApplyHotpathCameraTarget(new Vector2(CameraAcceptanceIds.HotpathSweepRightX, CameraAcceptanceIds.HotpathSweepCenterY));
        }

        private void ApplyHotpathCameraTarget(Vector2 targetCm)
        {
            GameEngine engine = RequireEngine();
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                TargetCm = targetCm
            });
            SyncMountedRoot();
        }

        private void RespawnHotpathCrowd()
        {
            GameEngine engine = RequireEngine();
            if (TryGetDiagnosticsState(engine, out var diagnostics))
            {
                diagnostics.HotpathCullCrowdEnabled = true;
            }

            var crowdQuery = new QueryDescription().WithAll<CameraAcceptanceHotpathCrowdTag>();
            engine.World.Destroy(in crowdQuery);
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is RuntimeEntitySpawnQueue spawnQueue)
            {
                spawnQueue.Clear();
            }

            CameraAcceptanceHotpathLaneSystem.ResetCrowdRequested(engine);
            SyncMountedRoot();
        }

        private void ClearHotpathCrowd()
        {
            GameEngine engine = RequireEngine();
            if (TryGetDiagnosticsState(engine, out var diagnostics))
            {
                diagnostics.HotpathCullCrowdEnabled = false;
            }

            var crowdQuery = new QueryDescription().WithAll<CameraAcceptanceHotpathCrowdTag>();
            engine.World.Destroy(in crowdQuery);
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is RuntimeEntitySpawnQueue spawnQueue)
            {
                spawnQueue.Clear();
            }

            CameraAcceptanceHotpathLaneSystem.ResetCrowdRequested(engine);
            SyncMountedRoot();
        }

        private void ToggleCaptainPosition()
        {
            GameEngine engine = RequireEngine();
            Entity entity = FindEntityByName(engine.World, CameraAcceptanceIds.CaptainName);
            if (entity == Entity.Null || !engine.World.Has<WorldPositionCm>(entity))
            {
                return;
            }

            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            Vector2 current = ToVector2(position);
            Vector2 next = Vector2.Distance(current, CaptainOriginCm) < 1f ? CaptainMovedCm : CaptainOriginCm;
            position = WorldPositionCm.FromCm((int)next.X, (int)next.Y);
            SyncMountedRoot();
        }

        private void SyncMountedRoot()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                return;
            }

            if (ApplyStateSnapshot(engine))
            {
                root.IsDirty = true;
            }
        }

        private GameEngine RequireEngine()
        {
            return _engine ?? throw new InvalidOperationException("CameraAcceptancePanelController is not bound to an engine.");
        }

        private static bool StateEquals(CameraAcceptancePanelState left, CameraAcceptancePanelState right)
        {
            if (!string.Equals(left.MapId, right.MapId, StringComparison.Ordinal) ||
                !string.Equals(left.MapDescription, right.MapDescription, StringComparison.Ordinal) ||
                !string.Equals(left.ControlsDescription, right.ControlsDescription, StringComparison.Ordinal) ||
                !string.Equals(left.ActiveCameraId, right.ActiveCameraId, StringComparison.Ordinal) ||
                !string.Equals(left.ActiveModeId, right.ActiveModeId, StringComparison.Ordinal) ||
                !string.Equals(left.SelectedName, right.SelectedName, StringComparison.Ordinal) ||
                !string.Equals(left.SelectedIdsSummary, right.SelectedIdsSummary, StringComparison.Ordinal) ||
                !string.Equals(left.FollowTarget, right.FollowTarget, StringComparison.Ordinal) ||
                !string.Equals(left.ActiveBlendCameraId, right.ActiveBlendCameraId, StringComparison.Ordinal) ||
                left.ProjectionSpawnCount != right.ProjectionSpawnCount ||
                !string.Equals(left.VisibleEntitySummary, right.VisibleEntitySummary, StringComparison.Ordinal) ||
                left.ViewportWidth != right.ViewportWidth ||
                left.ViewportHeight != right.ViewportHeight ||
                left.DiagnosticsCardLeft != right.DiagnosticsCardLeft ||
                left.PanelEnabled != right.PanelEnabled ||
                left.DiagnosticsHudEnabled != right.DiagnosticsHudEnabled ||
                left.SelectionTextEnabled != right.SelectionTextEnabled ||
                left.HotpathBarsEnabled != right.HotpathBarsEnabled ||
                left.HotpathHudTextEnabled != right.HotpathHudTextEnabled ||
                left.PrimitivesEnabled != right.PrimitivesEnabled ||
                left.DiagnosticsLines.Length != right.DiagnosticsLines.Length ||
                left.HotpathCullCrowdEnabled != right.HotpathCullCrowdEnabled)
            {
                return false;
            }

            if (left.SelectedIds.Length != right.SelectedIds.Length)
            {
                return false;
            }

            for (int i = 0; i < left.SelectedIds.Length; i++)
            {
                if (!string.Equals(left.SelectedIds[i], right.SelectedIds[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (left.VisibleEntityRows.Length != right.VisibleEntityRows.Length)
            {
                return false;
            }

            for (int i = 0; i < left.VisibleEntityRows.Length; i++)
            {
                if (!string.Equals(left.VisibleEntityRows[i], right.VisibleEntityRows[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            for (int i = 0; i < left.DiagnosticsLines.Length; i++)
            {
                if (!string.Equals(left.DiagnosticsLines[i], right.DiagnosticsLines[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj is ViewModeManager manager)
            {
                return manager;
            }

            return null;
        }

        private static string ResolveActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeObj) && modeObj is string modeId
                ? modeId
                : "map-default";
        }

        private static string ResolveActiveBlendCameraId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }

        private static string? ResolveSelectedEntityName(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var value) ||
                value is not Entity entity ||
                entity == Entity.Null ||
                !engine.World.IsAlive(entity) ||
                !engine.World.Has<Name>(entity))
            {
                return null;
            }

            return engine.World.Get<Name>(entity).Value;
        }

        private static string[] ResolveSelectedEntityIds(GameEngine engine)
        {
            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = CameraAcceptanceSelectionView.CopySelectedEntities(engine.World, engine.GlobalContext, selected);
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            string[] lines = new string[count];
            for (int i = 0; i < count; i++)
            {
                lines[i] = CameraAcceptanceSelectionView.FormatEntityId(selected[i]);
            }

            return lines;
        }

        private static string[] ResolveVisibleEntityRows(GameEngine engine, string mapId)
        {
            if (engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
            {
                throw new InvalidOperationException("Camera acceptance hotpath panel requires IViewController.");
            }

            var camera = CameraViewportUtil.StateToRenderState(engine.GameSession.Camera.State);
            if (camera.FovYDeg <= 0f || view.Fov <= 0f || view.Resolution.X <= 0f || view.Resolution.Y <= 0f)
            {
                return Array.Empty<string>();
            }

            var visibleRows = new List<VisibleEntityRow>();
            engine.World.Query(in VisibleEntityQuery, (Entity entity, ref Name name, ref MapEntity mapEntity, ref CullState cull, ref VisualTransform transform) =>
            {
                if (!cull.IsVisible ||
                    !string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Vector2 screen = CameraViewportUtil.WorldToScreen(transform.Position, camera, view.Resolution, view.AspectRatio);
                if (!IsInsideViewport(screen, view.Resolution))
                {
                    return;
                }

                visibleRows.Add(new VisibleEntityRow(entity.Id, $"{name.Value} {CameraAcceptanceSelectionView.FormatEntityId(entity)}"));
            });

            visibleRows.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));

            string[] rows = new string[visibleRows.Count];
            for (int i = 0; i < visibleRows.Count; i++)
            {
                rows[i] = visibleRows[i].Text;
            }

            return rows;
        }

        private static int CountSelectionRowChanges(IReadOnlyList<string> previous, IReadOnlyList<string> next)
        {
            int count = 0;
            int length = Math.Max(previous.Count, next.Count);
            for (int i = 0; i < length; i++)
            {
                string? left = i < previous.Count ? previous[i] : null;
                string? right = i < next.Count ? next[i] : null;
                if (!string.Equals(left, right, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static IReadOnlyList<string> ResolveTrackedRows(CameraAcceptancePanelState state)
        {
            return string.Equals(state.MapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase)
                ? state.VisibleEntityRows
                : state.SelectedIds;
        }

        private static int ResolveTrackedRowPoolSize(CameraAcceptancePanelState state)
        {
            return string.Equals(state.MapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase)
                ? state.VisibleEntityRows.Length
                : SelectionRowPoolSize;
        }

        private static string BuildVisibleEntitySummary(IReadOnlyList<string> visibleEntityRows)
        {
            return visibleEntityRows.Count <= 0
                ? "Visible on screen: 0"
                : $"Visible on screen: {visibleEntityRows.Count}";
        }

        private static Vector2 ResolveViewportSize(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root &&
                root.Width > 0f &&
                root.Height > 0f)
            {
                return new Vector2(root.Width, root.Height);
            }

            if (engine.GetService(CoreServiceKeys.ViewController) is IViewController view &&
                view.Resolution.X > 0f &&
                view.Resolution.Y > 0f)
            {
                return view.Resolution;
            }

            return new Vector2(1920f, 1080f);
        }

        private static float ComputeDiagnosticsCardLeft(float viewportWidth, IReadOnlyList<string> diagnosticsLines)
        {
            if (viewportWidth <= 0f)
            {
                return PanelWidth + DiagnosticsCardMargin;
            }

            int maxLength = 0;
            for (int i = 0; i < diagnosticsLines.Count; i++)
            {
                if (diagnosticsLines[i].Length > maxLength)
                {
                    maxLength = diagnosticsLines[i].Length;
                }
            }

            float estimatedWidth = diagnosticsLines.Count == 0
                ? 280f
                : MathF.Ceiling((maxLength * DiagnosticsAverageGlyphWidthPx) + 28f);
            return Math.Max(DiagnosticsCardMargin, viewportWidth - estimatedWidth - DiagnosticsCardMargin);
        }

        private static string SummarizeSelectedIds(IReadOnlyList<string> selectedIds)
        {
            if (selectedIds.Count == 0)
            {
                return "none";
            }

            int previewCount = Math.Min(4, selectedIds.Count);
            string[] previewItems = new string[previewCount];
            for (int i = 0; i < previewCount; i++)
            {
                previewItems[i] = selectedIds[i];
            }

            string preview = string.Join(", ", previewItems);
            return selectedIds.Count > previewCount
                ? $"{preview}, +{selectedIds.Count - previewCount} more"
                : preview;
        }

        private static bool TryGetDiagnosticsState(GameEngine engine, out CameraAcceptanceDiagnosticsState diagnostics)
        {
            if (engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState state)
            {
                diagnostics = state;
                return true;
            }

            diagnostics = null!;
            return false;
        }

        private static string FormatVector(Vector2? value)
        {
            if (!value.HasValue)
            {
                return "none";
            }

            return $"{value.Value.X:0},{value.Value.Y:0}";
        }

        private static Vector2 ToVector2(WorldPositionCm position)
        {
            var value = position.ToWorldCmInt2();
            return new Vector2(value.X, value.Y);
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

        private static string OnOff(bool value) => value ? "ON" : "OFF";

        private static bool IsInsideViewport(Vector2 screen, Vector2 resolution)
        {
            return !float.IsNaN(screen.X) &&
                   !float.IsNaN(screen.Y) &&
                   !float.IsInfinity(screen.X) &&
                   !float.IsInfinity(screen.Y) &&
                   screen.X >= 0f &&
                   screen.Y >= 0f &&
                   screen.X <= resolution.X &&
                   screen.Y <= resolution.Y;
        }

        private static string GetVisibleEntityRowId(int index)
        {
            return $"camera-visible-row-{index:0000}";
        }

        private sealed record CameraAcceptancePanelState(
            string MapId,
            string MapDescription,
            string ControlsDescription,
            string ActiveCameraId,
            string ActiveModeId,
            string SelectedName,
            string SelectedIdsSummary,
            string[] SelectedIds,
            string FollowTarget,
            string ActiveBlendCameraId,
            int ProjectionSpawnCount,
            string VisibleEntitySummary,
            string[] VisibleEntityRows,
            float ViewportWidth,
            float ViewportHeight,
            float DiagnosticsCardLeft,
            string[] DiagnosticsLines,
            bool PanelEnabled,
            bool DiagnosticsHudEnabled,
            bool SelectionTextEnabled,
            bool HotpathBarsEnabled,
            bool HotpathHudTextEnabled,
            bool PrimitivesEnabled,
            bool HotpathCullCrowdEnabled)
        {
            public static CameraAcceptancePanelState Empty { get; } = new(
                string.Empty,
                string.Empty,
                string.Empty,
                "none",
                "map-default",
                "none",
                "none",
                Array.Empty<string>(),
                "none",
                CameraAcceptanceIds.BlendSmoothCameraId,
                CameraAcceptanceIds.ProjectionSpawnCountDefault,
                "Visible on screen: 0",
                Array.Empty<string>(),
                1920f,
                1080f,
                16f,
                Array.Empty<string>(),
                true,
                true,
                true,
                true,
                true,
                true,
                true);
        }

        private readonly record struct VisibleEntityRow(int EntityId, string Text);
    }
}
