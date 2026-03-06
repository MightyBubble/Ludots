using Ludots.Core.Input.Orders;

namespace CoreInputMod.ViewMode
{
    /// <summary>
    /// Declarative view mode definition. Loaded from mod assets (viewmodes.json).
    /// Combines camera preset, input context, interaction mode, and UI config
    /// into a single switchable unit. Data stays the same — only the interaction changes.
    /// </summary>
    public sealed class ViewModeConfig
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string CameraPresetId { get; set; } = "Default";
        public string FollowTargetKind { get; set; } = "None";
        public string InputContextId { get; set; } = "";
        public string InteractionMode { get; set; } = "SmartCast";
        public string[]? SkillBarKeyLabels { get; set; }
        public bool SkillBarEnabled { get; set; } = true;
        public string SwitchActionId { get; set; } = "";
    }

    public enum FollowTargetKind
    {
        None,
        LocalPlayer,
        SelectedEntity,
        SelectedOrLocalPlayer
    }

    public static class FollowTargetKindParser
    {
        public static FollowTargetKind Parse(string? value)
        {
            if (string.IsNullOrEmpty(value)) return FollowTargetKind.None;
            return value switch
            {
                "None" => FollowTargetKind.None,
                "LocalPlayer" => FollowTargetKind.LocalPlayer,
                "SelectedEntity" => FollowTargetKind.SelectedEntity,
                "SelectedOrLocalPlayer" => FollowTargetKind.SelectedOrLocalPlayer,
                _ => FollowTargetKind.None
            };
        }
    }
}
