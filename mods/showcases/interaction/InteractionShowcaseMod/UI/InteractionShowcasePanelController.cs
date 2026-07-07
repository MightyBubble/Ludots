using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using CoreInputMod.ViewMode;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.UI;
using InteractionShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace InteractionShowcaseMod.UI
{
    internal sealed class InteractionShowcasePanelController
    {
        private readonly InteractionShowcaseRuntime _runtime;
        private ReactivePage<InteractionShowcasePanelState>? _page;
        private GameEngine? _engine;
        private UiSurfaceLeaseHandle _lease;

        public InteractionShowcasePanelController(InteractionShowcaseRuntime runtime)
        {
            _runtime = runtime;
        }

        public void MountOrRefresh(UIRoot root, GameEngine engine, string mapId)
        {
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return;
            }

            _engine = engine;

            var nextState = BuildState(engine, mapId);
            if (_page == null)
            {
                var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
                var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
                _page = new ReactivePage<InteractionShowcasePanelState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
            }
            else if (!_page.State.Equals(nextState))
            {
                _page.SetState(_ => nextState);
            }

            surfaceHost.PublishReactivePage(
                ref _lease,
                new UiSurfaceLeaseRequest("Showcase.Interaction.Panel", UiSurfaceSegment.Overlay, priority: 40),
                _page);
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (_lease.IsValid &&
                _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.ReleaseLease(ref _lease);
            }

            _engine = null;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<InteractionShowcasePanelState> context)
        {
            var state = context.State;
            return Ui.Column(BuildMainPanel(state))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(30);
        }

        private UiElementBuilder BuildMainPanel(InteractionShowcasePanelState state)
        {
            return Ui.Column(
                    BuildHeroStrip(state),
                    BuildWorkflowCard(state),
                    BuildDispatchCard(state))
                .Width(430f)
                .Padding(14f)
                .Gap(8f)
                .Radius(8f)
                .Background("#071019")
                .Absolute(16f, 16f)
                .ZIndex(30);
        }

        private UiElementBuilder BuildEntityInfoLayer(ReactiveContext<InteractionShowcasePanelState> context)
        {
            if (_engine?.GetService(EntityInfoPanelServiceKeys.Service) is not EntityInfoPanelService service)
            {
                return Ui.Column();
            }

            return EntityInfoPanelUiComposer.BuildLayer(service, context);
        }

        private UiElementBuilder BuildHeroStrip(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Hero Commands")
                        .FontSize(25f)
                        .Bold()
                        .Color("#F5F7FA"),
                    Ui.Text("Right-click move, blink routing, and command mode proof")
                        .FontSize(12f)
                        .Color("#B8C4D4")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildHeroChip(InteractionShowcaseIds.ArcweaverName, state.SelectedLabel.Contains(InteractionShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase)),
                            BuildHeroChip(InteractionShowcaseIds.VanguardName, state.SelectedLabel.Contains(InteractionShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase)),
                            BuildHeroChip(InteractionShowcaseIds.CommanderName, state.SelectedLabel.Contains(InteractionShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase)))
                        .Wrap()
                        .Gap(8f),
                    Ui.Row(
                            BuildMapButton("Hub", state.MapId == InteractionShowcaseIds.HubMapId, _ => LoadShowcaseMap(InteractionShowcaseIds.HubMapId)),
                            BuildActionButton("LoL", state.ActiveModeId == InteractionShowcaseIds.LolModeId, _ => SwitchMode(InteractionShowcaseIds.LolModeId)),
                            BuildActionButton("SC2", state.ActiveModeId == InteractionShowcaseIds.Sc2ModeId, _ => SwitchMode(InteractionShowcaseIds.Sc2ModeId)),
                            BuildActionButton("Action", state.ActiveModeId == InteractionShowcaseIds.ActionModeId, _ => SwitchMode(InteractionShowcaseIds.ActionModeId)))
                        .Wrap()
                        .Gap(8f))
                .Gap(8f)
                .Padding(14f)
                .Radius(8f)
                .Background("#0D1824");
        }

        private UiElementBuilder BuildWorkflowCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Given")
                        .FontSize(11f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text($"{state.LiveCount} hero(es) ready in {state.SelectionViewLabel}; leader = {state.SelectedLabel}")
                        .FontSize(13f)
                        .Color("#F5F7FA")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("When")
                        .FontSize(11f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text(state.WorkflowWhen)
                        .FontSize(13f)
                        .Color("#D7E1EC")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("Then")
                        .FontSize(11f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text(state.WorkflowThen)
                        .FontSize(13f)
                        .Color("#9EE493")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(7f)
                .Padding(12f)
                .Radius(8f)
                .Background("#101A24");
        }

        private UiElementBuilder BuildDispatchCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Command Routing")
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Row(
                            BuildStatusChip("command", state.ActiveCommandIntentLabel, true),
                            BuildStatusChip("target", state.PointerTargetFactsLabel, true),
                            BuildStatusChip("mode", state.DispatchProfileLabel, true))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text(state.CommandSourceSummary)
                        .FontSize(12f)
                        .Color("#F5F7FA")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.SchemeSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(8f)
                .Padding(12f)
                .Radius(8f)
                .Background("#0D1822");
        }

        private UiElementBuilder BuildSchemeCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Necessary Switches")
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Row(
                            BuildMapButton("Hub", state.MapId == InteractionShowcaseIds.HubMapId, _ => LoadShowcaseMap(InteractionShowcaseIds.HubMapId)),
                            BuildActionButton("LoL", state.ActiveModeId == InteractionShowcaseIds.LolModeId, _ => SwitchMode(InteractionShowcaseIds.LolModeId)),
                            BuildActionButton("SC2", state.ActiveModeId == InteractionShowcaseIds.Sc2ModeId, _ => SwitchMode(InteractionShowcaseIds.Sc2ModeId)),
                            BuildActionButton("Action", state.ActiveModeId == InteractionShowcaseIds.ActionModeId, _ => SwitchMode(InteractionShowcaseIds.ActionModeId)))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text(state.SchemeSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(8f)
                .Padding(12f)
                .Radius(8f)
                .Background("#101E2B");
        }

        private UiElementBuilder BuildModeCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Reference Interactions").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Row(
                            BuildActionButton("WoW", state.ActiveModeId == InteractionShowcaseIds.WowModeId, _ => SwitchMode(InteractionShowcaseIds.WowModeId)),
                            BuildActionButton("LoL", state.ActiveModeId == InteractionShowcaseIds.LolModeId, _ => SwitchMode(InteractionShowcaseIds.LolModeId)),
                            BuildActionButton("SC2", state.ActiveModeId == InteractionShowcaseIds.Sc2ModeId, _ => SwitchMode(InteractionShowcaseIds.Sc2ModeId)),
                            BuildActionButton("Indicator", state.ActiveModeId == InteractionShowcaseIds.IndicatorModeId, _ => SwitchMode(InteractionShowcaseIds.IndicatorModeId)),
                            BuildActionButton("Action", state.ActiveModeId == InteractionShowcaseIds.ActionModeId, _ => SwitchMode(InteractionShowcaseIds.ActionModeId)))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text(state.ModeSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildMapButton("Hub", state.MapId == InteractionShowcaseIds.HubMapId, _ => LoadShowcaseMap(InteractionShowcaseIds.HubMapId)),
                            BuildMapButton("Stress", state.MapId == InteractionShowcaseIds.StressMapId, _ => LoadShowcaseMap(InteractionShowcaseIds.StressMapId)))
                        .Wrap()
                        .Gap(8f))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101E2B");
        }

        private UiElementBuilder BuildSelectionCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Command Roster").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text($"Roster: {state.SelectionViewLabel}")
                        .FontSize(12f)
                        .Color("#F0C36B"),
                    Ui.Text($"Leader: {state.SelectedLabel}")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Text(state.SelectionSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("Choose a hero to open the inspector stack on the right and the RTS roster panel at the bottom.")
                        .FontSize(12f)
                        .Color("#E2C27A")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("LMB select | drag box-select | Ctrl+1..4 save group | 1..4 recall group | RMB move / confirm | Shift queue | S stop | F1-F5 switch reference feel.")
                        .FontSize(12f)
                        .Color("#93A4B8")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101A24");
        }

        private UiElementBuilder BuildSelectionDockCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Control Groups").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Row(
                            BuildSelectionViewButton("LIVE", state.LiveCount, state.SelectionViewMode == SelectionViewMode.Live, InteractionShowcaseIds.LiveSelectionButtonId, _ => ShowLiveSelection()),
                            BuildSelectionViewButton("SAVED", state.FormationCount, state.SelectionViewMode == SelectionViewMode.Formation, InteractionShowcaseIds.FormationSelectionButtonId, _ => ShowFormationSelection()))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Row(
                            BuildControlGroupButton(1, state.ActiveControlGroup == 1, state.Group1),
                            BuildControlGroupButton(2, state.ActiveControlGroup == 2, state.Group2),
                            BuildControlGroupButton(3, state.ActiveControlGroup == 3, state.Group3),
                            BuildControlGroupButton(4, state.ActiveControlGroup == 4, state.Group4))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text("Click G1-G4 to recall a saved group into the active roster. Saved view shows the last stored group.")
                        .FontSize(11f)
                        .Color("#93A4B8")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0D1822");
        }

        private UiElementBuilder BuildCoverageCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Coverage").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text(state.CoverageSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("Showcase focus: unit, point, direction, vector, self, toggle, double-tap, chord, context-scored routing, queue, multi-select fan-out, ring AoE, periodic zone, displacement, projectile, heal, buff and pressure throughput.")
                        .FontSize(12f)
                        .Color("#93A4B8")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0D1822");
        }

        private static UiElementBuilder BuildSkillCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Live Skill Sheet").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text(state.SkillSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"Roster: {state.RosterSummary}")
                        .FontSize(12f)
                        .Color("#93A4B8")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#112131");
        }

        private static UiElementBuilder BuildStressCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Stress Throughput").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text($"Requested: red {state.RequestedRed}/{state.DesiredPerSide} | blue {state.RequestedBlue}/{state.DesiredPerSide}")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Text($"Live: red {state.LiveRed} | blue {state.LiveBlue} | projectiles {state.ProjectileCount} (peak {state.PeakProjectileCount})")
                        .FontSize(12f)
                        .Color("#C7D0DD"),
                    Ui.Text($"Waves: {state.WavesDispatched} | orders issued: {state.OrdersIssued} | queue depth: {state.QueueDepth}")
                        .FontSize(12f)
                        .Color("#C7D0DD"),
                    Ui.Text($"Anchor HP: red {state.RedAnchorHealth:0} | blue {state.BlueAnchorHealth:0}")
                        .FontSize(12f)
                        .Color("#93A4B8"))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#161A22");
        }

        private static UiElementBuilder BuildHeroChip(string label, bool active)
        {
            return Ui.Text(label)
                .FontSize(12f)
                .Color(active ? "#08111A" : "#D5DEE8")
                .Background(active ? "#F0C36B" : "#1A2A3A")
                .Padding(8f, 6f)
                .Radius(8f);
        }

        private static UiElementBuilder BuildStatusChip(string label, string value, bool active)
        {
            return Ui.Text($"{label}: {value}")
                .FontSize(12f)
                .Color(active ? "#071019" : "#C7D0DD")
                .Background(active ? "#9EE493" : "#142230")
                .Padding(8f, 6f)
                .Radius(8f);
        }

        private static UiElementBuilder BuildMapButton(string label, bool active, Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#2C455A" : "#182234")
                .Color(active ? "#F5F7FA" : "#C7D0DD");
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5E4518" : "#121B29")
                .Color("#F5F7FA");
        }

        private static UiElementBuilder BuildSelectionViewButton(string label, int count, bool active, string elementId, Action<UiActionContext> onClick)
        {
            return Ui.Button($"{label} {count}", onClick)
                .Id(elementId)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#5E4518" : "#132232")
                .Color(active ? "#FFF4D8" : "#D5DEE8");
        }

        private UiElementBuilder BuildControlGroupButton(int groupIndex, bool active, SelectionGroupSummary summary)
        {
            string label = summary.Count <= 0
                ? $"G{groupIndex} empty"
                : $"G{groupIndex} {summary.Count}u {summary.PrimaryLabel}";
            return Ui.Button(label, _ =>
                {
                    if (_engine != null)
                    {
                        _runtime.RecallControlGroup(_engine, groupIndex);
                    }
                })
                .Id($"interaction-selection-group-{groupIndex}")
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#23415B" : (summary.Count > 0 ? "#152432" : "#111A24"))
                .Color(summary.Count > 0 ? "#F5F7FA" : "#7E93A8");
        }

        private void ShowLiveSelection()
        {
            if (_engine != null)
            {
                _runtime.ShowLiveSelection(_engine);
            }
        }

        private void ShowFormationSelection()
        {
            if (_engine != null)
            {
                _runtime.ShowFormationSelection(_engine);
            }
        }

        private void LoadShowcaseMap(string mapId)
        {
            if (_engine == null)
            {
                return;
            }

            string? currentMapId = _engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (InteractionShowcaseIds.IsShowcaseMap(currentMapId))
            {
                _engine.UnloadMap(currentMapId!);
            }

            _engine.LoadMap(mapId);
        }

        private void SwitchMode(string modeId)
        {
            if (_engine != null)
            {
                ViewModeRuntime.TrySwitchTo(_engine.GlobalContext, modeId);
            }
        }

        private static InteractionShowcasePanelState BuildState(GameEngine engine, string mapId)
        {
            string selectedLabel = ResolveSelectedLabel(engine);
            string selectionSummary = ResolveSelectionSummary(engine, selectedLabel);
            string roster = ResolveRoster(engine.World);
            string skillSummary = ResolveSkillSummary(selectedLabel);
            SelectionViewMode selectionViewMode = ResolveSelectionViewMode(engine);
            string selectionViewLabel = ResolveSelectionViewLabel(engine, selectionViewMode);
            int activeControlGroup = ResolveActiveControlGroup(engine);
            ResolveSelectionDockState(engine, out int liveCount, out int formationCount, out SelectionGroupSummary group1, out SelectionGroupSummary group2, out SelectionGroupSummary group3, out SelectionGroupSummary group4);
            ViewModeRuntime.TryGetActiveModeId(engine.GlobalContext, out string activeModeId);
            string activeModeName = ViewModeRuntime.TryGetActiveModeDisplayName(engine.GlobalContext, out string displayName)
                ? displayName
                : "Unassigned";

            var telemetry = ResolveStressTelemetry(engine);
            BlinkDispatchEvidence blinkEvidence = ResolveBlinkDispatchEvidence(engine);
            return new InteractionShowcasePanelState(
                MapId: mapId,
                MapDescription: InteractionShowcaseIds.DescribeMap(mapId),
                ActiveModeId: activeModeId,
                ActiveModeName: activeModeName,
                ModeSummary: ResolveModeSummary(activeModeId),
                SelectionViewMode: selectionViewMode,
                SelectionViewLabel: selectionViewLabel,
                ActiveControlGroup: activeControlGroup,
                SelectedLabel: selectedLabel,
                SelectionSummary: selectionSummary,
                LiveCount: liveCount,
                FormationCount: formationCount,
                Group1: group1,
                Group2: group2,
                Group3: group3,
                Group4: group4,
                RosterSummary: roster,
                CoverageSummary: ResolveCoverageSummary(selectedLabel),
                SkillSummary: skillSummary,
                IsStressMap: mapId == InteractionShowcaseIds.StressMapId,
                WorkflowWhen: ResolveWorkflowWhen(activeModeId, blinkEvidence),
                WorkflowThen: ResolveWorkflowThen(liveCount, selectedLabel, blinkEvidence),
                ActiveCommandIntentLabel: ResolveActiveCommandIntentLabel(engine),
                PointerTargetFactsLabel: ResolvePointerTargetFactsLabel(engine),
                DispatchProfileLabel: ResolveDispatchProfileLabel(blinkEvidence),
                CommandSourceSummary: ResolveCommandSourceSummary(engine, blinkEvidence),
                SchemeSummary: ResolveSchemeSummary(engine, blinkEvidence),
                DesiredPerSide: telemetry?.DesiredPerSide ?? 0,
                RequestedRed: telemetry?.RequestedRed ?? 0,
                RequestedBlue: telemetry?.RequestedBlue ?? 0,
                LiveRed: telemetry?.LiveRed ?? 0,
                LiveBlue: telemetry?.LiveBlue ?? 0,
                ProjectileCount: telemetry?.ProjectileCount ?? 0,
                PeakProjectileCount: telemetry?.PeakProjectileCount ?? 0,
                OrdersIssued: telemetry?.OrdersIssued ?? 0,
                WavesDispatched: telemetry?.WavesDispatched ?? 0,
                QueueDepth: telemetry?.QueueDepth ?? 0,
                RedAnchorHealth: telemetry?.RedAnchorHealth ?? 0f,
                BlueAnchorHealth: telemetry?.BlueAnchorHealth ?? 0f,
                EntityInfoUiRevision: engine.GetService(EntityInfoPanelServiceKeys.Service)?.UiRevision ?? 0);
        }

        private static InteractionShowcaseStressTelemetry? ResolveStressTelemetry(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InteractionShowcaseStressTelemetry.GlobalKey, out var value) &&
                   value is InteractionShowcaseStressTelemetry telemetry
                ? telemetry
                : null;
        }

        private static string ResolveSelectedLabel(GameEngine engine)
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected))
            {
                return "(none)";
            }

            return engine.World.TryGet(selected, out Name name)
                ? name.Value
                : $"Entity#{selected.Id}";
        }

        private static string ResolveSelectionSummary(GameEngine engine, string selectedLabel)
        {
            Entity[] selection = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            if (selection.Length <= 0)
            {
                return "Roster is empty. Drag a box around heroes to test RTS-style multi-cast fan-out.";
            }

            var names = new List<string>(selection.Length);
            for (int i = 0; i < selection.Length; i++)
            {
                Entity entity = selection[i];
                if (!engine.World.IsAlive(entity))
                {
                    continue;
                }

                if (engine.World.TryGet(entity, out Name name))
                {
                    names.Add(name.Value);
                }
            }

            string preview = names.Count == 0
                ? $"{selection.Length} units active."
                : string.Join(" | ", names);
            return $"{selection.Length} units active. {preview}";
        }

        private static void ResolveSelectionDockState(
            GameEngine engine,
            out int liveCount,
            out int formationCount,
            out SelectionGroupSummary group1,
            out SelectionGroupSummary group2,
            out SelectionGroupSummary group3,
            out SelectionGroupSummary group4)
        {
            liveCount = 0;
            formationCount = 0;
            group1 = SelectionGroupSummary.Empty;
            group2 = SelectionGroupSummary.Empty;
            group3 = SelectionGroupSummary.Empty;
            group4 = SelectionGroupSummary.Empty;

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
                viewerObj is not Entity viewer ||
                !engine.World.IsAlive(viewer) ||
                engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime selection)
            {
                return;
            }

            liveCount = selection.GetSelectionCount(viewer, SelectionSetKeys.LivePrimary);
            formationCount = selection.GetSelectionCount(viewer, SelectionSetKeys.FormationPrimary);
            group1 = ResolveControlGroupSummary(engine, selection, viewer, 1);
            group2 = ResolveControlGroupSummary(engine, selection, viewer, 2);
            group3 = ResolveControlGroupSummary(engine, selection, viewer, 3);
            group4 = ResolveControlGroupSummary(engine, selection, viewer, 4);
        }

        private static SelectionGroupSummary ResolveControlGroupSummary(GameEngine engine, SelectionRuntime selection, Entity viewer, int groupIndex)
        {
            if (!SelectionControlGroupRuntime.TryDescribeControlGroup(engine.World, selection, viewer, groupIndex, out SelectionContainerDescriptor descriptor))
            {
                return SelectionGroupSummary.Empty;
            }

            string primaryLabel = descriptor.Primary != Entity.Null && engine.World.IsAlive(descriptor.Primary) && engine.World.TryGet(descriptor.Primary, out Name name)
                ? name.Value
                : descriptor.MemberCount > 0
                    ? $"#{descriptor.Primary.Id}"
                    : string.Empty;
            return new SelectionGroupSummary(descriptor.MemberCount, primaryLabel);
        }

        private static SelectionViewMode ResolveSelectionViewMode(GameEngine engine)
        {
            return SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor descriptor) &&
                   string.Equals(descriptor.ViewKey, SelectionViewKeys.Formation, StringComparison.Ordinal)
                ? SelectionViewMode.Formation
                : SelectionViewMode.Live;
        }

        private static string ResolveSelectionViewLabel(GameEngine engine, SelectionViewMode mode)
        {
            if (!SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out _))
            {
                return mode == SelectionViewMode.Formation ? "Formation view" : "Live view";
            }

            return mode == SelectionViewMode.Formation
                ? "Formation view"
                : "Live view";
        }

        private static int ResolveActiveControlGroup(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InteractionShowcaseIds.ActiveControlGroupKey, out object? groupObj) &&
                   groupObj is int groupIndex
                ? groupIndex
                : 0;
        }

        private static string ResolveRoster(World world)
        {
            var names = new List<string>(8);
            var query = new QueryDescription().WithAll<Name, Ludots.Core.Gameplay.Components.PlayerOwner>();
            world.Query(in query, (Entity _, ref Name name, ref Ludots.Core.Gameplay.Components.PlayerOwner owner) =>
            {
                if (owner.PlayerId == 1 && !string.IsNullOrWhiteSpace(name.Value))
                {
                    names.Add(name.Value);
                }
            });

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.Count == 0
                ? "No controllable units loaded."
                : string.Join(" | ", names);
        }

        private static string ResolveModeSummary(string? modeId)
        {
            return modeId switch
            {
                InteractionShowcaseIds.WowModeId => "Target-first: preselect a unit, then fire abilities without cursor acquisition. This matches MMO command semantics.",
                InteractionShowcaseIds.LolModeId => "Smart-cast: key press immediately resolves hovered unit or cursor ground point. This matches LoL quick cast.",
                InteractionShowcaseIds.Sc2ModeId => "Aim-cast: key arms the skill, left click confirms, right click cancels. This matches RTS/MOBA confirm flows.",
                InteractionShowcaseIds.IndicatorModeId => "Indicator-release: hold to preview ring/line/cone overlays, release to fire. This matches quick-cast with indicator.",
                InteractionShowcaseIds.ActionModeId => "Context-scored: Space picks the best action and target from the current combat state.",
                _ => "Switch modes to compare the same data-driven ability mappings under different interaction semantics."
            };
        }

        private static string ResolveWorkflowWhen(string activeModeId, BlinkDispatchEvidence blinkEvidence)
        {
            if (blinkEvidence.Enabled)
            {
                return blinkEvidence.ProfileId switch
                {
                    "dispatch.all_together" => "Blink W uses All Together routing; every hero should blink together.",
                    "dispatch.one_by_one" => "Blink W uses One By One routing; this trigger should pick one hero.",
                    "dispatch.nearest_top_n" => "Blink W uses Nearest Top-N routing; the target point ranks the heroes.",
                    _ => "Blink W is resolving from the active hero group."
                };
            }

            return activeModeId switch
            {
                InteractionShowcaseIds.Sc2ModeId => "Press W to arm, confirm with LMB/RMB; the aim stays active until confirmed.",
                InteractionShowcaseIds.ActionModeId => "Press Space; the best available action and target are chosen.",
                _ => "Right-click ground or press WASD; the active command mode chooses the order.",
            };
        }

        private static string ResolveWorkflowThen(int liveCount, string selectedLabel, BlinkDispatchEvidence blinkEvidence)
        {
            if (blinkEvidence.Enabled)
            {
                if (!blinkEvidence.Valid)
                {
                    return blinkEvidence.SelectedNames;
                }

                return $"{blinkEvidence.SelectedCount}/{blinkEvidence.ActorCount} hero(es) blink: {blinkEvidence.SelectedNames}. Routing = {blinkEvidence.RoutingLabel}.";
            }

            if (liveCount <= 0)
            {
                return "No hero is active yet; this capture is not valid player evidence.";
            }

            return $"{liveCount} hero(es) receive the command; leader {selectedLabel} remains visible.";
        }

        private static string ResolveDispatchProfileLabel(BlinkDispatchEvidence blinkEvidence)
        {
            if (!blinkEvidence.Enabled)
            {
                return "All Together";
            }

            if (!blinkEvidence.Valid)
            {
                return "Blink not ready";
            }

            return $"{ResolveDispatchDisplayName(blinkEvidence.ProfileId)} {blinkEvidence.SelectedCount}/{blinkEvidence.ActorCount} {blinkEvidence.RoutingLabel}";
        }

        private static string ResolveActiveCommandIntentLabel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.InteractionContextStack) is not Ludots.Core.Input.Interaction.InteractionContextStack stack ||
                engine.GetService(CoreServiceKeys.ControlSchemeRuntime) is not Ludots.Core.Input.Interaction.ControlSchemeRuntime schemes)
            {
                return "missing";
            }

            int intentId = Ludots.Core.Input.Interaction.CommandIntentArbiter.ResolveActiveCommandIntent(stack, schemes);
            return intentId == 0 ? "none" : ResolveIntentDisplayName(stack.CommandIntentProfileIdRegistry.GetName(intentId));
        }

        private static string ResolvePointerTargetFactsLabel(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentHovered(engine.World, engine.GlobalContext, out Entity hovered) &&
                   hovered != Entity.Null &&
                   engine.World.IsAlive(hovered)
                ? "ground; hover ignored"
                : "ground";
        }

        private static string ResolveCommandSourceSummary(GameEngine engine, BlinkDispatchEvidence blinkEvidence)
        {
            if (blinkEvidence.Enabled && blinkEvidence.Valid)
            {
                return $"Command group: {blinkEvidence.ActorCount} mixed hero(es): {blinkEvidence.ActorNames}.";
            }

            if (engine.GetService(CoreServiceKeys.InteractionContextStack) is not Ludots.Core.Input.Interaction.InteractionContextStack stack ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not Ludots.Core.EntityCollections.EntityCollectionStore collections ||
                !stack.TryPeek(out Ludots.Core.Input.Interaction.InteractionContextFrame frame))
            {
                return "Command group is not ready.";
            }

            Entity owner = Entity.Null;
            if (frame.ContextEntity != Entity.Null &&
                engine.World.IsAlive(frame.ContextEntity) &&
                collections.TryGet(frame.ContextEntity, frame.ActiveCollectionKeyId, out Ludots.Core.EntityCollections.EntityCollectionHandle contextHandle) &&
                collections.TryGetView(contextHandle, out Ludots.Core.EntityCollections.EntityCollectionView contextView))
            {
                owner = frame.ContextEntity;
                return $"Command group: {contextView.Count} hero(es).";
            }

            if (engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) &&
                localPlayer != Entity.Null &&
                engine.World.IsAlive(localPlayer) &&
                collections.TryGet(localPlayer, frame.ActiveCollectionKeyId, out Ludots.Core.EntityCollections.EntityCollectionHandle localHandle) &&
                collections.TryGetView(localHandle, out Ludots.Core.EntityCollections.EntityCollectionView localView))
            {
                return $"Command group: {localView.Count} hero(es).";
            }

            return "Command group is empty; this capture is not valid player evidence.";
        }

        private static string ResolveSchemeSummary(GameEngine engine, BlinkDispatchEvidence blinkEvidence)
        {
            if (engine.GetService(CoreServiceKeys.ControlSchemeRuntime) is not Ludots.Core.Input.Interaction.ControlSchemeRuntime schemes ||
                schemes.ActiveSchemeId == 0)
            {
                return "No active command mode; input is waiting for a mode.";
            }

            string scheme = schemes.SchemeIdRegistry.GetName(schemes.ActiveSchemeId);
            string movement = schemes.TryGetActiveAxisMove(out _) ? "WASD movement on" : "WASD movement off";
            if (blinkEvidence.Enabled)
            {
                return $"{ResolveSchemeDisplayName(scheme)}: {movement}; blink lands at the marked ground point.";
            }

            return $"{ResolveSchemeDisplayName(scheme)}: {movement}";
        }

        private static string ResolveDispatchDisplayName(string profileId)
        {
            return profileId switch
            {
                "dispatch.all_together" => "All Together",
                "dispatch.one_by_one" => "One By One",
                "dispatch.nearest_top_n" => "Nearest Top-N",
                _ => "Dispatch"
            };
        }

        private static string ResolveIntentDisplayName(string intentId)
        {
            return intentId switch
            {
                "intent.command.default" => "Ground Command",
                _ => "Command"
            };
        }

        private static string ResolveSchemeDisplayName(string schemeId)
        {
            return schemeId switch
            {
                "scheme.default" => "Default Command",
                "scheme.wasd_move" => "WASD Move",
                _ => "Active Scheme"
            };
        }

        private static BlinkDispatchEvidence ResolveBlinkDispatchEvidence(GameEngine engine)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(InteractionShowcaseIds.AutoBlinkTimelineEnvKey), "1", StringComparison.Ordinal))
            {
                return BlinkDispatchEvidence.Disabled;
            }

            int frame = ResolveVisibleUatFrame(engine);
            string profileId = frame < 90
                ? "dispatch.all_together"
                : frame < 180
                    ? "dispatch.one_by_one"
                    : "dispatch.nearest_top_n";

            if (!TrySnapshotCommandSourceActors(engine, out Entity[] actors) || actors.Length <= 0)
            {
                return BlinkDispatchEvidence.Invalid(frame, profileId, "Blink proof is not ready: the active hero group is empty.");
            }

            if (engine.GetService(CoreServiceKeys.CastDispatchProfileRegistry) is not CastDispatchProfileRegistry dispatch)
            {
                return BlinkDispatchEvidence.Invalid(frame, profileId, "Blink proof is not ready: routing data is missing.");
            }

            if (!dispatch.ProfileIdRegistry.TryGetId(profileId, out int registryId))
            {
                return BlinkDispatchEvidence.Invalid(frame, profileId, "Blink proof is not ready: this routing mode is unavailable.");
            }

            var ctx = new CastDispatchContext(engine.World, new Vector3(2080f, 0f, 1080f), groupKey: 581_650L);
            var selected = new Entity[actors.Length];
            int selectedCount = dispatch.SelectDispatchTargets(registryId, actors, in ctx, selected, out CastDispatchRouting routing);
            return new BlinkDispatchEvidence(
                Enabled: true,
                Valid: true,
                Frame: frame,
                ProfileId: profileId,
                RegistryId: registryId,
                ActorCount: actors.Length,
                ActorNames: FormatEntityNames(engine.World, actors.AsSpan()),
                SelectedCount: selectedCount,
                SelectedNames: FormatEntityNames(engine.World, selected.AsSpan(0, selectedCount)),
                SharedOrderId: routing.SharedOrderId,
                Sequential: routing.Sequential);
        }

        private static int ResolveVisibleUatFrame(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(InteractionShowcaseIds.VisibleUatFrameKey, out object? frameObj) &&
                   frameObj is int frame
                ? frame
                : 0;
        }

        private static bool TrySnapshotCommandSourceActors(GameEngine engine, out Entity[] actors)
        {
            actors = Array.Empty<Entity>();
            if (engine.GetService(CoreServiceKeys.InteractionContextStack) is not InteractionContextStack stack ||
                engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections ||
                !stack.TryPeek(out InteractionContextFrame frame))
            {
                return false;
            }

            Entity owner = Entity.Null;
            if (frame.ContextEntity != Entity.Null && engine.World.IsAlive(frame.ContextEntity))
            {
                owner = frame.ContextEntity;
            }
            else if (engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) &&
                     localPlayer != Entity.Null &&
                     engine.World.IsAlive(localPlayer))
            {
                owner = localPlayer;
            }

            if (owner == Entity.Null ||
                !collections.TryGet(owner, frame.ActiveCollectionKeyId, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                return false;
            }

            actors = new Entity[view.Count];
            int copied = collections.CopyEntities(handle, 0, actors);
            if (copied == actors.Length)
            {
                return true;
            }

            Array.Resize(ref actors, copied);
            return copied > 0;
        }

        private static string FormatEntityNames(World world, ReadOnlySpan<Entity> entities)
        {
            if (entities.IsEmpty)
            {
                return "(none)";
            }

            var names = new List<string>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entity == Entity.Null || !world.IsAlive(entity))
                {
                    continue;
                }

                names.Add(world.TryGet(entity, out Name name)
                    ? name.Value
                    : $"Entity#{entity.Id}");
            }

            return names.Count == 0
                ? "(none)"
                : string.Join(" | ", names);
        }

        private static string ResolveCoverageSummary(string selectedLabel)
        {
            if (selectedLabel.Contains(InteractionShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase))
            {
                return "Arcweaver: Q unit duel-bolt, W point blink, E directional lance, R self nova, Z double-tap dash, F guard toggle, Space context-scored action, X+C vector rune line.";
            }

            if (selectedLabel.Contains(InteractionShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase))
            {
                return "Vanguard: Q unit challenge, W point leap, E cone cleave, R self ring shockwave, Z double-tap charge, F iron-wall toggle, Space context action, X+C advanced slam.";
            }

            if (selectedLabel.Contains(InteractionShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase))
            {
                return "Commander: Q allied unit support beam, W point tactical jump, E directional volley, R self overclock, Z double-tap thrust, F shield-net toggle, Space context action, X+C orbital vector strike.";
            }

            return "Select a hero to see the slot-to-mechanic mapping. Multi-select then cast move/stop/skills to validate RTS fan-out behavior.";
        }

        private static string ResolveSkillSummary(string selectedLabel)
        {
            if (selectedLabel.Contains(InteractionShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase))
            {
                return "Q DuelBolt (unit) | W BlinkStep (point) | E FireLance (direction) | R NovaPulse (self) | Z ArcDash (double-tap) | F GuardToggle (toggle) | Space ActionContext | X+C RuneBurst (vector)";
            }

            if (selectedLabel.Contains(InteractionShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase))
            {
                return "Q Challenge (unit) | W BannerLeap (point) | E CleaveCone (direction) | R Shockwave (ring self) | Z ChargeDash (double-tap) | F IronWall (toggle) | Space ActionContext | X+C GroundSlam (advanced)";
            }

            if (selectedLabel.Contains(InteractionShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase))
            {
                return "Q SupportBeam (ally unit) | W TacticalJump (point) | E VolleyLine (direction) | R Overclock (self buff) | Z ThrustJump (double-tap) | F ShieldNet (toggle) | Space ActionContext | X+C OrbitalStrike (vector)";
            }

            return "Primary verbs are bound to Q/W/E/R/Z/F/Space/X+C. Switch modes with F1-F5 and use Shift to queue orders.";
        }

        private sealed record InteractionShowcasePanelState(
            string MapId,
            string MapDescription,
            string ActiveModeId,
            string ActiveModeName,
            string ModeSummary,
            SelectionViewMode SelectionViewMode,
            string SelectionViewLabel,
            int ActiveControlGroup,
            string SelectedLabel,
            string SelectionSummary,
            int LiveCount,
            int FormationCount,
            SelectionGroupSummary Group1,
            SelectionGroupSummary Group2,
            SelectionGroupSummary Group3,
            SelectionGroupSummary Group4,
            string RosterSummary,
            string CoverageSummary,
            string SkillSummary,
            bool IsStressMap,
            string WorkflowWhen,
            string WorkflowThen,
            string ActiveCommandIntentLabel,
            string PointerTargetFactsLabel,
            string DispatchProfileLabel,
            string CommandSourceSummary,
            string SchemeSummary,
            int DesiredPerSide,
            int RequestedRed,
            int RequestedBlue,
            int LiveRed,
            int LiveBlue,
            int ProjectileCount,
            int PeakProjectileCount,
            int OrdersIssued,
            int WavesDispatched,
            int QueueDepth,
            float RedAnchorHealth,
            float BlueAnchorHealth,
            int EntityInfoUiRevision);

        private readonly record struct BlinkDispatchEvidence(
            bool Enabled,
            bool Valid,
            int Frame,
            string ProfileId,
            int RegistryId,
            int ActorCount,
            string ActorNames,
            int SelectedCount,
            string SelectedNames,
            bool SharedOrderId,
            bool Sequential)
        {
            public static BlinkDispatchEvidence Disabled => new(
                Enabled: false,
                Valid: false,
                Frame: 0,
                ProfileId: string.Empty,
                RegistryId: 0,
                ActorCount: 0,
                ActorNames: string.Empty,
                SelectedCount: 0,
                SelectedNames: string.Empty,
                SharedOrderId: false,
                Sequential: false);

            public static BlinkDispatchEvidence Invalid(int frame, string profileId, string message) => new(
                Enabled: true,
                Valid: false,
                Frame: frame,
                ProfileId: profileId,
                RegistryId: 0,
                ActorCount: 0,
                ActorNames: string.Empty,
                SelectedCount: 0,
                SelectedNames: message,
                SharedOrderId: false,
                Sequential: false);

            public string RoutingLabel => Sequential
                ? "sequential"
                : SharedOrderId
                    ? "shared parallel"
                    : "parallel";
        }

        private readonly record struct SelectionGroupSummary(int Count, string PrimaryLabel)
        {
            public static SelectionGroupSummary Empty => new(0, string.Empty);
        }

        private enum SelectionViewMode : byte
        {
            Live = 0,
            Formation = 1
        }
    }
}
