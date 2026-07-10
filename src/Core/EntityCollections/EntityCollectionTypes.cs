using System;
using Arch.Core;

namespace Ludots.Core.EntityCollections
{
    public static class EntityCollectionKeys
    {
        public const string UiCommandAcquisition = "collection.ui.command.acquisition";
        public const string HoveredEntity = "collection.ui.command.hover";
        public const string AbilityAimHover = "collection.ability.aim.hover";
        public const string AbilityAimAffected = "collection.ability.aim.affected";
        public const string EntityInfoExplicit = "collection.entityinfo.explicit";
        public const string CommandSource = "collection.command.source";
        public const string UiCastRaw = "collection.ui.cast.raw";
    }

    public static class EntityViewKeys
    {
        public const string ControlPlaneCommand = "view.control_plane.command";
    }

    public enum EntityCollectionSourceKind : byte
    {
        Explicit = 0,
        UiAcquisition = 1,
        CollectionView = 2,
        CollectionSnapshot = 3,
        RelationDerived = 4,
        SpatialQuery = 5,
        GasGraphResult = 6,
        Debug = 7,
        UiHover = 8,
        DynamicParticipant = 9,
    }

    public enum EntityCollectionRoleKind : byte
    {
        Display = 0,
        AcquisitionPreview = 1,
        CommandPreview = 2,
        CommandSource = 3,
        Debug = 4,
        AimAffected = 5,
    }

    [Flags]
    public enum EntityCollectionRowFlags : byte
    {
        None = 0,
        Primary = 1 << 0,
    }

    public readonly record struct EntityCollectionDescriptor(
        string Key,
        EntityCollectionSourceKind SourceKind,
        EntityCollectionRoleKind Role,
        Entity ContextEntity,
        Entity PrimaryEntity,
        string Title,
        string Summary)
    {
        public static EntityCollectionDescriptor Create(
            string key,
            EntityCollectionSourceKind sourceKind,
            EntityCollectionRoleKind role,
            Entity contextEntity = default,
            Entity primaryEntity = default,
            string title = "",
            string summary = "")
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Entity collection key is required.", nameof(key));
            }

            return new EntityCollectionDescriptor(
                key.Trim(),
                sourceKind,
                role,
                contextEntity,
                primaryEntity,
                title ?? string.Empty,
                summary ?? string.Empty);
        }
    }

    public readonly record struct EntityCollectionHandle(int Slot, uint Revision)
    {
        public bool IsValid => Slot >= 0 && Revision != 0;
        public static EntityCollectionHandle Invalid { get; } = new(-1, 0);
    }

    public readonly record struct EntityCollectionView(
        Entity Owner,
        int KeyId,
        string Key,
        EntityCollectionSourceKind SourceKind,
        EntityCollectionRoleKind Role,
        Entity ContextEntity,
        Entity PrimaryEntity,
        uint Revision,
        ulong Signature,
        int Count,
        string Title,
        string Summary);
}
