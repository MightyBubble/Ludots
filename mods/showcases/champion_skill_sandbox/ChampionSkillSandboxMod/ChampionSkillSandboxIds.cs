using System;

namespace ChampionSkillSandboxMod
{
    internal static class ChampionSkillSandboxIds
    {
        public const string MapId = "champion_skill_sandbox";
        public const string StressMapId = "champion_skill_stress";
        public const string InputContextId = "ChampionSkillSandbox.Controls";

        public const string SmartCastModeId = "ChampionSkillSandbox.Mode.SmartCast";
        public const string IndicatorModeId = "ChampionSkillSandbox.Mode.Indicator";
        public const string PressReleaseModeId = "ChampionSkillSandbox.Mode.PressReleaseAim";
        public const string TacticalCameraId = "ChampionSkillSandbox.Camera.Tactical";

        public const string SmartCastActionId = "CastModeSmart";
        public const string IndicatorActionId = "CastModeIndicator";
        public const string PressReleaseActionId = "CastModePressRelease";
        public const string ResetCameraActionId = "ResetCamera";
        public const string FreeCameraToolbarButtonId = "ChampionSkillSandbox.Camera.Free";
        public const string FollowSelectionToolbarButtonId = "ChampionSkillSandbox.Camera.Selection";
        public const string FollowSelectionGroupToolbarButtonId = "ChampionSkillSandbox.Camera.SelectionGroup";
        public const string ResetCameraToolbarButtonId = "ChampionSkillSandbox.Camera.Reset";
        public const string StressTeamADecreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamA.Decrease";
        public const string StressTeamAIncreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamA.Increase";
        public const string StressTeamBDecreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamB.Decrease";
        public const string StressTeamBIncreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamB.Increase";
        public const string StressHudBarToggleToolbarButtonId = "ChampionSkillSandbox.Stress.HudBar.Toggle";
        public const string StressHudTextToggleToolbarButtonId = "ChampionSkillSandbox.Stress.HudText.Toggle";
        public const string StressCombatTextToggleToolbarButtonId = "ChampionSkillSandbox.Stress.CombatText.Toggle";
        public const string PlayerSelectionToolbarButtonId = "ChampionSkillSandbox.Selection.Player.Live";
        public const string PlayerFormationToolbarButtonId = "ChampionSkillSandbox.Selection.Player.Formation";
        public const string AiTargetToolbarButtonId = "ChampionSkillSandbox.Selection.AI.Targets";
        public const string AiFormationToolbarButtonId = "ChampionSkillSandbox.Selection.AI.Formation";
        public const string CommandSnapshotToolbarButtonId = "ChampionSkillSandbox.Selection.Command.Snapshot";
        public const string ResetCameraRequestKey = "ChampionSkillSandbox.Camera.ResetRequested";
        public const string CameraFollowModeKey = "ChampionSkillSandbox.Camera.FollowMode";
        public const string SelectionViewChoiceKey = "ChampionSkillSandbox.Selection.ViewChoice";
        public const string SelectionIndicatorPerformerKey = "champion_skill_sandbox.selection_indicator";
        public const string HoverIndicatorPerformerKey = "champion_skill_sandbox.hover_indicator";
        public const int SelectionIndicatorScopeId = 4101;
        public const int HoverIndicatorScopeId = 4102;
        public const int AimHoverIndicatorScopeId = 4103;

        public const string EzrealAlphaName = "Ezreal Alpha";
        public const string EzrealCooldownName = "Ezreal Cooldown";
        public const string GarenAlphaName = "Garen Alpha";
        public const string GarenCourageName = "Garen Courage";
        public const string JayceCannonName = "Jayce Cannon";
        public const string JayceHammerName = "Jayce Hammer";
        public const string GeomancerAlphaName = "Geomancer Alpha";
        public const string SpellEngineerAlphaName = "Spell Engineer Alpha";
        public const string KineticVanguardAlphaName = "Kinetic Vanguard Alpha";
        public const string RunicBeaconName = "Runic Beacon";
        public const string RuneFieldName = "Rune Field";
        public const string StonePillarName = "Stone Pillar";
        public const string SpellBeaconName = "Spell Beacon";
        public const string GravityWellName = "Gravity Well";
        public const string BarrierSegmentName = "Barrier Segment";
        public const string GuidedLaserName = "Guided Laser";
        public const string TargetDummyDName = "Target Dummy D";
        public const string TargetDummyEName = "Target Dummy E";
        public const string TargetDummyFName = "Target Dummy F";
        public const string TargetDummyGName = "Target Dummy G";
        public const string TargetDummyHName = "Target Dummy H";
        public const string TargetBruteCName = "Target Brute C";

        public const string GarenCourageTag = "State.Champion.Garen.Courage";
        public const string JayceHammerTag = "State.Champion.Jayce.Hammer";
        public const string EzrealBlockedTag = "Cooldown.Champion.Ezreal.R";

        public static bool IsSandboxMap(string? mapId)
        {
            return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapId, StressMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsStressMap(string? mapId)
        {
            return string.Equals(mapId, StressMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSandboxMode(string? modeId)
        {
            return string.Equals(modeId, SmartCastModeId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(modeId, IndicatorModeId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(modeId, PressReleaseModeId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCameraFollowMode(string? buttonId)
        {
            return string.Equals(buttonId, FreeCameraToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, FollowSelectionToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, FollowSelectionGroupToolbarButtonId, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSelectionViewButton(string? buttonId)
        {
            return string.Equals(buttonId, PlayerSelectionToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, PlayerFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, AiTargetToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, AiFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonId, CommandSnapshotToolbarButtonId, StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveSelectionViewLabel(string? buttonId)
        {
            if (string.Equals(buttonId, PlayerFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase))
            {
                return "P1 Formation";
            }

            if (string.Equals(buttonId, AiTargetToolbarButtonId, StringComparison.OrdinalIgnoreCase))
            {
                return "AI Targets";
            }

            if (string.Equals(buttonId, AiFormationToolbarButtonId, StringComparison.OrdinalIgnoreCase))
            {
                return "AI Formation";
            }

            if (string.Equals(buttonId, CommandSnapshotToolbarButtonId, StringComparison.OrdinalIgnoreCase))
            {
                return "Command Snapshot";
            }

            return "P1 Live";
        }
    }
}
