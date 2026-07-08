using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.CommandSources
{
    public static class EntityCollectionContextRuntime
    {
        public static bool TryGetCurrentPrimary(World world, Dictionary<string, object> globals, out Entity primary)
        {
            primary = default;
            return TryResolveCurrentCollection(world, globals, out EntityCollectionStore collections, out EntityCollectionHandle handle, out _) &&
                   collections.TryGetEntityAt(handle, 0, out primary);
        }

        public static int GetCurrentCount(World world, Dictionary<string, object> globals)
        {
            return TryResolveCurrentCollection(world, globals, out EntityCollectionStore collections, out EntityCollectionHandle handle, out _)
                ? GetCount(collections, handle)
                : 0;
        }

        public static int CopyCurrent(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            return TryResolveCurrentCollection(world, globals, out EntityCollectionStore collections, out EntityCollectionHandle handle, out _)
                ? collections.CopyEntities(handle, 0, destination)
                : 0;
        }

        public static Entity[] SnapshotCurrent(World world, Dictionary<string, object> globals)
        {
            int count = GetCurrentCount(world, globals);
            if (count <= 0)
            {
                return Array.Empty<Entity>();
            }

            var entities = new Entity[count];
            int written = CopyCurrent(world, globals, entities);
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

        public static bool ContainsCurrent(World world, Dictionary<string, object> globals, Entity entity)
        {
            if (!world.IsAlive(entity) ||
                !TryResolveCurrentCollection(world, globals, out EntityCollectionStore collections, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            Span<Entity> scratch = view.Count <= 128 ? stackalloc Entity[view.Count] : new Entity[view.Count];
            int written = collections.CopyEntities(handle, 0, scratch);
            for (int i = 0; i < written; i++)
            {
                if (scratch[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetHovered(World world, Dictionary<string, object> globals, out Entity hovered)
        {
            hovered = default;
            return TryGetLocalOwner(world, globals, out Entity owner) &&
                   TryGetStore(globals, out EntityCollectionStore collections) &&
                   collections.TryGet(owner, EntityCollectionKeys.HoveredEntity, out EntityCollectionHandle handle) &&
                   collections.TryGetEntityAt(handle, 0, out hovered);
        }

        public static bool TryDescribeCurrentView(
            World world,
            Dictionary<string, object> globals,
            out EntityCollectionView view)
        {
            view = default;
            return TryResolveCurrentCollection(world, globals, out _, out _, out view);
        }

        public static bool TryResolveCurrentCollection(
            World world,
            Dictionary<string, object> globals,
            out EntityCollectionStore collections,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            collections = default!;
            handle = EntityCollectionHandle.Invalid;
            view = default;
            return TryGetStore(globals, out collections) &&
                   TryGetCurrentOwnerAndKey(world, globals, out Entity owner, out int keyId) &&
                   collections.TryGet(owner, keyId, out handle) &&
                   collections.TryGetView(handle, out view);
        }

        public static bool TryGetCurrentOwnerAndKey(
            World world,
            Dictionary<string, object> globals,
            out Entity owner,
            out int keyId)
        {
            owner = default;
            keyId = 0;
            if (!TryGetLocalOwner(world, globals, out owner))
            {
                return false;
            }

            if (TryGetInteractionStack(globals, out InteractionContextStack stack) &&
                stack.TryPeek(out InteractionContextFrame frame) &&
                frame.ActiveCollectionKeyId > 0)
            {
                keyId = frame.ActiveCollectionKeyId;
                return true;
            }

            if (!TryGetStore(globals, out EntityCollectionStore collections))
            {
                return false;
            }

            keyId = collections.KeyRegistry.Register(EntityCollectionKeys.CommandSource);
            return keyId > 0;
        }

        private static int GetCount(EntityCollectionStore collections, EntityCollectionHandle handle)
        {
            return collections.TryGetView(handle, out EntityCollectionView view) ? view.Count : 0;
        }

        private static bool TryGetLocalOwner(World world, Dictionary<string, object> globals, out Entity owner)
        {
            owner = default;
            return globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? ownerObj) &&
                   ownerObj is Entity local &&
                   world.IsAlive(local) &&
                   (owner = local) != Entity.Null;
        }

        private static bool TryGetStore(Dictionary<string, object> globals, out EntityCollectionStore collections)
        {
            collections = default!;
            return globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) &&
                   storeObj is EntityCollectionStore store &&
                   (collections = store) != null;
        }

        private static bool TryGetInteractionStack(Dictionary<string, object> globals, out InteractionContextStack stack)
        {
            stack = default!;
            return globals.TryGetValue(CoreServiceKeys.InteractionContextStack.Name, out object? stackObj) &&
                   stackObj is InteractionContextStack resolved &&
                   (stack = resolved) != null;
        }
    }
}
