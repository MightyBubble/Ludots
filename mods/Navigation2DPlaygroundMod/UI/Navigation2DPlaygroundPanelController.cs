using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Navigation2DPlaygroundMod.Runtime;
using Navigation2DPlaygroundMod.Systems;

namespace Navigation2DPlaygroundMod.UI
{
    internal sealed class Navigation2DPlaygroundPanelController
    {
        private const float PanelWidth = 470f;

        private readonly ReactivePage<Navigation2DPlaygroundPanelState> _page;
        private Navigation2DPlaygroundPanelState _lastState = Navigation2DPlaygroundPanelState.Empty;
        private GameEngine? _engine;

        public Navigation2DPlaygroundPanelController()
        {
            _page = new ReactivePage<Navigation2DPlaygroundPanelState>(Navigation2DPlaygroundPanelState.Empty, BuildRoot);
        }

        public UiScene Scene => _page.Scene;

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

            _engine = null;
            _lastState = Navigation2DPlaygroundPanelState.Empty;
            _page.SetState(_ => Navigation2DPlaygroundPanelState.Empty);
        }

        private UiElementBuilder BuildRoot(ReactiveContext<Navigation2DPlaygroundPanelState> context)
        {
            var state = context.State;
            if (string.IsNullOrWhiteSpace(state.MapId))
            {
                return Ui.Card(
                        Ui.Text("Navigation2D Playground").FontSize(22f).Bold().Color("#F7FAFF"),
                        Ui.Text("No active playground map.").FontSize(13f).Color("#90A1B7"))
                    .Width(PanelWidth)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(18f)
                    .Background("#0F1826")
                    .Absolute(16f, 16f)
                    .ZIndex(20);
            }

            return Ui.Card(
                    Ui.Text("Navigation2D Playground").FontSize(22f).Bold().Color("#F7FAFF"),
                    Ui.Text("Latest runtime path: reactive UI, CoreInput selection, virtual camera view modes, real Navigation2D sim.").FontSize(12f).Color("#D0D8E6").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Scenario: {state.ScenarioIndexLabel}  {state.ScenarioName}").FontSize(13f).Color("#D0D8E6"),
                    Ui.Text($"Mode: {state.ActiveModeId}  Tool: {state.ToolModeLabel}").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Selected: {state.SelectedCount}  Spawn Batch: {state.SpawnBatch}").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Agents/team: {state.AgentsPerTeam}  Live: {state.LiveAgents}  Blockers: {state.Blockers}").FontSize(13f).Color("#8EA2BD"),
                    Ui.Text($"Steering: {state.SteeringMode}  Cache: {state.CacheState} ({state.CacheHitRate})").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Flow: enabled={state.FlowEnabled} debug={state.FlowDebugEnabled} mode={state.FlowDebugMode} iter={state.FlowIterations} activeTiles={state.FlowActiveTiles}").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Spatial: {state.SpatialMode}  CellMigrations: {state.SpatialCellMigrations}").FontSize(12f).Color("#8EA2BD"),
                    Ui.Text("Scenario").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                        BuildActionButton("Prev", false, PreviousScenario),
                        BuildActionButton("Next", false, NextScenario),
                        BuildActionButton("Reset", false, ResetScenario))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Scale").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                        BuildActionButton("- Team", false, () => AdjustAgentsPerTeam(-Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(RequireEngine()).AgentsPerTeamStep)),
                        BuildActionButton("+ Team", false, () => AdjustAgentsPerTeam(Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(RequireEngine()).AgentsPerTeamStep)),
                        BuildActionButton("- Spawn", false, () => AdjustSpawnBatch(-Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(RequireEngine()).SpawnBatchStep)),
                        BuildActionButton("+ Spawn", false, () => AdjustSpawnBatch(Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(RequireEngine()).SpawnBatchStep)))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Tools").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                        BuildToolButton("Move", Navigation2DPlaygroundToolMode.Move, state.ToolMode == Navigation2DPlaygroundToolMode.Move),
                        BuildToolButton("Spawn T0", Navigation2DPlaygroundToolMode.SpawnTeam0, state.ToolMode == Navigation2DPlaygroundToolMode.SpawnTeam0),
                        BuildToolButton("Spawn T1", Navigation2DPlaygroundToolMode.SpawnTeam1, state.ToolMode == Navigation2DPlaygroundToolMode.SpawnTeam1),
                        BuildToolButton("Blocker", Navigation2DPlaygroundToolMode.SpawnBlocker, state.ToolMode == Navigation2DPlaygroundToolMode.SpawnBlocker))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Camera").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                        BuildModeButton("RTS", state.ActiveModeId == Navigation2DPlaygroundIds.CommandModeId, Navigation2DPlaygroundIds.CommandModeId),
                        BuildModeButton("Follow", state.ActiveModeId == Navigation2DPlaygroundIds.FollowModeId, Navigation2DPlaygroundIds.FollowModeId))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Flow").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Row(
                        BuildActionButton(state.FlowEnabled ? "Flow On" : "Flow Off", state.FlowEnabled, ToggleFlowEnabled),
                        BuildActionButton(state.FlowDebugEnabled ? "Debug On" : "Debug Off", state.FlowDebugEnabled, ToggleFlowDebug),
                        BuildActionButton($"Mode {state.FlowDebugMode}", false, CycleFlowDebugMode))
                        .Wrap()
                        .Gap(8f),
                    BuildSelectedIdsSection(state.SelectedIds),
                    Ui.Text("Play").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text("Left click to select. Drag a box to multi-select. Right click issues the active tool. F1/F2 swap camera modes. G/H/J/U/Y/K/L/N/M/R keep the keyboard debug path alive.").FontSize(12f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal))
                .Width(PanelWidth)
                .Padding(16f)
                .Gap(10f)
                .Radius(18f)
                .Background("#0F1826")
                .Absolute(16f, 16f)
                .ZIndex(20);
        }

        private static UiElementBuilder BuildSelectedIdsSection(IReadOnlyList<string> selectedIds)
        {
            var children = new List<UiElementBuilder>
            {
                Ui.Text("Selection Buffer").FontSize(12f).Bold().Color("#F4C77D")
            };

            if (selectedIds.Count == 0)
            {
                children.Add(Ui.Text("none").FontSize(12f).Color("#8EA2BD"));
            }
            else
            {
                for (int i = 0; i < selectedIds.Count; i++)
                {
                    children.Add(Ui.Text(selectedIds[i]).FontSize(12f).Color("#D0D8E6"));
                }
            }

            return Ui.Column(children.ToArray()).Gap(4f);
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, Action onClick)
        {
            return Ui.Button(label, _ => onClick())
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#31576A" : "#121B29")
                .Color("#F7FAFF");
        }

        private UiElementBuilder BuildToolButton(string label, Navigation2DPlaygroundToolMode toolMode, bool active)
        {
            return Ui.Button(label, _ => SetToolMode(toolMode))
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#5A4219" : "#182436")
                .Color(active ? "#F7FAFF" : "#C7D3E1");
        }

        private UiElementBuilder BuildModeButton(string label, bool active, string modeId)
        {
            return Ui.Button(label, _ => SwitchViewMode(modeId))
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#244E66" : "#182436")
                .Color(active ? "#F7FAFF" : "#C7D3E1");
        }

        private bool ApplyStateSnapshot(GameEngine engine)
        {
            var next = CaptureState(engine);
            if (StateEquals(_lastState, next))
            {
                return false;
            }

            _lastState = next;
            _page.SetState(_ => next);
            return true;
        }

        private Navigation2DPlaygroundPanelState CaptureState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(mapId))
            {
                return Navigation2DPlaygroundPanelState.Empty;
            }

            string[] selectedIds = ResolveSelectedIds(engine);
            var navRuntime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
            float cacheHitRate = navRuntime?.AgentSoA.SteeringCacheLookupsFrame > 0
                ? (float)navRuntime.AgentSoA.SteeringCacheHitsFrame / navRuntime.AgentSoA.SteeringCacheLookupsFrame
                : 0f;

            return new Navigation2DPlaygroundPanelState(
                MapId: mapId,
                ScenarioIndexLabel: $"{engine.GetService(Navigation2DPlaygroundKeys.ScenarioIndex) + 1}/{engine.GetService(Navigation2DPlaygroundKeys.ScenarioCount)}",
                ScenarioName: engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown",
                ActiveModeId: ResolveActiveModeId(engine),
                AgentsPerTeam: engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam),
                LiveAgents: engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal),
                Blockers: engine.GetService(Navigation2DPlaygroundKeys.BlockerCount),
                SpawnBatch: engine.GetService(Navigation2DPlaygroundKeys.SpawnBatch),
                SelectedCount: selectedIds.Length,
                SelectedIds: selectedIds,
                ToolMode: Navigation2DPlaygroundState.ToolMode,
                ToolModeLabel: Navigation2DPlaygroundState.ToolMode.ToString(),
                FlowEnabled: navRuntime?.FlowEnabled ?? false,
                FlowDebugEnabled: navRuntime?.FlowDebugEnabled ?? false,
                FlowDebugMode: navRuntime?.FlowDebugMode ?? 0,
                FlowIterations: navRuntime?.FlowIterationsPerTick ?? 0,
                FlowActiveTiles: navRuntime == null ? 0 : CountActiveFlowTiles(navRuntime),
                SteeringMode: navRuntime?.Config.Steering.Mode.ToString() ?? "Unavailable",
                CacheState: ResolveCacheState(navRuntime),
                CacheHitRate: $"{cacheHitRate:P1}",
                SpatialMode: navRuntime?.Config.Spatial.UpdateMode.ToString() ?? "Unavailable",
                SpatialCellMigrations: navRuntime?.CellMap.InstrumentedCellMigrations ?? 0L);
        }

        private void PreviousScenario()
        {
            Navigation2DPlaygroundControlSystem.PreviousScenario(RequireEngine());
            SyncMountedRoot();
        }

        private void NextScenario()
        {
            Navigation2DPlaygroundControlSystem.NextScenario(RequireEngine());
            SyncMountedRoot();
        }

        private void ResetScenario()
        {
            Navigation2DPlaygroundControlSystem.RespawnScenario(RequireEngine());
            SyncMountedRoot();
        }

        private void AdjustAgentsPerTeam(int delta)
        {
            Navigation2DPlaygroundControlSystem.AdjustAgentsPerTeam(RequireEngine(), delta);
            SyncMountedRoot();
        }

        private void AdjustSpawnBatch(int delta)
        {
            Navigation2DPlaygroundControlSystem.AdjustSpawnBatch(RequireEngine(), delta);
            SyncMountedRoot();
        }

        private void SetToolMode(Navigation2DPlaygroundToolMode toolMode)
        {
            Navigation2DPlaygroundState.ToolMode = toolMode;
            SyncMountedRoot();
        }

        private void SwitchViewMode(string modeId)
        {
            GameEngine engine = RequireEngine();
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj is ViewModeManager manager)
            {
                manager.SwitchTo(modeId);
                SyncMountedRoot();
            }
        }

        private void ToggleFlowEnabled()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime)
            {
                navRuntime.FlowEnabled = !navRuntime.FlowEnabled;
                SyncMountedRoot();
            }
        }

        private void ToggleFlowDebug()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime)
            {
                navRuntime.FlowDebugEnabled = !navRuntime.FlowDebugEnabled;
                SyncMountedRoot();
            }
        }

        private void CycleFlowDebugMode()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime)
            {
                navRuntime.FlowDebugMode = (navRuntime.FlowDebugMode + 1) % 3;
                SyncMountedRoot();
            }
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
            return _engine ?? throw new InvalidOperationException("Navigation2DPlaygroundPanelController is not bound to an engine.");
        }

        private static bool StateEquals(Navigation2DPlaygroundPanelState left, Navigation2DPlaygroundPanelState right)
        {
            if (!string.Equals(left.MapId, right.MapId, StringComparison.Ordinal) ||
                !string.Equals(left.ScenarioIndexLabel, right.ScenarioIndexLabel, StringComparison.Ordinal) ||
                !string.Equals(left.ScenarioName, right.ScenarioName, StringComparison.Ordinal) ||
                !string.Equals(left.ActiveModeId, right.ActiveModeId, StringComparison.Ordinal) ||
                left.AgentsPerTeam != right.AgentsPerTeam ||
                left.LiveAgents != right.LiveAgents ||
                left.Blockers != right.Blockers ||
                left.SpawnBatch != right.SpawnBatch ||
                left.SelectedCount != right.SelectedCount ||
                left.ToolMode != right.ToolMode ||
                !string.Equals(left.ToolModeLabel, right.ToolModeLabel, StringComparison.Ordinal) ||
                left.FlowEnabled != right.FlowEnabled ||
                left.FlowDebugEnabled != right.FlowDebugEnabled ||
                left.FlowDebugMode != right.FlowDebugMode ||
                left.FlowIterations != right.FlowIterations ||
                left.FlowActiveTiles != right.FlowActiveTiles ||
                !string.Equals(left.SteeringMode, right.SteeringMode, StringComparison.Ordinal) ||
                !string.Equals(left.CacheState, right.CacheState, StringComparison.Ordinal) ||
                !string.Equals(left.CacheHitRate, right.CacheHitRate, StringComparison.Ordinal) ||
                !string.Equals(left.SpatialMode, right.SpatialMode, StringComparison.Ordinal) ||
                left.SpatialCellMigrations != right.SpatialCellMigrations)
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

            return true;
        }

        private static string ResolveActiveModeId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(ViewModeManager.ActiveModeIdKey, out var modeObj) && modeObj is string modeId
                ? modeId
                : "map-default";
        }

        private static string[] ResolveSelectedIds(GameEngine engine)
        {
            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = Navigation2DPlaygroundSelectionView.CopySelectedEntities(engine.World, engine.GlobalContext, selected);
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            string[] lines = new string[count];
            for (int i = 0; i < count; i++)
            {
                lines[i] = Navigation2DPlaygroundSelectionView.FormatEntityId(selected[i]);
            }

            return lines;
        }

        private static int CountActiveFlowTiles(Navigation2DRuntime runtime)
        {
            int activeTiles = 0;
            for (int i = 0; i < runtime.FlowCount; i++)
            {
                activeTiles += runtime.Flows[i].ActiveTileCount;
            }

            return activeTiles;
        }

        private static string ResolveCacheState(Navigation2DRuntime? runtime)
        {
            if (runtime == null)
            {
                return "Unavailable";
            }

            if (!runtime.Config.Steering.TemporalCoherence.Enabled)
            {
                return "ConfigOff";
            }

            return runtime.AgentSoA.SteeringCacheFrameEnabled
                ? "Active"
                : (runtime.Config.Steering.TemporalCoherence.RequireSteadyStateWorld ? "WaitingSteadyState" : "Ready");
        }

        private sealed record Navigation2DPlaygroundPanelState(
            string MapId,
            string ScenarioIndexLabel,
            string ScenarioName,
            string ActiveModeId,
            int AgentsPerTeam,
            int LiveAgents,
            int Blockers,
            int SpawnBatch,
            int SelectedCount,
            string[] SelectedIds,
            Navigation2DPlaygroundToolMode ToolMode,
            string ToolModeLabel,
            bool FlowEnabled,
            bool FlowDebugEnabled,
            int FlowDebugMode,
            int FlowIterations,
            int FlowActiveTiles,
            string SteeringMode,
            string CacheState,
            string CacheHitRate,
            string SpatialMode,
            long SpatialCellMigrations)
        {
            public static Navigation2DPlaygroundPanelState Empty { get; } = new(
                MapId: string.Empty,
                ScenarioIndexLabel: "0/0",
                ScenarioName: string.Empty,
                ActiveModeId: "map-default",
                AgentsPerTeam: 0,
                LiveAgents: 0,
                Blockers: 0,
                SpawnBatch: 0,
                SelectedCount: 0,
                SelectedIds: Array.Empty<string>(),
                ToolMode: Navigation2DPlaygroundToolMode.Move,
                ToolModeLabel: Navigation2DPlaygroundToolMode.Move.ToString(),
                FlowEnabled: false,
                FlowDebugEnabled: false,
                FlowDebugMode: 0,
                FlowIterations: 0,
                FlowActiveTiles: 0,
                SteeringMode: "Unavailable",
                CacheState: "Unavailable",
                CacheHitRate: "0.0 %",
                SpatialMode: "Unavailable",
                SpatialCellMigrations: 0L);
        }
    }
}
