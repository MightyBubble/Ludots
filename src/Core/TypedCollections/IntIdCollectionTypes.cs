using System;
using Arch.Core;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.TypedCollections
{
    // Reuses entity-collection source/role kinds so panel bind shares one vocabulary; bags hold template/slot/tag/progression/item-definition ids, not entities.
    public readonly record struct IntIdCollectionDescriptor(
        string Key,
        EntityCollectionSourceKind SourceKind,
        EntityCollectionRoleKind Role,
        string Title,
        string Summary)
    {
        public static IntIdCollectionDescriptor Create(
            string key,
            EntityCollectionSourceKind sourceKind,
            EntityCollectionRoleKind role,
            string title = "",
            string summary = "")
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Int-id collection key is required.", nameof(key));
            }

            return new IntIdCollectionDescriptor(
                key.Trim(),
                sourceKind,
                role,
                title ?? string.Empty,
                summary ?? string.Empty);
        }
    }

    public readonly record struct IntIdCollectionHandle(int Slot, uint Revision)
    {
        public bool IsValid => Slot >= 0 && Revision != 0;
        public static IntIdCollectionHandle Invalid { get; } = new(-1, 0);
    }

    public readonly record struct IntIdCollectionView(
        Entity Owner,
        int KeyId,
        string Key,
        EntityCollectionSourceKind SourceKind,
        EntityCollectionRoleKind Role,
        uint Revision,
        ulong Signature,
        int Count,
        string Title,
        string Summary);
}
