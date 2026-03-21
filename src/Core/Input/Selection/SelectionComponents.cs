using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    public enum SelectionContainerKind : byte
    {
        Live = 0,
        Snapshot = 1,
        Group = 2,
        Formation = 3,
        Derived = 4,
        CommandBinding = 5,
        Debug = 6
    }

    public static class SelectionSetKeys
    {
        public const string Ambient = "selection.live.primary";
        public const string LivePrimary = Ambient;
        public const string FormationPrimary = "selection.formation.primary";
        public const string CommandPreview = "selection.command.preview";
        public const string CommandSnapshot = "selection.command.snapshot";
    }

    public static class SelectionViewKeys
    {
        public const string Primary = "selection.view.primary";
        public const string Secondary = "selection.view.secondary";
        public const string CommandPreview = "selection.view.command-preview";
        public const string Formation = "selection.view.formation";
        public const string Debug = "selection.view.debug";
    }

    public readonly record struct SelectionContainerHandle(Entity ContainerEntity)
    {
        public bool IsValid => ContainerEntity != Entity.Null;

        public static SelectionContainerHandle Invalid { get; } = new(Entity.Null);
    }

    public struct SelectionContainerTag
    {
    }

    public struct SelectionContainerOwner
    {
        public Entity Value;
    }

    public struct SelectionContainerAliasId
    {
        public int Value;
    }

    public struct SelectionContainerKindComponent
    {
        public byte Value;

        public SelectionContainerKind Kind
        {
            readonly get => (SelectionContainerKind)Value;
            set => Value = (byte)value;
        }
    }

    public struct SelectionContainerRevision
    {
        public uint Value;
    }

    public struct SelectionContainerMemberCount
    {
        public int Value;
    }

    public struct SelectionMemberTag
    {
    }

    public struct SelectionMemberContainer
    {
        public Entity Value;
    }

    public struct SelectionMemberTarget
    {
        public Entity Value;
    }

    public struct SelectionMemberOrdinal
    {
        public int Value;
    }

    public struct SelectionMemberRoleId
    {
        public int Value;
    }

    public struct SelectionViewBindingTag
    {
    }

    public struct SelectionViewBindingViewer
    {
        public Entity Value;
    }

    public struct SelectionViewBindingKeyId
    {
        public int Value;
    }

    public struct SelectionViewBindingContainer
    {
        public Entity Value;
    }

    public struct SelectionLeaseOwnerTag
    {
    }

    public struct SelectionLeaseContainer
    {
        public Entity Value;
    }

    public struct SelectionSelectableTag
    {
    }

    public struct SelectionSelectableState
    {
        public byte IsEnabled;

        public readonly bool Enabled => IsEnabled != 0;

        public static SelectionSelectableState EnabledByDefault => new() { IsEnabled = 1 };
        public static SelectionSelectableState Disabled => new() { IsEnabled = 0 };
    }

    public struct SelectionDragState
    {
        public Vector2 StartScreen;
        public Vector2 CurrentScreen;
        public byte IsActive;

        public readonly bool Active => IsActive != 0;

        public void Begin(Vector2 screenPosition)
        {
            StartScreen = screenPosition;
            CurrentScreen = screenPosition;
            IsActive = 1;
        }

        public void Clear()
        {
            StartScreen = default;
            CurrentScreen = default;
            IsActive = 0;
        }

        public readonly bool ExceedsThreshold(float thresholdPixels)
        {
            float dx = CurrentScreen.X - StartScreen.X;
            float dy = CurrentScreen.Y - StartScreen.Y;
            return dx * dx + dy * dy >= thresholdPixels * thresholdPixels;
        }
    }
}
