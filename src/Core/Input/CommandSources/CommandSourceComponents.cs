using System.Numerics;

namespace Ludots.Core.Input.CommandSources
{
    public sealed class CommandSourceAcquisitionConfig
    {
        public int MutationApplyBudgetPerFrame { get; set; } = 4096;
        public float ClickPickRadiusPixels { get; set; } = 20f;
        public float DragThresholdPixels { get; set; } = 8f;
        public CommandSourceTargetFilterConfig? TargetFilter { get; set; }
        public string[] MovePathPreviewOrderTypeKeys { get; set; } = System.Array.Empty<string>();
        public CommandSourceAcquisitionCollectionConfig Acquisition { get; set; } = new();
    }

    public sealed class CommandSourceAcquisitionCollectionConfig
    {
        public string CollectionKey { get; set; } = Ludots.Core.EntityCollections.EntityCollectionKeys.UiCommandAcquisition;
        public string Title { get; set; } = "Command acquisition";
    }

    public sealed class CommandSourceTargetFilterConfig
    {
        public string? RelationFilter { get; set; }

        public Ludots.Core.Gameplay.Teams.RelationshipFilter ParseRelationFilter()
        {
            return Ludots.Core.Gameplay.Teams.RelationshipFilterUtil.Parse(RelationFilter ?? string.Empty);
        }
    }

    public struct CommandSourceSelectableTag
    {
    }

    public struct CommandSourceSelectableState
    {
        public byte IsEnabled;

        public readonly bool Enabled => IsEnabled != 0;

        public static CommandSourceSelectableState EnabledByDefault => new() { IsEnabled = 1 };
        public static CommandSourceSelectableState Disabled => new() { IsEnabled = 0 };
    }

    public struct CommandSourceDragState
    {
        public Vector2 StartScreen;
        public Vector2 CurrentScreen;
        public byte IsActive;
        public byte AcquisitionModeValue;

        public readonly bool Active => IsActive != 0;

        public CommandSourceAcquisitionMode AcquisitionMode
        {
            readonly get => (CommandSourceAcquisitionMode)AcquisitionModeValue;
            set => AcquisitionModeValue = (byte)value;
        }

        public void Begin(Vector2 screenPosition, CommandSourceAcquisitionMode acquisitionMode)
        {
            StartScreen = screenPosition;
            CurrentScreen = screenPosition;
            AcquisitionMode = acquisitionMode;
            IsActive = 1;
        }

        public void Clear()
        {
            StartScreen = default;
            CurrentScreen = default;
            AcquisitionModeValue = 0;
            IsActive = 0;
        }

        public readonly bool ExceedsThreshold(float thresholdPixels)
        {
            float dx = CurrentScreen.X - StartScreen.X;
            float dy = CurrentScreen.Y - StartScreen.Y;
            return dx * dx + dy * dy >= thresholdPixels * thresholdPixels;
        }
    }

    public enum CommandSourceAcquisitionMode : byte
    {
        Replace = 0,
        Additive = 1,
        Toggle = 2,
    }

    public static class CommandSourceModifierActionIds
    {
        public const string Additive = "QueueModifier";
        public const string Toggle = "PrecisionModifier";
    }
}
