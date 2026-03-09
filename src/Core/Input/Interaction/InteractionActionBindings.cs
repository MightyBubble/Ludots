namespace Ludots.Core.Input.Interaction
{
    public sealed class InteractionActionBindings
    {
        public const string DefaultConfirmActionId = "Select";
        public const string DefaultCancelActionId = "Cancel";
        public const string DefaultCommandActionId = "Command";
        public const string DefaultPointerPositionActionId = "PointerPos";
        public const string DefaultResponseChainPassActionId = "ResponseChainPass";
        public const string DefaultResponseChainNegateActionId = "ResponseChainNegate";
        public const string DefaultResponseChainActivateActionId = "ResponseChainActivate";

        public string ConfirmActionId { get; set; } = DefaultConfirmActionId;
        public string CancelActionId { get; set; } = DefaultCancelActionId;
        public string CommandActionId { get; set; } = DefaultCommandActionId;
        public string PointerPositionActionId { get; set; } = DefaultPointerPositionActionId;
        public string ResponseChainPassActionId { get; set; } = DefaultResponseChainPassActionId;
        public string ResponseChainNegateActionId { get; set; } = DefaultResponseChainNegateActionId;
        public string ResponseChainActivateActionId { get; set; } = DefaultResponseChainActivateActionId;
    }
}
