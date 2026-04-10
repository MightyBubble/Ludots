using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.UI
{
    internal sealed class MassFlowNavPlaygroundPanelController
    {
        private readonly MassFlowNavPlaygroundRuntime _runtime;
        private ReactivePage<MassFlowNavPlaygroundPanelState>? _page;
        private GameEngine? _engine;
        private float _refreshCooldownSeconds;
        private int _lastDirtyVersion = -1;

        public MassFlowNavPlaygroundPanelController(MassFlowNavPlaygroundRuntime runtime)
        {
            _runtime = runtime;
        }

        public void MountOrRefresh(UIRoot root, GameEngine engine, MassFlowNavPlaygroundState runtimeState, float dt)
        {
            _engine = engine;
            _refreshCooldownSeconds = MathF.Max(0f, _refreshCooldownSeconds - dt);
            bool forceRefresh = _page == null || _lastDirtyVersion != runtimeState.PanelDirtyVersion || _refreshCooldownSeconds <= 0f;
            if (!forceRefresh && ReferenceEquals(root.Scene, _page?.Scene))
            {
                return;
            }

            MassFlowNavPlaygroundPanelState nextState = BuildState(engine, runtimeState);
            if (_page == null)
            {
                var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
                var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
                _page = new ReactivePage<MassFlowNavPlaygroundPanelState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
            }
            else
            {
                _page.SetState(_ => nextState);
            }

            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                root.MountScene(_page.Scene);
            }

            root.IsDirty = true;
            _refreshCooldownSeconds = 0.25f;
            _lastDirtyVersion = runtimeState.PanelDirtyVersion;
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
            }

            _refreshCooldownSeconds = 0f;
            _lastDirtyVersion = -1;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<MassFlowNavPlaygroundPanelState> context)
        {
            return Ui.Column(BuildPanel(context.State))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(40);
        }

        private UiElementBuilder BuildPanel(MassFlowNavPlaygroundPanelState state)
        {
            return Ui.Column(
                    BuildHeaderCard(state),
                    BuildPopulationCard(state),
                    BuildFlowCard(state),
                    BuildFormationCard(state),
                    BuildGapCard(state))
                .Width(460f)
                .Padding(16f)
                .Gap(10f)
                .Radius(24f)
                .Background("#081219")
                .Absolute(16f, 16f);
        }

        private UiElementBuilder BuildHeaderCard(MassFlowNavPlaygroundPanelState state)
        {
            return Ui.Card(
                    Ui.Text("Mass Flow Nav Playground")
                        .FontSize(24f)
                        .Bold()
                        .Color("#F5F7FA"),
                    Ui.Text("External-reference sandbox with shared team flow, persistent manual overrides, formal box selection, RMB commands, and Q/E formation rotation.")
                        .FontSize(12f)
                        .Color("#B7C4D4")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Selection {state.SelectedCount} | Manual {state.ManualCount} | Groups {state.GroupCount} | FlowEnabled {(state.FlowEnabled ? "On" : "Off")}")
                        .FontSize(12f)
                        .Color("#F0C36B"),
                    Ui.Text("Drag-select friendlies with CoreInput selection. RMB with selection issues manual orders. RMB with empty selection retargets the chosen shared flow. Hold Q/E to rotate the currently selected formation.")
                        .FontSize(12f)
                        .Color("#91A2B6")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0E1822");
        }

        private UiElementBuilder BuildPopulationCard(MassFlowNavPlaygroundPanelState state)
        {
            return Ui.Card(
                    Ui.Text("Population").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text($"Current: team A {state.FriendlyCount} | team B {state.EnemyCount}")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Row(
                            BuildActionButton("Respawn 5K", state.DesiredUnitCount == 5000, _ => SetUnits(5000)),
                            BuildActionButton("Respawn 10K", state.DesiredUnitCount == 10000, _ => SetUnits(10000)),
                            BuildActionButton("Respawn 20K", state.DesiredUnitCount == 20000, _ => SetUnits(20000)),
                            BuildActionButton("Respawn Now", false, _ => _runtime.Respawn(_engine!)))
                        .Wrap()
                        .Gap(8f))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101E2B");
        }

        private UiElementBuilder BuildFlowCard(MassFlowNavPlaygroundPanelState state)
        {
            return Ui.Card(
                    Ui.Text("Shared Flow").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Row(
                            BuildActionButton("Team A Flow", state.SelectedTeamFlowId == 0, _ => SetSelectedFlow(0)),
                            BuildActionButton("Team B Flow", state.SelectedTeamFlowId == 1, _ => SetSelectedFlow(1)))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text($"Active retarget: {state.SelectedTeamFlowLabel}")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Text($"Flow A target: {state.Flow0Target}")
                        .FontSize(12f)
                        .Color("#B7C4D4"),
                    Ui.Text($"Flow B target: {state.Flow1Target}")
                        .FontSize(12f)
                        .Color("#B7C4D4"),
                    Ui.Text("Uncommanded units follow their team flow. Manual overrides stay detached until you explicitly regroup or dissolve them.")
                        .FontSize(12f)
                        .Color("#91A2B6")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0D1824");
        }

        private UiElementBuilder BuildFormationCard(MassFlowNavPlaygroundPanelState state)
        {
            return Ui.Card(
                    Ui.Text("Formation Override").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Row(
                            BuildActionButton("None", state.FormationMode == "None", _ => SetMode(MassFlowFormationMode.None)),
                            BuildActionButton("Line", state.FormationMode == "Line", _ => SetMode(MassFlowFormationMode.Line)),
                            BuildActionButton("Square", state.FormationMode == "Square", _ => SetMode(MassFlowFormationMode.Square)),
                            BuildActionButton("Circle", state.FormationMode == "Circle", _ => SetMode(MassFlowFormationMode.Circle)),
                            BuildActionButton("Wedge", state.FormationMode == "Wedge", _ => SetMode(MassFlowFormationMode.Wedge)))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text($"Selected formation angle: {state.SelectedRotationDeg:0.0} deg")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Text($"Selection preview: {state.SelectionPreview}")
                        .FontSize(12f)
                        .Color("#B7C4D4")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(BuildActionButton("Clear Selection", false, _ => _runtime.ClearSelection(_engine!)))
                        .Gap(8f))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101A24");
        }

        private UiElementBuilder BuildGapCard(MassFlowNavPlaygroundPanelState state)
        {
            var lines = new UiElementBuilder[state.KnownGaps.Count];
            for (int i = 0; i < state.KnownGaps.Count; i++)
            {
                lines[i] = Ui.Text($"- {state.KnownGaps[i]}")
                    .FontSize(12f)
                    .Color("#C7D0DD")
                    .WhiteSpace(UiWhiteSpace.Normal);
            }

            return Ui.Card(
                    Ui.Text("Current Ludots Nav Gaps").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Column(lines).Gap(6f))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#161A22");
        }

        private UiElementBuilder BuildActionButton(string label, bool active, Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5E4518" : "#121B29")
                .Color(active ? "#FFF4D8" : "#F5F7FA");
        }

        private void SetUnits(int unitCount)
        {
            if (_engine != null)
            {
                _runtime.SetDesiredUnitCount(_engine, unitCount);
            }
        }

        private void SetSelectedFlow(int flowId)
        {
            if (_engine != null)
            {
                _runtime.SetSelectedTeamFlow(_engine, flowId);
            }
        }

        private void SetMode(MassFlowFormationMode mode)
        {
            if (_engine != null)
            {
                _runtime.SetFormationMode(_engine, mode);
            }
        }

        private MassFlowNavPlaygroundPanelState BuildState(GameEngine engine, MassFlowNavPlaygroundState runtimeState)
        {
            int selectedCount = SelectionContextRuntime.GetCurrentCount(engine.World, engine.GlobalContext);
            string selectionPreview = selectedCount <= 0
                ? "(none)"
                : $"{selectedCount} units selected";

            return new MassFlowNavPlaygroundPanelState(
                DesiredUnitCount: runtimeState.DesiredUnitCount,
                FriendlyCount: runtimeState.FriendlyCount,
                EnemyCount: runtimeState.EnemyCount,
                SelectedCount: selectedCount,
                ManualCount: runtimeState.ManualCount,
                GroupCount: runtimeState.Groups.Count,
                SelectedTeamFlowId: runtimeState.SelectedTeamFlowId,
                SelectedTeamFlowLabel: runtimeState.SelectedTeamFlowId == 0 ? "Team A" : "Team B",
                Flow0Target: DescribeFlowTarget(engine.World, runtimeState.Team0FlowGoalEntity),
                Flow1Target: DescribeFlowTarget(engine.World, runtimeState.Team1FlowGoalEntity),
                FormationMode: runtimeState.FormationMode.ToString(),
                SelectedRotationDeg: _runtime.GetSelectedFormationRotationDeg(engine),
                SelectionPreview: selectionPreview,
                KnownGaps: runtimeState.KnownNavGaps,
                FlowEnabled: engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime && navRuntime.FlowEnabled);
        }

        private static string DescribeFlowTarget(World world, Entity flowGoalEntity)
        {
            if (!world.IsAlive(flowGoalEntity) || !world.TryGet(flowGoalEntity, out NavFlowGoal2D goal))
            {
                return "(missing)";
            }

            (int x, int y) = goal.GoalCm.RoundToInt();
            return $"({x}, {y}) cm";
        }

        private sealed record MassFlowNavPlaygroundPanelState(
            int DesiredUnitCount,
            int FriendlyCount,
            int EnemyCount,
            int SelectedCount,
            int ManualCount,
            int GroupCount,
            int SelectedTeamFlowId,
            string SelectedTeamFlowLabel,
            string Flow0Target,
            string Flow1Target,
            string FormationMode,
            float SelectedRotationDeg,
            string SelectionPreview,
            System.Collections.Generic.IReadOnlyList<string> KnownGaps,
            bool FlowEnabled);
    }
}
