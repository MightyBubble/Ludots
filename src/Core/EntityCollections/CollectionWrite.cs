using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        internal sealed class Scratch
        {
            public readonly List<Entity> Members = new(256);
            public readonly HashSet<Entity> Membership = new(256);
            public Entity[] Current = new Entity[256];
        }

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
                    Scratch scratch = store.WriteScratch;
                    scratch.Members.Clear();
                    scratch.Membership.Clear();
                    if (op == CollectionWriteOp.Subtract)
                        foreach (Entity entity in entities) scratch.Membership.Add(entity);
                    if (store.TryGet(owner, collectionKeyId, out EntityCollectionHandle handle) &&
                        store.TryGetView(handle, out EntityCollectionView view))
                    {
                        if (view.Count > scratch.Current.Length)
                            Array.Resize(ref scratch.Current, checked(view.Count * 2));
                        int currentCount = store.CopyEntities(handle, 0, scratch.Current);
                        foreach (Entity entity in scratch.Current.AsSpan(0, currentCount))
                        {
                            if (op == CollectionWriteOp.Add
                                ? scratch.Membership.Add(entity)
                                : !scratch.Membership.Contains(entity))
                                scratch.Members.Add(entity);
                        }
                    }
                    if (op == CollectionWriteOp.Add)
                        foreach (Entity entity in entities)
                            if (scratch.Membership.Add(entity)) scratch.Members.Add(entity);
                    Write(store, owner, collectionKeyId, CollectionsMarshal.AsSpan(scratch.Members));
                    return;
                default:
                    throw new InvalidOperationException(
                        $"COLLECTION.WRITE.OpInvalid: op {(int)op}; expected replace(0)/add(1)/subtract(2).");
            }
        }


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

    }
}
