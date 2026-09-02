using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Downstream collection writer for the graph pass-through contract (#1398 S2b gap 9,
    /// Case E 06 B-side). Upstream graphs compute a final entity set and dispatch it under an
    /// authored event key via the <c>DispatchCollectionEvent</c> op (A-side); this writer is
    /// the separate consumer registered per event key — the two sides connect only through
    /// the event key, so a different acquisition shape (box vs lasso) reuses the same writer.
    /// Received batches write <see cref="EntityCollectionStore"/> at
    /// (owner = event source rep, collection key) with set semantics: replace (=), add (∪),
    /// subtract (−). The op kind is computed in-graph from the modifier semantic actions
    /// (no modifier → replace), keeping the engine free of modifier enums.
    /// <para>
    /// Deliberately parallel to <see cref="ContextBoundCollectionWriter"/> rather than a
    /// generalization of it: the cast writer's unit of work is a raw acquisition filtered and
    /// routed through the anchor's mounted context (its inputs are context-resolved), while
    /// this writer's unit of work is an already-final set carried by an event payload —
    /// folding the two would bloat the cast kernel with event-transport concerns.
    /// </para>
    /// </summary>
    public sealed class EventKeyedCollectionWriter
    {
        private readonly EntityCollectionStore _store;
        private readonly Dictionary<EventKey, bool> _registered = new();
        private readonly List<Entity> _mergeScratch = new(capacity: 64);
        private Entity[] _currentScratch = new Entity[64];

        public EventKeyedCollectionWriter(EntityCollectionStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Event keys this writer consumes; registration is the authoring surface.</summary>
        public IReadOnlyCollection<EventKey> RegisteredEventKeys => _registered.Keys;

        /// <summary>Register one consumed event key (duplicate registrations are idempotent).</summary>
        public void Register(string eventKey)
        {
            if (string.IsNullOrWhiteSpace(eventKey))
            {
                throw new ArgumentException("Event key is required.", nameof(eventKey));
            }

            _registered[new EventKey(eventKey.Trim())] = true;
        }

        /// <summary>
        /// TriggerManager event handler entry: consumes only its registered keys, reads the
        /// reserved <c>MapTrigger.Collection*</c> payload keys, and applies the set semantics.
        /// Unknown collection key ids and payloads missing a live source entity fail fast.
        /// </summary>
        public Task HandleEvent(EventKey eventKey, ScriptContext context)
        {
            if (context == null || !_registered.ContainsKey(eventKey))
            {
                return Task.CompletedTask;
            }

            Entity owner = ResolveOwner(context);
            int collectionKeyId = ResolveCollectionKeyId(context);
            Entity[] entitySet = context.Get<object>(MapTriggerEventPayloadKeys.CollectionEntitySet) as Entity[]
                ?? throw new InvalidOperationException(
                    $"EVENT.COLLECTION.EntitySetMissing: event '{eventKey.Value}' carries no {MapTriggerEventPayloadKeys.CollectionEntitySet} payload.");
            int entityCount = ResolveEntityCount(context, entitySet.Length);
            int opKind = context.Get<int>(MapTriggerEventPayloadKeys.CollectionOp);

            switch (opKind)
            {
                case (int)EventCollectionWriteOp.Replace:
                    Write(owner, collectionKeyId, entitySet.AsSpan(0, entityCount));
                    break;
                case (int)EventCollectionWriteOp.Add:
                case (int)EventCollectionWriteOp.Subtract:
                    Write(
                        owner,
                        collectionKeyId,
                        MergeWithCurrent(
                            owner,
                            collectionKeyId,
                            entitySet.AsSpan(0, entityCount),
                            adding: opKind == (int)EventCollectionWriteOp.Add));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"EVENT.COLLECTION.OpInvalid: event '{eventKey.Value}' carries collection op {opKind}; expected replace(0)/add(1)/subtract(2).");
            }

            return Task.CompletedTask;
        }

        private static int ResolveEntityCount(ScriptContext context, int arrayLength)
        {
            if (!context.Contains(MapTriggerEventPayloadKeys.CollectionEntityCount))
            {
                return arrayLength;
            }

            int count = context.Get<int>(MapTriggerEventPayloadKeys.CollectionEntityCount);
            if (count < 0 || count > arrayLength)
            {
                throw new InvalidOperationException(
                    $"EVENT.COLLECTION.EntityCountInvalid: count {count} is outside CollectionEntitySet length {arrayLength}.");
            }

            return count;
        }

        private Entity ResolveOwner(ScriptContext context)
        {
            if (!context.Contains(MapTriggerEventPayloadKeys.SourceEntity))
            {
                throw new InvalidOperationException(
                    $"EVENT.COLLECTION.SourceMissing: collection pass-through events require {MapTriggerEventPayloadKeys.SourceEntity}; the writing rep is the collection owner.");
            }

            Entity owner = context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity);
            if (owner == Entity.Null || owner == default)
            {
                throw new InvalidOperationException(
                    "EVENT.COLLECTION.SourceMissing: collection pass-through events require a live source entity as the collection owner.");
            }

            return owner;
        }

        private int ResolveCollectionKeyId(ScriptContext context)
        {
            int collectionKeyId = context.Get<int>(MapTriggerEventPayloadKeys.CollectionKey);
            if (collectionKeyId <= 0 || string.IsNullOrEmpty(_store.KeyRegistry.GetName(collectionKeyId)))
            {
                throw new InvalidOperationException(
                    $"EVENT.COLLECTION.KeyUnknown: collection key id {collectionKeyId} is not registered in the EntityCollectionStore key space.");
            }

            return collectionKeyId;
        }

        private Entity[] _mergeResultScratch = Array.Empty<Entity>();

        private ReadOnlySpan<Entity> MergeWithCurrent(Entity owner, int collectionKeyId, ReadOnlySpan<Entity> entitySet, bool adding)
        {
            _mergeScratch.Clear();
            if (_store.TryGet(owner, collectionKeyId, out EntityCollectionHandle handle) &&
                _store.TryGetView(handle, out EntityCollectionView view))
            {
                if (view.Count > _currentScratch.Length)
                {
                    _currentScratch = new Entity[view.Count * 2];
                }

                int currentCount = _store.CopyEntities(owner, collectionKeyId, _currentScratch);
                if (adding)
                {
                    AppendDistinct(_mergeScratch, _currentScratch.AsSpan(0, currentCount));
                    AppendDistinct(_mergeScratch, entitySet);
                }
                else
                {
                    for (int i = 0; i < currentCount; i++)
                    {
                        Entity current = _currentScratch[i];
                        bool removed = false;
                        for (int r = 0; r < entitySet.Length; r++)
                        {
                            if (entitySet[r] == current)
                            {
                                removed = true;
                                break;
                            }
                        }

                        if (!removed)
                        {
                            _mergeScratch.Add(current);
                        }
                    }
                }
            }
            else if (adding)
            {
                AppendDistinct(_mergeScratch, entitySet);
            }

            if (_mergeScratch.Count > _mergeResultScratch.Length)
            {
                int next = _mergeResultScratch.Length == 0 ? 64 : _mergeResultScratch.Length;
                while (next < _mergeScratch.Count)
                {
                    next *= 2;
                }

                _mergeResultScratch = new Entity[next];
            }

            for (int i = 0; i < _mergeScratch.Count; i++)
            {
                _mergeResultScratch[i] = _mergeScratch[i];
            }

            return _mergeResultScratch.AsSpan(0, _mergeScratch.Count);
        }

        private static void AppendDistinct(List<Entity> destination, ReadOnlySpan<Entity> source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (destination.Contains(source[i]))
                {
                    continue;
                }

                destination.Add(source[i]);
            }
        }

        private void Write(Entity owner, int collectionKeyId, ReadOnlySpan<Entity> entities)
        {
            string key = _store.KeyRegistry.GetName(collectionKeyId)
                ?? throw new InvalidOperationException(
                    $"EVENT.COLLECTION.KeyUnknown: collection key id {collectionKeyId} is not registered in the EntityCollectionStore key space.");
            var descriptor = EntityCollectionDescriptor.Create(
                key,
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.CommandSource);
            _store.Replace(owner, collectionKeyId, descriptor, entities, owner);
        }
    }

    /// <summary>Set semantics carried by the collection pass-through contract.</summary>
    public enum EventCollectionWriteOp : byte
    {
        Replace = 0,
        Add = 1,
        Subtract = 2,
    }
}
