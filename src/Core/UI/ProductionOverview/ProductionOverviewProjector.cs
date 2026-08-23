using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;

namespace Ludots.Core.UI.ProductionOverview
{
    /// <summary>
    /// Projects a production/worker/queue overview profile into a DataPlane snapshot.
    /// Reads existing command panel status/queue and/or OrderBuffer plus entity-collection
    /// worker stats — never owns a parallel production store or production rules.
    /// </summary>
    public sealed class ProductionOverviewProjector
    {
        private const int MaxMembers = 256;
        private const int MaxStatuses = 16;
        private const int MaxQueueItems = 16;
        private const int MaxSlots = 64;

        private readonly IEntityCommandPanelSourceRegistry _sources;
        private readonly EntityCollectionStore? _collections;
        private readonly ControlPlaneView? _controlPlane;
        private readonly StringIntRegistry? _collectionKeys;
        private readonly OrderTypeRegistry? _orderTypes;
        private readonly World? _world;

        private readonly Entity[] _memberScratch = new Entity[MaxMembers];
        private readonly Entity[] _controlPlaneScratch = new Entity[MaxMembers];
        private readonly EntityCommandPanelStatusView[] _statusScratch = new EntityCommandPanelStatusView[MaxStatuses];
        private readonly EntityCommandPanelQueueItemView[] _queueScratch = new EntityCommandPanelQueueItemView[MaxQueueItems];
        private readonly EntityCommandPanelSlotView[] _slotScratch = new EntityCommandPanelSlotView[MaxSlots];

        public ProductionOverviewProjector(
            IEntityCommandPanelSourceRegistry sources,
            EntityCollectionStore? collections = null,
            ControlPlaneView? controlPlane = null,
            StringIntRegistry? collectionKeys = null,
            OrderTypeRegistry? orderTypes = null,
            World? world = null)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _collections = collections;
            _controlPlane = controlPlane;
            _collectionKeys = collectionKeys;
            _orderTypes = orderTypes;
            _world = world;
        }

        public ProductionOverviewSnapshot Project(
            ProductionOverviewProfile profile,
            in ProductionOverviewBindingContext binding)
        {
            ArgumentNullException.ThrowIfNull(profile);

            if (!_sources.TryGet(profile.CommandPanelSourceId, out IEntityCommandPanelSource source))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' references unknown command panel source '{profile.CommandPanelSourceId}'.");
            }

            Entity owner = ResolveOwner(profile, in binding);
            string instanceKey = ResolveInstanceKey(profile, in binding, owner);
            int memberCount = CopyProducerMembers(profile, owner, instanceKey);

            var rows = new List<ProductionOverviewStatusRow>(memberCount);
            var queueItems = new List<ProductionOverviewQueueItem>(memberCount);
            var blockedReasons = new List<string>(8);

            uint revision = HashCombine(2166136261u, HashString(profile.Id));
            revision = HashCombine(revision, (uint)profile.QueueSourceKind);
            revision = HashCombine(revision, (uint)owner.Id);
            revision = HashCombine(revision, (uint)owner.Version);

            for (int i = 0; i < memberCount; i++)
            {
                Entity member = _memberScratch[i];
                if (_world != null && !_world.IsAlive(member))
                {
                    continue;
                }

                var memberContext = new EntityCommandPanelSourceContext(
                    member,
                    profile.CommandPanelSourceId,
                    instanceKey);

                if (EntityCommandPanelSourceDispatch.TryGetRevision(source, in memberContext, out uint memberRevision))
                {
                    revision = HashCombine(revision, memberRevision);
                }

                AppendStatuses(source, in memberContext, member, rows, ref revision);
                AppendBlockedReasons(source, in memberContext, blockedReasons, ref revision);
                AppendQueueItems(profile, source, in memberContext, member, queueItems, ref revision);
            }

