using EntityCommandPanelMod.Runtime;
using EntityCommandPanelMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillSandboxToolbarTail : IEntityCommandPanelToolbarTail
    {
        private readonly GameEngine _engine;

        public ChampionSkillSandboxToolbarTail(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public string? Title => null;

        public string? Subtitle => AppliesToCurrentMap ? BuildSubtitle() : null;

        private string BuildSubtitle()
        {
            if (!ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value))
            {
                return $"Theme {EntityCommandPanelShowcaseTheme.ResolveLabel(ResolveActiveShowcaseThemeId())} | Cast + Camera | F1/F2/F3 Cast | RMB Move";
            }

            ChampionSkillStressControlState? control = ResolveStressControl();
            ChampionSkillStressTelemetry? telemetry = ResolveStressTelemetry();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            return $"View {ChampionSkillSandboxIds.ResolveSelectionViewLabel(ResolveActiveSelectionViewId())} | A {telemetry?.LiveTeamA ?? 0}/{control?.DesiredTeamA ?? 0} | B {telemetry?.LiveTeamB ?? 0}/{control?.DesiredTeamB ?? 0} | Proj {telemetry?.ProjectileCount ?? 0} peak {telemetry?.PeakProjectileCount ?? 0} | HUD {(renderDebug?.DrawWorldHudBars ?? true ? "B" : "-")}{(renderDebug?.DrawWorldHudText ?? true ? "T" : "-")}{(renderDebug?.DrawCombatText ?? true ? "F" : "-")}";
        }

        public uint Revision => AppliesToCurrentMap ? BuildRevision() : 0;

        private uint BuildRevision()
        {
            uint revision = 1u;
            string activeFollowModeId = ResolveActiveCameraFollowMode();
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

            if (ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value))
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

        public int ButtonCount => AppliesToCurrentMap
            ? 4 + (ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value) ? 12 : 3)
            : 0;

        public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
        {
            if (!AppliesToCurrentMap || destination.IsEmpty)
            {
                return 0;
            }

            bool isStressMap = ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value);
            string activeFollowModeId = ResolveActiveCameraFollowMode();
            string activeSelectionViewId = ResolveActiveSelectionViewId();
            string activeShowcaseThemeId = ResolveActiveShowcaseThemeId();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            var buttons = new EntityCommandPanelToolbarButtonView[ButtonCount];
            buttons[0] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FreeCameraToolbarButtonId,
                "Free",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FreeCameraToolbarButtonId, StringComparison.Ordinal),
                "#D7D2C4");
            buttons[1] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionToolbarButtonId,
                "Follow",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.Ordinal),
                "#8ED9A9");
            buttons[2] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId,
                "Group",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.Ordinal),
                "#F0C35A");
            buttons[3] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.ResetCameraToolbarButtonId,
                "Reset",
                false,
                "#D7D2C4");
            if (isStressMap)
            {
                buttons[4] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId,
                    "A-",
                    false,
                    "#FF9B7A");
                buttons[5] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId,
                    "A+",
                    false,
                    "#FF9B7A");
                buttons[6] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId,
                    "B-",
                    false,
                    "#67D4FF");
                buttons[7] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId,
                    "B+",
                    false,
                    "#67D4FF");
                buttons[8] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId,
                    "Bar",
                    renderDebug?.DrawWorldHudBars ?? true,
                    "#E2D7A6");
                buttons[9] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId,
                    "Text",
                    renderDebug?.DrawWorldHudText ?? true,
                    "#F2E3B3");
                buttons[10] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId,
                    "Float",
                    renderDebug?.DrawCombatText ?? true,
                    "#FFCF86");
                buttons[11] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId,
                    "P1",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId, StringComparison.Ordinal),
                    "#98E7A7");
                buttons[12] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerFormationToolbarButtonId,
                    "P1F",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerFormationToolbarButtonId, StringComparison.Ordinal),
                    "#DAE89B");
                buttons[13] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiTargetToolbarButtonId,
                    "AI",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiTargetToolbarButtonId, StringComparison.Ordinal),
                    "#FFAE86");
                buttons[14] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiFormationToolbarButtonId,
                    "AIF",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiFormationToolbarButtonId, StringComparison.Ordinal),
                    "#F6CF79");
                buttons[15] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId,
                    "CMD",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId, StringComparison.Ordinal),
                    "#7FD8F2");
            }
            else
            {
                buttons[4] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.Dota2Id,
                    "Dota2",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal),
                    "#D37A4B");
                buttons[5] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.LolId,
                    "LoL",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.LolId, StringComparison.Ordinal),
                    "#D5B25B");
                buttons[6] = new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseTheme.Sc2Id,
                    "SC2",
                    string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal),
                    "#59B7FF");
            }

            int count = Math.Min(destination.Length, buttons.Length);
            buttons.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public bool TryActivate(string buttonId)
        {
            if (!AppliesToCurrentMap)
            {
                return false;
            }

            if (string.Equals(buttonId, ChampionSkillSandboxIds.ResetCameraToolbarButtonId, StringComparison.Ordinal))
            {
                _engine.GlobalContext[ChampionSkillSandboxIds.ResetCameraRequestKey] = true;
                return true;
            }

            if (ChampionSkillSandboxIds.IsCameraFollowMode(buttonId))
            {
                _engine.GlobalContext[ChampionSkillSandboxIds.CameraFollowModeKey] = buttonId;
                return true;
            }

            if (ChampionSkillSandboxIds.IsSelectionViewButton(buttonId))
            {
                _engine.GlobalContext[ChampionSkillSandboxIds.SelectionViewChoiceKey] = buttonId;
                return true;
            }

            ChampionSkillStressControlState? control = ResolveStressControl();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            if (control != null)
            {
                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamA(-ChampionSkillStressControlState.Step);
                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamA(ChampionSkillStressControlState.Step);
                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamB(-ChampionSkillStressControlState.Step);
                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamB(ChampionSkillStressControlState.Step);
                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId, StringComparison.Ordinal))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudBars = !renderDebug.DrawWorldHudBars;
                    }

                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId, StringComparison.Ordinal))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudText = !renderDebug.DrawWorldHudText;
                    }

                    return true;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId, StringComparison.Ordinal))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawCombatText = !renderDebug.DrawCombatText;
                    }

                    return true;
                }
            }

            if (EntityCommandPanelShowcaseTheme.IsThemeButton(buttonId))
            {
                _engine.GlobalContext[EntityCommandPanelShowcaseTheme.ContextKey] = EntityCommandPanelShowcaseTheme.Normalize(
                    buttonId,
                    ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId());
                return true;
            }

            return false;
        }

        private bool AppliesToCurrentMap =>
            ChampionSkillSandboxIds.IsSandboxMap(_engine.CurrentMapSession?.MapId.Value);

        private string ResolveActiveCameraFollowMode()
        {
            if (_engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.CameraFollowModeKey, out var modeObj) &&
                modeObj is string modeId &&
                ChampionSkillSandboxIds.IsCameraFollowMode(modeId))
            {
                return modeId;
            }

            return ChampionSkillSandboxIds.FreeCameraToolbarButtonId;
        }

        private string ResolveActiveShowcaseThemeId()
        {
            if (_engine.GlobalContext.TryGetValue(EntityCommandPanelShowcaseTheme.ContextKey, out var themeObj) &&
                themeObj is string themeId)
            {
                return EntityCommandPanelShowcaseTheme.Normalize(themeId, ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId());
            }

            return ChampionSkillSandboxIds.ResolveDefaultShowcaseThemeId();
        }

        private ChampionSkillStressControlState? ResolveStressControl()
        {
            return _engine.GlobalContext.TryGetValue(ChampionSkillStressControlState.GlobalKey, out var value) &&
                   value is ChampionSkillStressControlState control
                ? control
                : null;
        }

        private ChampionSkillStressTelemetry? ResolveStressTelemetry()
        {
            return _engine.GlobalContext.TryGetValue(ChampionSkillStressTelemetry.GlobalKey, out var value) &&
                   value is ChampionSkillStressTelemetry telemetry
                ? telemetry
                : null;
        }

        private RenderDebugState? ResolveRenderDebugState()
        {
            return _engine.GetService(CoreServiceKeys.RenderDebugState);
        }

        private string ResolveActiveSelectionViewId()
        {
            if (_engine.GlobalContext.TryGetValue(ChampionSkillSandboxIds.SelectionViewChoiceKey, out var value) &&
                value is string buttonId &&
                ChampionSkillSandboxIds.IsSelectionViewButton(buttonId))
            {
                return buttonId;
            }

            return ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId;
        }
    }
}
