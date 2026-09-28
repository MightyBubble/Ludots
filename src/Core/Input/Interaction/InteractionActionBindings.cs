using System;

namespace Ludots.Core.Input.Interaction
{
    public sealed class InteractionActionBindings
    {
        private string? _confirmActionId;
        private string? _commandActionId;
        private string? _cancelActionId;

        public string ConfirmActionId
        {
            get => _confirmActionId ?? throw Missing(nameof(ConfirmActionId));
            set => _confirmActionId = value;
        }

        public string CommandActionId
        {
            get => _commandActionId ?? throw Missing(nameof(CommandActionId));
            set => _commandActionId = value;
        }

        public string CancelActionId
        {
            get => _cancelActionId ?? throw Missing(nameof(CancelActionId));
            set => _cancelActionId = value;
        }

        public void Validate()
        {
            RequireActionId(_confirmActionId, nameof(ConfirmActionId));
            RequireActionId(_commandActionId, nameof(CommandActionId));
            RequireActionId(_cancelActionId, nameof(CancelActionId));
        }

        private static void RequireActionId(string? value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Missing(propertyName);
            }
        }

        private static InvalidOperationException Missing(string propertyName)
        {
            return new InvalidOperationException(
                $"game.json interactionActions.{char.ToLowerInvariant(propertyName[0])}{propertyName.Substring(1)} must be explicitly configured.");
        }
    }
}
