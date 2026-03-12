using System;
using System.Collections.Generic;
using Arch.Core;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace InteractionShowcaseMod.UI
{
    internal sealed class InteractionShowcasePanelController
    {
        private ReactivePage<InteractionShowcasePanelState>? _page;
        private UiScene? _mountedScene;
        private GameEngine? _engine;
        private ViewModeManager? _viewModeManager;

        public void MountOrRefresh(UIRoot root, GameEngine engine, string mapId, ViewModeManager? viewModeManager)
        {
            _engine = engine;
            _viewModeManager = viewModeManager;

            var nextState = BuildState(engine, mapId, viewModeManager);
            if (_page == null)
            {
                _page = new ReactivePage<InteractionShowcasePanelState>(nextState, BuildRoot);
            }
            else if (!_page.State.Equals(nextState))
            {
                _page.SetState(_ => nextState);
            }

            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                root.MountScene(_page.Scene);
            }

            _mountedScene = _page.Scene;
            root.IsDirty = true;
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
            }

            _mountedScene = null;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<InteractionShowcasePanelState> context)
        {
            var state = context.State;
            return Ui.Column(
                    BuildHeroStrip(state),
                    BuildModeCard(state),
                    BuildSelectionCard(state),
                    BuildCoverageCard(state),
                    state.IsStressMap ? BuildStressCard(state) : BuildSkillCard(state))
                .Width(560f)
                .Padding(18f)
                .Gap(12f)
                .Radius(24f)
                .Background("#08111A")
                .Absolute(16f, 16f)
                .ZIndex(30);
        }

        private UiElementBuilder BuildHeroStrip(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Ludots Interaction Showcase")
                        .FontSize(25f)
                        .Bold()
                        .Color("#F5F7FA"),
                    Ui.Text(state.MapDescription)
                        .FontSize(12f)
                        .Color("#B8C4D4")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildHeroChip(InteractionShowcaseIds.ArcweaverName, state.SelectedLabel.Contains(InteractionShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase)),
                            BuildHeroChip(InteractionShowcaseIds.VanguardName, state.SelectedLabel.Contains(InteractionShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase)),
                            BuildHeroChip(InteractionShowcaseIds.CommanderName, state.SelectedLabel.Contains(InteractionShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase)))
                        .Wrap()
                        .Gap(8f))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0D1824");
        }

        private UiElementBuilder BuildModeCard(InteractionShowcasePanelState state)
        {
            return Ui.Card(
                    Ui.Text("Reference Interactions").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Row(
                            BuildActionButton("WoW", state.ActiveModeId == InteractionShowcaseIds.WowModeId, _ => _viewModeManager?.SwitchTo(InteractionShowcaseIds.WowModeId)),
                            BuildActionButton("LoL", state.ActiveModeId == InteractionShowcaseIds.LolModeId, _ => _viewModeManager?.SwitchTo(InteractionShowcaseIds.LolModeId)),
                            BuildActionButton("SC2", state.ActiveModeId == InteractionShowcaseIds.Sc2ModeId, _ => _viewModeManager?.SwitchTo(InteractionShowcaseIds.Sc2ModeId)),
                            BuildActionButton("Indicator", state.ActiveModeId == InteractionShowcaseIds.IndicatorModeId, _ => _viewModeManager?.SwitchTo(InteractionShowcaseIds.IndicatorModeId)),
                            BuildActionButton("Action", state.ActiveModeId == InteractionShowcaseIds.ActionModeId, _ => _viewModeManager?.SwitchTo(InteractionShowcaseIds.ActionModeId)))
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
                    Ui.Text("Selection + Orders").FontSize(12f).Bold().Color("#F0C36B"),
                    Ui.Text($"Primary: {state.SelectedLabel}")
                        .FontSize(12f)
                        .Color("#F5F7FA"),
                    Ui.Text(state.SelectionSummary)
                        .FontSize(12f)
                        .Color("#C7D0DD")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text("LMB select | drag box-select | Tab cycle hover target | RMB move / confirm | Shift queue | S stop | F1-F5 switch reference feel.")
                        .FontSize(12f)
                        .Color("#93A4B8")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101A24");
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
                .Radius(999f);
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

        private static InteractionShowcasePanelState BuildState(GameEngine engine, string mapId, ViewModeManager? viewModeManager)
        {
            string selectedLabel = ResolveSelectedLabel(engine);
            string selectionSummary = ResolveSelectionSummary(engine, selectedLabel);
            string roster = ResolveRoster(engine.World);
            string skillSummary = ResolveSkillSummary(selectedLabel);

            var telemetry = ResolveStressTelemetry(engine);
            return new InteractionShowcasePanelState(
                MapId: mapId,
                MapDescription: InteractionShowcaseIds.DescribeMap(mapId),
                ActiveModeId: viewModeManager?.ActiveMode?.Id ?? string.Empty,
                ActiveModeName: viewModeManager?.ActiveMode?.DisplayName ?? "Unassigned",
                ModeSummary: ResolveModeSummary(viewModeManager?.ActiveMode?.Id),
                SelectedLabel: selectedLabel,
                SelectionSummary: selectionSummary,
                RosterSummary: roster,
                CoverageSummary: ResolveCoverageSummary(selectedLabel),
                SkillSummary: skillSummary,
                IsStressMap: mapId == InteractionShowcaseIds.StressMapId,
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
                BlueAnchorHealth: telemetry?.BlueAnchorHealth ?? 0f);
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
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj) ||
                selectedObj is not Entity selected ||
                !engine.World.IsAlive(selected))
            {
                return "(none)";
            }

            return engine.World.TryGet(selected, out Name name)
                ? name.Value
                : $"Entity#{selected.Id}";
        }

        private static string ResolveSelectionSummary(GameEngine engine, string selectedLabel)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) ||
                localObj is not Entity localPlayer ||
                !engine.World.IsAlive(localPlayer) ||
                !engine.World.Has<SelectionBuffer>(localPlayer))
            {
                return selectedLabel == "(none)"
                    ? "No live selection. Hub map supports single-select, drag multi-select, queue, and per-hero skill routing."
                    : $"1 unit selected: {selectedLabel}";
            }

            ref var selection = ref engine.World.Get<SelectionBuffer>(localPlayer);
            if (selection.Count <= 0)
            {
                return "Selection is empty. Drag a box around heroes to test RTS-style multi-cast fan-out.";
            }

            var names = new List<string>(selection.Count);
            for (int i = 0; i < selection.Count; i++)
            {
                Entity entity = selection.Get(i);
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
                ? $"{selection.Count} units selected."
                : string.Join(" | ", names);
            return $"{selection.Count} units selected. {preview}";
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
                InteractionShowcaseIds.ActionModeId => "Context-scored: Space routes through ContextGroup scoring and picks the best slot + target from live ECS state.",
                _ => "Switch modes to compare the same data-driven ability mappings under different interaction semantics."
            };
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
            string SelectedLabel,
            string SelectionSummary,
            string RosterSummary,
            string CoverageSummary,
            string SkillSummary,
            bool IsStressMap,
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
            float BlueAnchorHealth);
    }
}
