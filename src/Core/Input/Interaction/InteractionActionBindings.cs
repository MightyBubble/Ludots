namespace Ludots.Core.Input.Interaction
{
    public sealed class InteractionActionBindings
    {
        public const string DefaultConfirmActionId = "Select";
        public const string DefaultCancelActionId = "Cancel";
        public const string DefaultCommandActionId = "Command";
        public const string DefaultPointerPositionActionId = "PointerPos";

        public string ConfirmActionId { get; set; } = DefaultConfirmActionId;
        public string CancelActionId { get; set; } = DefaultCancelActionId;
        public string CommandActionId { get; set; } = DefaultCommandActionId;
        public string PointerPositionActionId { get; set; } = DefaultPointerPositionActionId;
    }
}
