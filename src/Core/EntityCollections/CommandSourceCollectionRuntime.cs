using System;
using Arch.Core;

namespace Ludots.Core.EntityCollections
{
    public static class CommandSourceCollectionRuntime
    {
        public static EntityCollectionDescriptor CreateDescriptor(
            Entity owner,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind,
            string title,
            string summary)
        {
            if (owner == Entity.Null)
            {
                throw new ArgumentException("Command source owner is required.", nameof(owner));
            }

            return EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                sourceKind,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: entities.Length > 0 ? entities[0] : Entity.Null,
                title: string.IsNullOrWhiteSpace(title) ? "Command source" : title.Trim(),
                summary: string.IsNullOrWhiteSpace(summary) ? $"{entities.Length} command entities" : summary.Trim());
        }

        public static EntityCollectionHandle Replace(
            EntityCollectionStore collections,
            Entity owner,
            ReadOnlySpan<Entity> entities,
            EntityCollectionSourceKind sourceKind,
            string title,
            string summary)
        {
            ArgumentNullException.ThrowIfNull(collections);
            EntityCollectionDescriptor descriptor = CreateDescriptor(owner, entities, sourceKind, title, summary);
            return collections.Replace(owner, descriptor, entities);
        }

        public static bool TryGet(
            EntityCollectionStore collections,
            Entity owner,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            ArgumentNullException.ThrowIfNull(collections);
            handle = EntityCollectionHandle.Invalid;
            view = default;
            if (owner == Entity.Null ||
                !collections.TryGet(owner, EntityCollectionKeys.CommandSource, out handle))
            {
                return false;
            }

            if (!collections.TryGetView(handle, out view))
            {
                handle = EntityCollectionHandle.Invalid;
                return false;
            }

            if (view.Role != EntityCollectionRoleKind.CommandSource)
            {
                throw new InvalidOperationException(
                    $"Entity collection '{EntityCollectionKeys.CommandSource}' for owner {owner.Id}:{owner.WorldId}:{owner.Version} must have role {EntityCollectionRoleKind.CommandSource}; got {view.Role}.");
            }

            return true;
        }

        public static int CopyCommandSources(EntityCollectionStore collections, Entity owner, Span<Entity> destination)
        {
            return TryGet(collections, owner, out EntityCollectionHandle handle, out _)
                ? collections.CopyEntities(handle, 0, destination)
                : 0;
        }

        public static bool TryGetPrimary(EntityCollectionStore collections, Entity owner, out Entity entity)
        {
            entity = Entity.Null;
            return TryGet(collections, owner, out EntityCollectionHandle handle, out _) &&
                   collections.TryGetEntityAt(handle, 0, out entity);
        }
    }
}