            IReadOnlyList<ProductionOverviewWorkerRow> workerRows = ProjectWorkers(profile, in binding, ref revision);
            revision = HashCombine(revision, (uint)rows.Count);
            revision = HashCombine(revision, (uint)queueItems.Count);
            revision = HashCombine(revision, (uint)workerRows.Count);
            revision = HashCombine(revision, (uint)blockedReasons.Count);

            return new ProductionOverviewSnapshot(
                profile.Id,
                owner.Id,
                owner.Version,
                revision == 0 ? 1u : revision,
                rows,
                queueItems,
                workerRows,
                blockedReasons);
        }

        private void AppendStatuses(
            IEntityCommandPanelSource source,
            in EntityCommandPanelSourceContext context,
            Entity owner,
            List<ProductionOverviewStatusRow> rows,
            ref uint revision)
        {
            int count = EntityCommandPanelSourceDispatch.CopyStatuses(source, in context, _statusScratch);
            for (int i = 0; i < count; i++)
            {
                EntityCommandPanelStatusView status = _statusScratch[i];
                rows.Add(new ProductionOverviewStatusRow(
                    owner.Id,
                    owner.Version,
                    status.Label,
                    status.Detail,
                    status.ProgressPermille,
                    status.AccentColorHex,
                    blockedReason: string.Empty));
                revision = HashCombine(revision, (uint)status.ProgressPermille);
                revision = HashCombine(revision, HashString(status.Label));
            }
        }

        private void AppendBlockedReasons(
            IEntityCommandPanelSource source,
            in EntityCommandPanelSourceContext context,
            List<string> blockedReasons,
            ref uint revision)
        {
            int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
            if (groupCount <= 0)
            {
                return;
            }

            int slotCount = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, _slotScratch);
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = _slotScratch[i];
                if ((slot.StateFlags & EntityCommandSlotStateFlags.Blocked) == 0)
                {
                    continue;
                }

                string reason = string.IsNullOrWhiteSpace(slot.DetailLabel) ? "blocked" : slot.DetailLabel;
                if (blockedReasons.Contains(reason))
                {
                    continue;
                }

                blockedReasons.Add(reason);
                revision = HashCombine(revision, HashString(reason));
            }
        }

        private void AppendQueueItems(
            ProductionOverviewProfile profile,
            IEntityCommandPanelSource source,
            in EntityCommandPanelSourceContext context,
            Entity owner,
            List<ProductionOverviewQueueItem> queueItems,
            ref uint revision)
        {
            switch (profile.QueueSourceKind)
            {
                case ProductionQueueSourceKind.CommandPanelSupplemental:
                    AppendCommandPanelQueue(source, in context, owner, queueItems, ref revision);
                    break;
                case ProductionQueueSourceKind.OrderBuffer:
                    AppendOrderBufferQueue(profile, owner, queueItems, ref revision);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Production overview profile '{profile.Id}' has unsupported queueSourceKind '{profile.QueueSourceKind}'.");
            }
        }

        private void AppendCommandPanelQueue(
            IEntityCommandPanelSource source,
            in EntityCommandPanelSourceContext context,
            Entity owner,
            List<ProductionOverviewQueueItem> queueItems,
            ref uint revision)
        {
            // Status progress is the SSOT for active work; attach it to matching Active queue rows.
            int statusCount = EntityCommandPanelSourceDispatch.CopyStatuses(source, in context, _statusScratch);
            int count = EntityCommandPanelSourceDispatch.CopyQueueItems(source, in context, _queueScratch);
            for (int i = 0; i < count; i++)
            {
                EntityCommandPanelQueueItemView item = _queueScratch[i];
                string stage = ToStageId(item.Stage);
                short progress = 0;
                if (item.Stage == EntityCommandPanelQueueStage.Active)
                {
                    for (int s = 0; s < statusCount; s++)
                    {
                        if (string.Equals(_statusScratch[s].Label, item.Label, StringComparison.Ordinal))
                        {
                            progress = _statusScratch[s].ProgressPermille;
                            break;
                        }
                    }
                }

                queueItems.Add(new ProductionOverviewQueueItem(
                    owner.Id,
                    owner.Version,
                    stage,
                    item.Label,
                    item.Detail,
                    progress,
                    item.AccentColorHex,
                    blockedReason: string.Empty));
                revision = HashCombine(revision, HashString(stage));
                revision = HashCombine(revision, HashString(item.Label));
                revision = HashCombine(revision, (uint)(ushort)progress);
            }
        }

        private void AppendOrderBufferQueue(
            ProductionOverviewProfile profile,
            Entity owner,
            List<ProductionOverviewQueueItem> queueItems,
            ref uint revision)
        {
            if (_world == null)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' orderBuffer queueSourceKind requires World.");
            }

            if (!_world.IsAlive(owner) || !_world.Has<OrderBuffer>(owner))
            {
                return;
            }

            ref OrderBuffer orders = ref _world.Get<OrderBuffer>(owner);
            if (orders.HasActive)
            {
                AppendOrderItem(owner, in orders.ActiveOrder.Order, ProductionQueueStageIds.Active, queueItems, ref revision);
            }

            for (int i = 0; i < orders.QueuedCount; i++)
            {
                QueuedOrder queued = orders.GetQueued(i);
                AppendOrderItem(owner, in queued.Order, ProductionQueueStageIds.Queued, queueItems, ref revision);
            }

            if (orders.HasPending)
            {
                AppendOrderItem(owner, in orders.PendingOrder.Order, ProductionQueueStageIds.Pending, queueItems, ref revision);
            }
        }

        private void AppendOrderItem(
            Entity owner,
            in Order order,
            string stage,
            List<ProductionOverviewQueueItem> queueItems,
            ref uint revision)
        {
            string label = ResolveOrderLabel(in order);
            queueItems.Add(new ProductionOverviewQueueItem(
                owner.Id,
                owner.Version,
                stage,
                label,
                detail: string.Empty,
                progressPermille: 0,
                accentColorHex: string.Empty,
                blockedReason: string.Empty));
            revision = HashCombine(revision, (uint)order.OrderTypeId);
            revision = HashCombine(revision, HashString(stage));
            revision = HashCombine(revision, HashString(label));
        }

        private string ResolveOrderLabel(in Order order)
        {
            if (_orderTypes != null &&
                order.OrderTypeId > 0 &&
                _orderTypes.TryGet(order.OrderTypeId, out OrderTypeConfig config))
            {
                if (!string.IsNullOrWhiteSpace(config.Label))
                {
                    return config.Label;
                }

                if (!string.IsNullOrWhiteSpace(config.Key))
                {
                    return config.Key;
                }
            }

            return order.OrderTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private IReadOnlyList<ProductionOverviewWorkerRow> ProjectWorkers(
            ProductionOverviewProfile profile,
            in ProductionOverviewBindingContext binding,
            ref uint revision)
        {
            if (profile.WorkerBuckets.Count == 0)
            {
                return Array.Empty<ProductionOverviewWorkerRow>();
            }

            if (string.IsNullOrWhiteSpace(profile.WorkerCollectionKey))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' declares workerBuckets but missing workerCollectionKey.");
            }

            if (_collections == null || _collectionKeys == null)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' worker overview requires EntityCollectionStore and collection key registry.");
            }

            if (_world == null)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' worker overview requires World.");
            }

            if (!_collectionKeys.TryGetId(profile.WorkerCollectionKey, out _))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' workerCollectionKey '{profile.WorkerCollectionKey}' is not a registered collection key.");
            }

            Entity owner = ResolveOwner(profile, in binding);
            if (!_collections.TryGet(owner, profile.WorkerCollectionKey, out EntityCollectionHandle handle))
            {
                return BuildEmptyWorkerRows(profile, ref revision);
            }

            int memberCount = _collections.CopyEntities(handle, 0, _memberScratch);
            var counts = new int[profile.WorkerBuckets.Count];
            for (int i = 0; i < memberCount; i++)
            {
                Entity worker = _memberScratch[i];
                if (!_world.IsAlive(worker))
                {
                    continue;
                }

                int bucketIndex = MatchWorkerBucket(profile, worker);
                if (bucketIndex < 0)
                {
                    continue;
                }

                counts[bucketIndex]++;
            }

            var rows = new ProductionOverviewWorkerRow[profile.WorkerBuckets.Count];
            for (int i = 0; i < profile.WorkerBuckets.Count; i++)
            {
                ProductionWorkerBucket bucket = profile.WorkerBuckets[i];
                rows[i] = new ProductionOverviewWorkerRow(bucket.BucketId, bucket.DisplayTokenId, counts[i], bucket.SortOrder);
                revision = HashCombine(revision, (uint)counts[i]);
                revision = HashCombine(revision, HashString(bucket.BucketId));
            }

            return rows;
        }

        private IReadOnlyList<ProductionOverviewWorkerRow> BuildEmptyWorkerRows(
            ProductionOverviewProfile profile,
            ref uint revision)
        {
            var rows = new ProductionOverviewWorkerRow[profile.WorkerBuckets.Count];
            for (int i = 0; i < profile.WorkerBuckets.Count; i++)
            {
                ProductionWorkerBucket bucket = profile.WorkerBuckets[i];
                rows[i] = new ProductionOverviewWorkerRow(bucket.BucketId, bucket.DisplayTokenId, 0, bucket.SortOrder);
                revision = HashCombine(revision, HashString(bucket.BucketId));
            }

            return rows;
        }

        private int MatchWorkerBucket(ProductionOverviewProfile profile, Entity worker)
        {
            int idleIndex = -1;
            for (int i = 0; i < profile.WorkerBuckets.Count; i++)
            {
                ProductionWorkerBucket bucket = profile.WorkerBuckets[i];
                if (bucket.MatchKind == ProductionWorkerMatchKind.Idle)
                {
                    idleIndex = i;
                    continue;
                }

                if (MatchesBucket(bucket, worker))
                {
                    return i;
                }
            }

            if (idleIndex >= 0 && IsIdleWorker(worker))
            {
                return idleIndex;
            }

            return -1;
        }

        private bool MatchesBucket(ProductionWorkerBucket bucket, Entity worker)
        {
            switch (bucket.MatchKind)
            {
                case ProductionWorkerMatchKind.Tag:
                {
                    int tagId = TagRegistry.GetId(bucket.MatchRef);
                    if (tagId == TagRegistry.InvalidId)
                    {
                        throw new InvalidOperationException(
                            $"Worker bucket '{bucket.BucketId}' references unknown tag '{bucket.MatchRef}'.");
                    }

                    return _world!.Has<GameplayTagContainer>(worker) &&
                           _world.Get<GameplayTagContainer>(worker).HasTag(tagId);
                }

                case ProductionWorkerMatchKind.OrderType:
                {
                    if (_orderTypes == null || !_orderTypes.TryGetId(bucket.MatchRef, out int orderTypeId))
                    {
                        throw new InvalidOperationException(
                            $"Worker bucket '{bucket.BucketId}' references unknown order type '{bucket.MatchRef}'.");
                    }

                    if (!_world!.Has<OrderBuffer>(worker))
                    {
                        return false;
                    }

                    ref OrderBuffer orders = ref _world.Get<OrderBuffer>(worker);
                    return orders.HasActive && orders.ActiveOrder.Order.OrderTypeId == orderTypeId;
                }

                case ProductionWorkerMatchKind.AttributePositive:
                {
                    int attrId = AttributeRegistry.GetId(bucket.MatchRef);
                    if (attrId == AttributeRegistry.InvalidId)
                    {
                        throw new InvalidOperationException(
                            $"Worker bucket '{bucket.BucketId}' references unknown attribute '{bucket.MatchRef}'.");
                    }

                    if (!_world!.Has<AttributeBuffer>(worker))
                    {
                        return false;
                    }

                    return _world.Get<AttributeBuffer>(worker).GetCurrent(attrId) > 0f;
                }

                case ProductionWorkerMatchKind.Idle:
                    return IsIdleWorker(worker);

                default:
                    throw new InvalidOperationException(
                        $"Worker bucket '{bucket.BucketId}' has unsupported matchKind '{bucket.MatchKind}'.");
            }
        }

        private bool IsIdleWorker(Entity worker)
        {
            if (!_world!.Has<OrderBuffer>(worker))
            {
                return true;
            }

            ref OrderBuffer orders = ref _world.Get<OrderBuffer>(worker);
            return orders.IsEmpty && !orders.HasPending;
        }

        private Entity ResolveOwner(ProductionOverviewProfile profile, in ProductionOverviewBindingContext binding)
        {
            switch (profile.SourceKind)
            {
                case ProductionOverviewSourceKind.ExplicitEntity:
                    if (binding.FocusedEntity == Entity.Null)
                    {
                        throw new InvalidOperationException(
                            $"Production overview profile '{profile.Id}' explicitEntity source requires focusedEntity in the binding context.");
                    }

                    return binding.FocusedEntity;

                case ProductionOverviewSourceKind.SolePossessedRep:
                case ProductionOverviewSourceKind.EntityCollection:
                case ProductionOverviewSourceKind.ControlPlaneView:
                    if (binding.CollectionOwner != Entity.Null)
                    {
                        return binding.CollectionOwner;
                    }

                    if (binding.SolePossessedRep != Entity.Null)
                    {
                        return binding.SolePossessedRep;
                    }

                    throw new InvalidOperationException(
                        $"Production overview profile '{profile.Id}' requires a sole possessed rep or collection owner in the binding context.");

                default:
                    throw new InvalidOperationException(
                        $"Production overview profile '{profile.Id}' has unsupported sourceKind '{profile.SourceKind}'.");
            }
        }

        private string ResolveInstanceKey(
            ProductionOverviewProfile profile,
            in ProductionOverviewBindingContext binding,
            Entity owner)
        {
            switch (profile.SourceKind)
            {
                case ProductionOverviewSourceKind.ExplicitEntity:
                    return binding.InstanceKey;

                case ProductionOverviewSourceKind.SolePossessedRep:
                case ProductionOverviewSourceKind.EntityCollection:
                    return string.IsNullOrWhiteSpace(binding.InstanceKey)
                        ? profile.SourceRef
                        : binding.InstanceKey;

                case ProductionOverviewSourceKind.ControlPlaneView:
                    return EnsureControlPlaneMaterialized(profile, owner);

                default:
                    throw new InvalidOperationException(
                        $"Production overview profile '{profile.Id}' has unsupported sourceKind '{profile.SourceKind}'.");
            }
        }

        private string EnsureControlPlaneMaterialized(ProductionOverviewProfile profile, Entity owner)
        {
            if (_controlPlane == null || _collections == null || _collectionKeys == null)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' controlPlaneView source requires ControlPlaneView, EntityCollectionStore, and collection key registry.");
            }

            if (!_collectionKeys.TryGetId(profile.SourceRef, out int collectionKeyId))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' controlPlaneView sourceRef '{profile.SourceRef}' is not a registered collection key.");
            }

            string materializationKey = EntityViewKeys.ControlPlaneCommand;
            if (!_collectionKeys.TryGetId(materializationKey, out _))
            {
                _collectionKeys.Register(materializationKey);
            }

            int memberCount = _controlPlane.CopyMembers(owner, collectionKeyId, _controlPlaneScratch);
            var descriptor = EntityCollectionDescriptor.Create(
                materializationKey,
                EntityCollectionSourceKind.CollectionView,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: memberCount > 0 ? _controlPlaneScratch[0] : Entity.Null,
                title: profile.Id);
            _collections.Replace(owner, descriptor, _controlPlaneScratch.AsSpan(0, memberCount));
            return materializationKey;
        }

        /// <summary>
        /// Resolves producer members for the overview.
        /// <list type="bullet">
        /// <item>
        /// <see cref="ProductionOverviewSourceKind.ExplicitEntity"/> /
        /// <see cref="ProductionOverviewSourceKind.SolePossessedRep"/>:
        /// documented single-owner behavior — the resolved owner is the sole producer.
        /// </item>
        /// <item>
        /// <see cref="ProductionOverviewSourceKind.EntityCollection"/> /
        /// <see cref="ProductionOverviewSourceKind.ControlPlaneView"/>:
        /// require a non-empty producer collection at sourceRef/instanceKey; missing store,
        /// missing collection key/view, or empty collection fail fast (no owner fallback).
        /// </item>
        /// </list>
        /// </summary>
        private int CopyProducerMembers(ProductionOverviewProfile profile, Entity owner, string instanceKey)
        {
            if (profile.SourceKind is ProductionOverviewSourceKind.ExplicitEntity
                or ProductionOverviewSourceKind.SolePossessedRep)
            {
                _memberScratch[0] = owner;
                return 1;
            }

            if (profile.SourceKind is not (ProductionOverviewSourceKind.EntityCollection
                or ProductionOverviewSourceKind.ControlPlaneView))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' has unsupported sourceKind '{profile.SourceKind}'.");
            }

            string collectionKey = string.IsNullOrWhiteSpace(instanceKey) ? profile.SourceRef : instanceKey;
            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' sourceKind '{ToSourceKindId(profile.SourceKind)}' requires sourceRef (collection/query key).");
            }

            if (_collections == null)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' sourceKind '{ToSourceKindId(profile.SourceKind)}' sourceRef '{collectionKey}' requires EntityCollectionStore.");
            }

            if (!_collections.TryGet(owner, collectionKey, out EntityCollectionHandle handle))
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' missing producer collection for sourceRef '{collectionKey}'.");
            }

            int count = _collections.CopyEntities(handle, 0, _memberScratch);
            if (count <= 0)
            {
                throw new InvalidOperationException(
                    $"Production overview profile '{profile.Id}' producer collection sourceRef '{collectionKey}' is empty.");
            }

            return Math.Min(count, MaxMembers);
        }

        private static string ToSourceKindId(ProductionOverviewSourceKind sourceKind)
        {
            return sourceKind switch
            {
                ProductionOverviewSourceKind.SolePossessedRep => ProductionOverviewSourceKindIds.SolePossessedRep,
                ProductionOverviewSourceKind.ExplicitEntity => ProductionOverviewSourceKindIds.ExplicitEntity,
                ProductionOverviewSourceKind.EntityCollection => ProductionOverviewSourceKindIds.EntityCollection,
                ProductionOverviewSourceKind.ControlPlaneView => ProductionOverviewSourceKindIds.ControlPlaneView,
                _ => sourceKind.ToString()
            };
        }

        private static string ToStageId(EntityCommandPanelQueueStage stage)
        {
            return stage switch
            {
                EntityCommandPanelQueueStage.Active => ProductionQueueStageIds.Active,
                EntityCommandPanelQueueStage.Queued => ProductionQueueStageIds.Queued,
                EntityCommandPanelQueueStage.Pending => ProductionQueueStageIds.Pending,
                _ => throw new InvalidOperationException($"Unknown command panel queue stage '{stage}'.")
            };
        }

        private static uint HashCombine(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
                return hash;
            }
        }

        private static uint HashString(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash;
            }
        }
    }

    public static class ProductionQueueStageIds
    {
        public const string Active = "active";
        public const string Queued = "queued";
        public const string Pending = "pending";
    }
}
