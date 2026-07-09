using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.CommandSources
{
    public static class EntityCollectionContextRuntime
    {
        public static bool TryGetPrimary(
            World world,
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey,
            out Entity primary)
        {
            primary = default;
            return world != null &&
                   collections != null &&
                   world.IsAlive(owner) &&
                   TryResolveCollection(collections, owner, collectionKey, out EntityCollectionHandle handle, out _) &&
                   collections.TryGetEntityAt(handle, 0, out primary) &&
                   primary != Entity.Null &&
                   world.IsAlive(primary);
        }

        public static bool TryGetPrimary(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            string collectionKey,
            out Entity primary)
        {
            primary = default;
            return TryGetStore(globals, out EntityCollectionStore collections) &&
                   TryGetPrimary(world, collections, owner, collectionKey, out primary);
        }

        public static int GetCount(EntityCollectionStore collections, Entity owner, string collectionKey)
        {
            return collections != null &&
                   TryResolveCollection(collections, owner, collectionKey, out _, out EntityCollectionView view)
                ? view.Count
                : 0;
        }

        public static int GetCount(Dictionary<string, object> globals, Entity owner, string collectionKey)
        {
            return TryGetStore(globals, out EntityCollectionStore collections)
                ? GetCount(collections, owner, collectionKey)
                : 0;
        }

        public static int Copy(EntityCollectionStore collections, Entity owner, string collectionKey, Span<Entity> destination)
        {
            return collections != null &&
                   TryResolveCollection(collections, owner, collectionKey, out EntityCollectionHandle handle, out _)
                ? collections.CopyEntities(handle, 0, destination)
                : 0;
        }

        public static int Copy(Dictionary<string, object> globals, Entity owner, string collectionKey, Span<Entity> destination)
        {
            return TryGetStore(globals, out EntityCollectionStore collections)
                ? Copy(collections, owner, collectionKey, destination)
                : 0;
        }

        public static Entity[] Snapshot(EntityCollectionStore collections, Entity owner, string collectionKey)
        {
            int count = GetCount(collections, owner, collectionKey);
            if (count <= 0)
            {
                return Array.Empty<Entity>();
            }

            var entities = new Entity[count];
            int written = Copy(collections, owner, collectionKey, entities);
            if (written <= 0)
            {
                return Array.Empty<Entity>();
            }

            if (written != entities.Length)
            {
                Array.Resize(ref entities, written);
            }

            return entities;
        }

        public static Entity[] Snapshot(Dictionary<string, object> globals, Entity owner, string collectionKey)
        {
            return TryGetStore(globals, out EntityCollectionStore collections)
                ? Snapshot(collections, owner, collectionKey)
                : Array.Empty<Entity>();
        }

        public static bool Contains(
            World world,
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey,
            Entity entity)
        {
            if (world == null ||
                collections == null ||
                entity == Entity.Null ||
                !world.IsAlive(entity) ||
                !TryResolveCollection(collections, owner, collectionKey, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            for (int i = 0; i < view.Count; i++)
            {
                if (collections.TryGetEntityAt(handle, i, out Entity current) &&
                    current == entity)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Contains(
            World world,
            Dictionary<string, object> globals,
            Entity owner,
            string collectionKey,
            Entity entity)
        {
            return TryGetStore(globals, out EntityCollectionStore collections) &&
                   Contains(world, collections, owner, collectionKey, entity);
        }

        public static bool TryGetHovered(
            World world,
            EntityCollectionStore collections,
            Entity owner,
            out Entity hovered)
        {
            return TryGetPrimary(world, collections, owner, EntityCollectionKeys.HoveredEntity, out hovered);
        }

        public static bool TryDescribeView(
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey,
            out EntityCollectionView view)
        {
            return TryResolveCollection(collections, owner, collectionKey, out _, out view);
        }

        public static bool TryResolveCollection(
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            handle = EntityCollectionHandle.Invalid;
            view = default;
            return collections != null &&
                   owner != Entity.Null &&
                   !string.IsNullOrWhiteSpace(collectionKey) &&
                   collections.TryGet(owner, collectionKey, out handle) &&
                   collections.TryGetView(handle, out view);
        }

        private static bool TryGetStore(Dictionary<string, object> globals, out EntityCollectionStore collections)
        {
            collections = default!;
            return globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) &&
                   storeObj is EntityCollectionStore store &&
                   (collections = store) != null;
        }

    }
}
