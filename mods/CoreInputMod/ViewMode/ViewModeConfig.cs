namespace CoreInputMod.ViewMode
{
    public sealed class ViewModeConfig
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string VirtualCameraId { get; set; } = "Default";
        public string InputContextId { get; set; } = "";
        public string InteractionMode { get; set; } = "SmartCast";
        public string SelectionProfileId { get; set; } = "";
        public string[]? SkillBarKeyLabels { get; set; }
        public bool SkillBarEnabled { get; set; } = true;
        public string SwitchActionId { get; set; } = "";
    }
}
