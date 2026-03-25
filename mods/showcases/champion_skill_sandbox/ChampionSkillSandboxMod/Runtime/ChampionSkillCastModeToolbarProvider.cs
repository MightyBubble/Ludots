using System;
using Arch.Core;
using CoreInputMod.ViewMode;
using EntityCommandPanelMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillCastModeToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private GameEngine? _engine;

        public bool IsVisible => ChampionSkillSandboxIds.IsSandboxMap(_engine?.CurrentMapSession?.MapId.Value);

        public uint Revision
        {
            get
            {
                uint revision = IsVisible ? 1u : 0u;
                string activeModeId = ResolveActiveModeId();
                string activeFollowModeId = ResolveActiveCameraFollowMode();
                if (!string.IsNullOrWhiteSpace(activeModeId))
                {
                    revision ^= (uint)activeModeId.GetHashCode(StringComparison.Ordinal);
                }

                if (!string.IsNullOrWhiteSpace(activeFollowModeId))
                {
                    revision ^= (uint)activeFollowModeId.GetHashCode(StringComparison.Ordinal);
                }

                string activeSelectionViewId = ResolveActiveSelectionViewId();
                if (!string.IsNullOrWhiteSpace(activeSelectionViewId))
                {
                    revision ^= (uint)activeSelectionViewId.GetHashCode(StringComparison.Ordinal);
                }

                string activeShowcaseThemeId = ResolveActiveShowcaseThemeId();
                if (!string.IsNullOrWhiteSpace(activeShowcaseThemeId))
                {
                    revision ^= (uint)activeShowcaseThemeId.GetHashCode(StringComparison.Ordinal);
                }

                if (ChampionSkillSandboxIds.IsStressMap(_engine?.CurrentMapSession?.MapId.Value))
                {
                    ChampionSkillStressControlState? control = ResolveStressControl();
                    ChampionSkillStressTelemetry? telemetry = ResolveStressTelemetry();
                    RenderDebugState? renderDebug = ResolveRenderDebugState();
                    revision ^= (uint)(control?.DesiredTeamA ?? 0);
                    revision ^= (uint)((control?.DesiredTeamB ?? 0) << 5);
                    revision ^= (uint)((telemetry?.LiveTeamA ?? 0) << 10);
                    revision ^= (uint)((telemetry?.LiveTeamB ?? 0) << 15);
                    revision ^= (uint)((telemetry?.ProjectileCount ?? 0) << 20);
                    revision ^= (renderDebug?.DrawWorldHudBars ?? true) ? 1u << 25 : 0u;
                    revision ^= (renderDebug?.DrawWorldHudText ?? true) ? 1u << 26 : 0u;
                    revision ^= (renderDebug?.DrawCombatText ?? true) ? 1u << 27 : 0u;
                }

                return revision;
            }
        }

        public string Title
        {
            get
            {
                string? mapId = _engine?.CurrentMapSession?.MapId.Value;
                if (ChampionSkillSandboxIds.IsStressMap(mapId))
                {
                    return "Stress Harness";
                }

                if (ChampionSkillSandboxIds.IsMusouBranchMap(mapId))
                {
                    return "Musou Branch";
                }

                if (ChampionSkillSandboxIds.IsMusouHitConfirmMap(mapId))
                {
                    return "Hit Confirm";
                }

                Entity selected = ResolveSelectedEntity();
                if (!IsNamedEntity(selected, ChampionSkillSandboxIds.DuelistAlphaName))
                {
                    return "Cast Mode";
                }

                return string.Equals(ResolveActiveModeId(), ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase)
                    ? "Duelist Action Combo"
                    : "Duelist Showcase";
            }
        }

        public string Subtitle
        {
            get
            {
                string? mapId = _engine?.CurrentMapSession?.MapId.Value;
                if (!ChampionSkillSandboxIds.IsStressMap(mapId))
                {
                    if (ChampionSkillSandboxIds.IsMusouBranchMap(mapId))
                    {
                        return "Q=Square chain | E=Triangle branch | W/R extra melee";
                    }

                    if (ChampionSkillSandboxIds.IsMusouHitConfirmMap(mapId))
                    {
                        return "Q must hit before Q/E follow-ups unlock";
                    }

                    return $"Theme {EntityCommandPanelShowcaseTheme.ResolveLabel(ResolveActiveShowcaseThemeId())} | {BuildSandboxSubtitle()}";
                }

                ChampionSkillStressControlState? control = ResolveStressControl();
                ChampionSkillStressTelemetry? telemetry = ResolveStressTelemetry();
                RenderDebugState? renderDebug = ResolveRenderDebugState();
                return $"View {ChampionSkillSandboxIds.ResolveSelectionViewLabel(ResolveActiveSelectionViewId())} | A {telemetry?.LiveTeamA ?? 0}/{control?.DesiredTeamA ?? 0} | B {telemetry?.LiveTeamB ?? 0}/{control?.DesiredTeamB ?? 0} | Proj {telemetry?.ProjectileCount ?? 0} peak {telemetry?.PeakProjectileCount ?? 0} | HUD {(renderDebug?.DrawWorldHudBars ?? true ? "B" : "-")}{(renderDebug?.DrawWorldHudText ?? true ? "T" : "-")}{(renderDebug?.DrawCombatText ?? true ? "F" : "-")}";
            }
        }

        public void Bind(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (!IsVisible || destination.IsEmpty)
            {
                return 0;
            }

            bool isStressMap = ChampionSkillSandboxIds.IsStressMap(_engine?.CurrentMapSession?.MapId.Value);
            string activeModeId = ResolveActiveModeId();
            string activeFollowModeId = ResolveActiveCameraFollowMode();
            string activeSelectionViewId = ResolveActiveSelectionViewId();
            string activeShowcaseThemeId = ResolveActiveShowcaseThemeId();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            var buttons = new EntityCommandPanelToolbarButtonView[isStressMap ? 20 : 11];
            buttons[0] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.SmartCastModeId,
                "Quick",
                string.Equals(activeModeId, ChampionSkillSandboxIds.SmartCastModeId, StringComparison.OrdinalIgnoreCase),
                "#F6C35B");
            buttons[1] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.IndicatorModeId,
                "Preview",
                string.Equals(activeModeId, ChampionSkillSandboxIds.IndicatorModeId, StringComparison.OrdinalIgnoreCase),
                "#61C3FF");
            buttons[2] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.PressReleaseModeId,
                "Confirm",
                string.Equals(activeModeId, ChampionSkillSandboxIds.PressReleaseModeId, StringComparison.OrdinalIgnoreCase),
                "#93E07A");
            buttons[3] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.ActionModeId,
                "Auto",
                string.Equals(activeModeId, ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase),
                "#F3DF86");
            buttons[4] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FreeCameraToolbarButtonId,
                "FreeCam",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FreeCameraToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                "#D7D2C4");
            buttons[5] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionToolbarButtonId,
                "Follow",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                "#8ED9A9");
            buttons[6] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId,
                "PackCam",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                "#F0C35A");
            buttons[7] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.ResetCameraToolbarButtonId,
                "ResetCam",
                false,
                "#D7D2C4");
            if (isStressMap)
            {
                buttons[8] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId,
                    "A-",
                    false,
                    "#FF9B7A");
                buttons[9] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId,
                    "A+",
                    false,
                    "#FF9B7A");
                buttons[10] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId,
                    "B-",
                    false,
                    "#67D4FF");
                buttons[11] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId,
                    "B+",
                    false,
                    "#67D4FF");
                buttons[12] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId,
                    "Bar",
                    renderDebug?.DrawWorldHudBars ?? true,
                    "#E2D7A6");
                buttons[13] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId,
                    "Text",
                    renderDebug?.DrawWorldHudText ?? true,
                    "#F2E3B3");
                buttons[14] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId,
                    "Float",
                    renderDebug?.DrawCombatText ?? true,
                    "#FFCF86");
                buttons[15] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId,
                    "P1",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                    "#98E7A7");
                buttons[16] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerFormationToolbarButtonId,
                    "P1F",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                    "#DAE89B");
                buttons[17] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiTargetToolbarButtonId,
                    "AI",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiTargetToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                    "#FFAE86");
                buttons[18] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiFormationToolbarButtonId,
                    "AIF",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                    "#F6CF79");
                buttons[19] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId,
                    "CMD",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId, StringComparison.OrdinalIgnoreCase),
                    "#7FD8F2");
            }
            else
            {
                buttons[8] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.Dota2Id,
                    "Dota2",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.OrdinalIgnoreCase),
                    "#D37A4B");
                buttons[9] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.LolId,
                    "LoL",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.LolId, StringComparison.OrdinalIgnoreCase),
                    "#D5B25B");
                buttons[10] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.Sc2Id,
                    "SC2",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.OrdinalIgnoreCase),
                    "#59B7FF");
            }

            int count = Math.Min(destination.Length, buttons.Length);
            buttons[..count].CopyTo(destination);
            return count;
        }

        public void Activate(string buttonId)
        {
            if (string.IsNullOrWhiteSpace(buttonId))
            {
                return;
            }

            if (string.Equals(buttonId, ChampionSkillSandboxIds.ResetCameraToolbarButtonId, StringComparison.OrdinalIgnoreCase))
            {
                if (_engine != null)
                {
                    _engine.GlobalContext[ChampionSkillSandboxIds.ResetCameraRequestKey] = true;
                }

                return;
            }

            if (ChampionSkillSandboxIds.IsCameraFollowMode(buttonId))
            {
                if (_engine != null)
                {
                    _engine.GlobalContext[ChampionSkillSandboxIds.CameraFollowModeKey] = buttonId;
                }

                return;
            }

            if (ChampionSkillSandboxIds.IsSelectionViewButton(buttonId))
            {
                if (_engine != null)
                {
                    _engine.GlobalContext[ChampionSkillSandboxIds.SelectionViewChoiceKey] = buttonId;
                }

                return;
            }

            ChampionSkillStressControlState? control = ResolveStressControl();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            if (control != null)
            {
                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    control.AdjustTeamA(-ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    control.AdjustTeamA(ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    control.AdjustTeamB(-ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    control.AdjustTeamB(ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudBars = !renderDebug.DrawWorldHudBars;
                    }

                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudText = !renderDebug.DrawWorldHudText;
                    }

                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawCombatText = !renderDebug.DrawCombatText;
                    }

                    return;
                }
            }

            if (EntityCommandPanelShowcaseTheme.IsThemeButton(buttonId))
            {
                if (_engine != null)
                {
                    _engine.GlobalContext[EntityCommandPanelShowcaseTheme.ContextKey] = EntityCommandPanelShowcaseTheme.Normalize(buttonId, ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId());
                }

                return;
            }

            ViewModeRuntime.TrySwitchTo(_engine?.GlobalContext!, buttonId);
        }

        private string ResolveActiveCameraFollowMode()
        {
            if (_engine?.GlobalContext.TryGetValue(ChampionSkillSandboxIds.CameraFollowModeKey, out var modeObj) == true &&
                modeObj is string modeId &&
                ChampionSkillSandboxIds.IsCameraFollowMode(modeId))
            {
                return modeId;
            }

            return ChampionSkillSandboxIds.FreeCameraToolbarButtonId;
        }

        private string ResolveActiveShowcaseThemeId()
        {
            if (_engine?.GlobalContext.TryGetValue(EntityCommandPanelShowcaseTheme.ContextKey, out var themeObj) == true &&
                themeObj is string themeId)
            {
                return EntityCommandPanelShowcaseTheme.Normalize(themeId, ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId());
            }

            return ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId();
        }

        private ChampionSkillStressControlState? ResolveStressControl()
        {
            return _engine?.GlobalContext.TryGetValue(ChampionSkillStressControlState.GlobalKey, out var value) == true &&
                   value is ChampionSkillStressControlState control
                ? control
                : null;
        }

        private ChampionSkillStressTelemetry? ResolveStressTelemetry()
        {
            return _engine?.GlobalContext.TryGetValue(ChampionSkillStressTelemetry.GlobalKey, out var value) == true &&
                   value is ChampionSkillStressTelemetry telemetry
                ? telemetry
                : null;
        }

        private RenderDebugState? ResolveRenderDebugState()
        {
            return _engine?.GetService(CoreServiceKeys.RenderDebugState);
        }

        private string ResolveActiveModeId()
        {
            if (_engine != null &&
                ViewModeRuntime.TryGetActiveModeId(_engine.GlobalContext, out string activeModeId) &&
                !string.IsNullOrWhiteSpace(activeModeId))
            {
                return activeModeId;
            }

            return ChampionSkillSandboxIds.ActionModeId;
        }

        private string ResolveActiveSelectionViewId()
        {
            if (_engine?.GlobalContext.TryGetValue(ChampionSkillSandboxIds.SelectionViewChoiceKey, out var value) == true &&
                value is string buttonId &&
                ChampionSkillSandboxIds.IsSelectionViewButton(buttonId))
            {
                return buttonId;
            }

            return ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId;
        }

        private string BuildSandboxSubtitle()
        {
            Entity selected = ResolveSelectedEntity();
            if (selected == Entity.Null)
            {
                return "1 Select Duelist Alpha | 2 Leave Auto mode on | 3 Hover D/E/F | 4 Tap Space for the melee route";
            }

            if (!IsNamedEntity(selected, ChampionSkillSandboxIds.DuelistAlphaName))
            {
                string selectedName = ResolveEntityName(selected);
                return string.IsNullOrWhiteSpace(selectedName)
                    ? "Select Duelist Alpha | Auto mode | Hover D/E/F | Space = auto melee | Q = manual chain"
                    : $"Current {selectedName} | Select Duelist Alpha for auto melee | Space = auto route | Q = manual chain";
            }

            if (!string.Equals(ResolveActiveModeId(), ChampionSkillSandboxIds.ActionModeId, StringComparison.OrdinalIgnoreCase))
            {
                return "Duelist ready | Press F5 for Auto mode | Hover D/E/F | Space = auto melee | Q = manual chain";
            }

            if (TryBuildDuelistActionSummary(selected, out string summary))
            {
                return summary;
            }

            return "Duelist auto melee | Hover D/E/F | Space = auto route | Q = manual chain | E = pack sweep";
        }

        private bool TryBuildDuelistActionSummary(Entity actor, out string summary)
        {
            summary = string.Empty;
            if (_engine == null ||
                actor == Entity.Null ||
                !_engine.World.IsAlive(actor))
            {
                return false;
            }

            Span<ContextScoredCandidateProbe> probes = stackalloc ContextScoredCandidateProbe[8];
            int actionContextAbilityId = AbilityIdRegistry.GetId("Ability.Champion.Duelist.ActionContext");
            bool resolved = ChampionSkillSandboxDuelistContextInspector.TryInspect(
                _engine,
                actionContextAbilityId,
                probes,
                out Entity inspectedActor,
                out Entity hovered,
                out _,
                out int probeCount,
                out ContextScoredOrderResolution resolution);

            if (inspectedActor != actor)
            {
                return false;
            }

            string hoverName = ResolveEntityName(hovered);
            string targetName = ResolveEntityName(resolution.Target);
            string qLabel = ResolveResolvedSlotAbilityLabel(actor, slotIndex: 0, fallback: "Chain Jab I");
            string eLabel = ResolveResolvedSlotAbilityLabel(actor, slotIndex: 2, fallback: "Crowd Sweep");

            if (!resolved || probeCount <= 0)
            {
                summary = $"Hover {(string.IsNullOrWhiteSpace(hoverName) ? "none" : hoverName)} | Auto scans the pack | Q manual {qLabel} | E {eLabel}";
                return true;
            }

            string spaceLabel = ResolveAbilityDisplayName(probes[0].AbilityId);
            if (string.IsNullOrWhiteSpace(spaceLabel))
            {
                return false;
            }

            string hoverSegment = string.IsNullOrWhiteSpace(hoverName) ? "Hover none" : $"Hover {hoverName}";
            string autoSegment = string.IsNullOrWhiteSpace(targetName)
                ? $"Auto {spaceLabel}"
                : $"Auto {spaceLabel} -> {targetName}";
            summary = $"{hoverSegment} | {autoSegment} | Q manual {qLabel} | E {eLabel}";
            return true;
        }

        private Entity ResolveSelectedEntity()
        {
            if (_engine == null)
            {
                return Entity.Null;
            }

            return SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity selected) &&
                   selected != Entity.Null &&
                   _engine.World.IsAlive(selected)
                ? selected
                : Entity.Null;
        }

        private Entity ResolveHoveredEntity()
        {
            if (_engine == null)
            {
                return Entity.Null;
            }

            return _engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hoveredObj) &&
                   hoveredObj is Entity hovered &&
                   hovered != Entity.Null &&
                   _engine.World.IsAlive(hovered)
                ? hovered
                : Entity.Null;
        }

        private bool IsNamedEntity(Entity entity, string expectedName)
        {
            return string.Equals(ResolveEntityName(entity), expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveEntityName(Entity entity)
        {
            if (_engine == null ||
                entity == Entity.Null ||
                !_engine.World.IsAlive(entity) ||
                !_engine.World.TryGet(entity, out Ludots.Core.Components.Name name))
            {
                return string.Empty;
            }

            return name.Value ?? string.Empty;
        }

        private string ResolveAbilityDisplayName(int abilityId)
        {
            if (abilityId <= 0)
            {
                return string.Empty;
            }

            if (_engine?.GetService(CoreServiceKeys.AbilityDefinitionRegistry) is AbilityDefinitionRegistry definitions &&
                definitions.TryGet(abilityId, out var definition) &&
                definition.HasPresentation &&
                definition.Presentation != null)
            {
                string displayName = definition.Presentation.ResolveDisplayName(string.Empty);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }

            string raw = AbilityIdRegistry.GetName(abilityId);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return $"Ability#{abilityId}";
            }

            int lastDot = raw.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < raw.Length
                ? raw[(lastDot + 1)..]
                : raw;
        }

        private string ResolveResolvedSlotAbilityLabel(Entity actor, int slotIndex, string fallback)
        {
            if (_engine == null ||
                actor == Entity.Null ||
                !_engine.World.IsAlive(actor) ||
                !_engine.World.Has<AbilityStateBuffer>(actor))
            {
                return fallback;
            }

            ref readonly AbilityStateBuffer abilities = ref _engine.World.Get<AbilityStateBuffer>(actor);
            if ((uint)slotIndex >= (uint)abilities.Count)
            {
                return fallback;
            }

            bool hasForm = _engine.World.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _engine.World.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasGranted = _engine.World.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _engine.World.Get<GrantedSlotBuffer>(actor) : default;
            var resolved = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, slotIndex);
            return resolved.AbilityId > 0
                ? ResolveAbilityDisplayName(resolved.AbilityId)
                : fallback;
        }
    }
}
