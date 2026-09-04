using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.EntityCollections
{
    /// <summary>How a WriteCollection op combines the incoming entity set with the current members.</summary>
    public enum CollectionWriteOp : byte
    {
        Replace = 0,
        Add = 1,
        Subtract = 2,
    }

    /// <summary>
    /// Graph-side collection write primitive: applies replace/add/subtract semantics to one
    /// owned collection in the store. The graph decides owner, key, op, and entities; this
    /// helper only executes the set math. Membership change events fire from the store's
    /// presentation diff exactly as for any other writer.
    /// </summary>
    public static class CollectionWrite
    {
        private static readonly List<Entity> MergeScratch = new(capacity: 64);

        public static void Apply(
            EntityCollectionStore store,
            Entity owner,
            int collectionKeyId,
            CollectionWriteOp op,
            ReadOnlySpan<Entity> entities)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (owner == Entity.Null || owner == default)
            {
                throw new InvalidOperationException(
                    "COLLECTION.WRITE.OwnerMissing: WriteCollection requires a live owner entity (the writing rep).");
            }

            if (string.IsNullOrEmpty(store.KeyRegistry.GetName(collectionKeyId)))
            {
                throw new InvalidOperationException(
                    $"COLLECTION.WRITE.KeyUnknown: collection key id {collectionKeyId} is not registered in the EntityCollectionStore key space.");
            }

            switch (op)
            {
                case CollectionWriteOp.Replace:
                    Write(store, owner, collectionKeyId, entities);
                    return;
                case CollectionWriteOp.Add:
                case CollectionWriteOp.Subtract:
                    MergeScratch.Clear();
                    if (store.TryGet(owner, collectionKeyId, out EntityCollectionHandle handle) && handle.IsValid &&
                        store.TryGetView(handle, out EntityCollectionView view))
                    {
                        if (view.Count > _currentScratch.Length)
                        {
                            _currentScratch = new Entity[view.Count * 2];
                        }

                        int currentCount = store.CopyEntities(owner, collectionKeyId, _currentScratch);
                        if (op == CollectionWriteOp.Add)
                        {
                            AppendDistinct(MergeScratch, _currentScratch, currentCount);
                            AppendDistinct(MergeScratch, entities);
                        }
                        else
                        {
                            KeepNotInIncoming(MergeScratch, _currentScratch, currentCount, entities);
                        }
                    }
                    else if (op == CollectionWriteOp.Add)
                    {
                        AppendDistinct(MergeScratch, entities);
                    }

                    Write(store, owner, collectionKeyId, MergeScratch.ToArray());
                    return;
                default:
                    throw new InvalidOperationException(
                        $"COLLECTION.WRITE.OpInvalid: op {(int)op}; expected replace(0)/add(1)/subtract(2).");
            }
        }

        private static Entity[] _currentScratch = new Entity[64];

        private static void Write(
            EntityCollectionStore store,
            Entity owner,
            int collectionKeyId,
            ReadOnlySpan<Entity> entities)
        {
            string keyName = store.KeyRegistry.GetName(collectionKeyId)
                ?? throw new InvalidOperationException(
                    $"COLLECTION.WRITE.KeyUnknown: collection key id {collectionKeyId} is not registered.");
            var descriptor = EntityCollectionDescriptor.Create(
                keyName,
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.CommandSource);
            store.Replace(owner, collectionKeyId, in descriptor, entities, owner);
        }

        private static void AppendDistinct(List<Entity> target, ReadOnlySpan<Entity> source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (!target.Contains(source[i]))
                {
                    target.Add(source[i]);
                }
            }
        }

        private static void AppendDistinct(List<Entity> target, Entity[] source, int count)
        {
            AppendDistinct(target, source.AsSpan(0, count));
        }

        private static void KeepNotInIncoming(List<Entity> target, Entity[] current, int count, ReadOnlySpan<Entity> incoming)
        {
            for (int i = 0; i < count; i++)
            {
                Entity entity = current[i];
                bool removed = false;
                for (int r = 0; r < incoming.Length; r++)
                {
                    if (incoming[r] == entity)
                    {
                        removed = true;
                        break;
                    }
                }

                if (!removed)
                {
                    target.Add(entity);
                }
            }
        }
    }
}
