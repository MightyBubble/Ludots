using System;
using System.Collections.Generic;
using Arch.Core;
using EntityCommandPanelMod.UI;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillCastModeToolbarProvider : IEntityCommandPanelToolbarProvider
    {
        private GameEngine? _engine;
        private ChampionSkillCastModeCatalog? _castModes;

        public bool IsVisible => ChampionSkillSandboxIds.IsSandboxMap(_engine?.CurrentMapSession?.MapId.Value);

        public uint Revision
        {
            get
            {
                uint revision = IsVisible ? 1u : 0u;
                int activeModeIndex = ResolveActiveModeIndex();
                string activeFollowModeId = ResolveActiveCameraFollowMode();
                if (activeModeIndex >= 0)
                {
                    revision ^= (uint)(activeModeIndex + 1);
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

        public string Title => ChampionSkillSandboxIds.IsStressMap(_engine?.CurrentMapSession?.MapId.Value)
            ? "Stress Harness"
            : "Cast + Showcase";

        public string Subtitle
        {
            get
            {
                if (!ChampionSkillSandboxIds.IsStressMap(_engine?.CurrentMapSession?.MapId.Value))
                {
                    return $"Theme {EntityCommandPanelShowcaseTheme.ResolveLabel(ResolveActiveShowcaseThemeId())} | Cast + Camera | F1/F2/F3 Cast | RMB Move";
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
            string? activeModeId = ResolveActiveModeId();
            string activeFollowModeId = ResolveActiveCameraFollowMode();
            string activeSelectionViewId = ResolveActiveSelectionViewId();
            string activeShowcaseThemeId = ResolveActiveShowcaseThemeId();
            RenderDebugState? renderDebug = ResolveRenderDebugState();
            ChampionSkillCastModeCatalog.Mode[] modes = RequireCastModes().Modes;
            int tailAt = modes.Length + 4;
            var buttons = new EntityCommandPanelToolbarButtonView[tailAt + (isStressMap ? 12 : 3)];
            for (int i = 0; i < modes.Length; i++)
            {
                buttons[i] = new EntityCommandPanelToolbarButtonView(
                    modes[i].ButtonId,
                    modes[i].Label,
                    string.Equals(activeModeId, modes[i].ButtonId, StringComparison.Ordinal),
                    modes[i].Accent);
            }

            buttons[modes.Length] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FreeCameraToolbarButtonId,
                "Free",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FreeCameraToolbarButtonId, StringComparison.Ordinal),
                "#D7D2C4");
            buttons[modes.Length + 1] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionToolbarButtonId,
                "Follow",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionToolbarButtonId, StringComparison.Ordinal),
                "#8ED9A9");
            buttons[modes.Length + 2] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId,
                "Group",
                string.Equals(activeFollowModeId, ChampionSkillSandboxIds.FollowSelectionGroupToolbarButtonId, StringComparison.Ordinal),
                "#F0C35A");
            buttons[modes.Length + 3] = new EntityCommandPanelToolbarButtonView(
                ChampionSkillSandboxIds.ResetCameraToolbarButtonId,
                "Reset",
                false,
                "#D7D2C4");
            buttons[tailAt] = new EntityCommandPanelToolbarButtonView(
                EntityCommandPanelShowcaseTheme.Dota2Id,
                "Dota2",
                string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal),
                "#D37A4B");
            buttons[tailAt + 1] = new EntityCommandPanelToolbarButtonView(
                EntityCommandPanelShowcaseTheme.LolId,
                "LoL",
                string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.LolId, StringComparison.Ordinal),
                "#D5B25B");
            buttons[tailAt + 2] = new EntityCommandPanelToolbarButtonView(
                EntityCommandPanelShowcaseTheme.Sc2Id,
                "SC2",
                string.Equals(activeShowcaseThemeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal),
                "#59B7FF");
            if (isStressMap)
            {
                buttons[tailAt] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId,
                    "A-",
                    false,
                    "#FF9B7A");
                buttons[tailAt + 1] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId,
                    "A+",
                    false,
                    "#FF9B7A");
                buttons[tailAt + 2] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId,
                    "B-",
                    false,
                    "#67D4FF");
                buttons[tailAt + 3] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId,
                    "B+",
                    false,
                    "#67D4FF");
                buttons[tailAt + 4] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId,
                    "Bar",
                    renderDebug?.DrawWorldHudBars ?? true,
                    "#E2D7A6");
                buttons[tailAt + 5] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId,
                    "Text",
                    renderDebug?.DrawWorldHudText ?? true,
                    "#F2E3B3");
                buttons[tailAt + 6] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId,
                    "Float",
                    renderDebug?.DrawCombatText ?? true,
                    "#FFCF86");
                buttons[tailAt + 7] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId,
                    "P1",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerSelectionToolbarButtonId, StringComparison.Ordinal),
                    "#98E7A7");
                buttons[tailAt + 8] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.PlayerFormationToolbarButtonId,
                    "P1F",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.PlayerFormationToolbarButtonId, StringComparison.Ordinal),
                    "#DAE89B");
                buttons[tailAt + 9] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiTargetToolbarButtonId,
                    "AI",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiTargetToolbarButtonId, StringComparison.Ordinal),
                    "#FFAE86");
                buttons[tailAt + 10] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.AiFormationToolbarButtonId,
                    "AIF",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.AiFormationToolbarButtonId, StringComparison.Ordinal),
                    "#F6CF79");
                buttons[tailAt + 11] = new EntityCommandPanelToolbarButtonView(
                    ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId,
                    "CMD",
                    string.Equals(activeSelectionViewId, ChampionSkillSandboxIds.CommandSnapshotToolbarButtonId, StringComparison.Ordinal),
                    "#7FD8F2");
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

            if (string.Equals(buttonId, ChampionSkillSandboxIds.ResetCameraToolbarButtonId, StringComparison.Ordinal))
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
                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamADecreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamA(-ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamAIncreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamA(ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBDecreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamB(-ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressTeamBIncreaseToolbarButtonId, StringComparison.Ordinal))
                {
                    control.AdjustTeamB(ChampionSkillStressControlState.Step);
                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudBarToggleToolbarButtonId, StringComparison.Ordinal))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudBars = !renderDebug.DrawWorldHudBars;
                    }

                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressHudTextToggleToolbarButtonId, StringComparison.Ordinal))
                {
                    if (renderDebug != null)
                    {
                        renderDebug.DrawWorldHudText = !renderDebug.DrawWorldHudText;
                    }

                    return;
                }

                if (string.Equals(buttonId, ChampionSkillSandboxIds.StressCombatTextToggleToolbarButtonId, StringComparison.Ordinal))
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

            if (_engine != null && RequireCastModes().TryGetByButtonId(buttonId, out ChampionSkillCastModeCatalog.Mode mode))
            {
                ResolveCastModeHandler(mode.ActionId).InjectButtonPress(mode.ActionId);
            }
        }

        // Sole-seat clients sample the global handler; extra seats sample their own channel.
        // TriggerGraphActionBindingSystem uses the same split, so the set_cast_mode graphs see this edge.
        private PlayerInputHandler ResolveCastModeHandler(string actionId)
        {
            if (_engine == null)
            {
                throw new InvalidOperationException("ChampionSkillSandbox cast mode toolbar is not bound.");
            }

            ClientLocalSeatRegistry seats = _engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException(
                    "ChampionSkillSandbox cast mode toolbar requires ClientLocalSeatRegistry.");
            PlayerInputHandler? handler = null;
            if (seats.Count > 1)
            {
                ClientLocalSeatInputRuntime seatInput = _engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                    ?? throw new InvalidOperationException(
                        "ChampionSkillSandbox cast mode toolbar requires ClientLocalSeatInputRuntime.");
                IReadOnlyList<string> seatIds = seats.SeatIds;
                for (int i = 0; i < seatIds.Count; i++)
                {
                    ClientLocalSeat seat = seats.Require(seatIds[i]);
                    if (!seat.HasPossession)
                    {
                        continue;
                    }

                    if (!seatInput.TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel))
                    {
                        throw new InvalidOperationException(
                            $"ChampionSkillSandbox cast mode action '{actionId}' cannot attribute to seat '{seat.SeatId}'.");
                    }

                    handler = channel.Handler;
                    break;
                }
            }
            else
            {
                handler = _engine.GetService(CoreServiceKeys.InputHandler);
            }

            if (handler == null)
            {
                throw new InvalidOperationException(
                    $"ChampionSkillSandbox cast mode action '{actionId}' has no input handler.");
            }

            if (!handler.HasAction(actionId))
            {
                throw new InvalidOperationException(
                    $"ChampionSkillSandbox cast mode action '{actionId}' is not a declared input action.");
            }

            return handler;
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

        private ChampionSkillCastModeCatalog RequireCastModes()
        {
            if (_engine == null)
            {
                throw new InvalidOperationException("ChampionSkillSandbox cast mode toolbar is not bound.");
            }

            return _castModes ??= ChampionSkillCastModeCatalog.Load(_engine);
        }

        private string? ResolveActiveModeId()
        {
            return _engine == null ? null : RequireCastModes().ResolveActiveButtonId(_engine);
        }

        private int ResolveActiveModeIndex()
        {
            return _engine == null ? -1 : RequireCastModes().IndexOfButton(ResolveActiveModeId());
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
    }
}
