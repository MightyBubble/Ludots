using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class CollectionGasEntityCommandPanelSource :
        IEntityCommandPanelContextSource,
        IEntityCommandPanelContextActionSource
    {
        public const string SourceId = "gas.collection-ability-slots";

        private const int MaxOwners = 256;
        private const int MaxAggregatedSlots = 64;

        private readonly EntityCollectionStore _collections;
        private readonly GasEntityCommandPanelSource _gasSource;
        private readonly IEntityCommandPanelCollectionQueryConfigRegistry _queryConfigs;
        private readonly Entity[] _ownerScratch = new Entity[MaxOwners];
        private readonly EntityCommandPanelSlotView[] _ownerSlots = new EntityCommandPanelSlotView[8];
        private readonly EntityCommandPanelSlotView[] _aggregatedSlots = new EntityCommandPanelSlotView[MaxAggregatedSlots];
        private readonly int[] _ownerCounts = new int[MaxAggregatedSlots];
        private readonly Entity[] _activationOwners = new Entity[MaxAggregatedSlots];
        private readonly int[] _activationSlotIndices = new int[MaxAggregatedSlots];
        private readonly string[] _detailCacheBaseLabels = new string[MaxAggregatedSlots];
        private readonly int[] _detailCacheOwnerCounts = new int[MaxAggregatedSlots];
        private readonly string[] _detailCacheLabels = new string[MaxAggregatedSlots];

        public CollectionGasEntityCommandPanelSource(
            EntityCollectionStore collections,
            GasEntityCommandPanelSource gasSource,
            IEntityCommandPanelCollectionQueryConfigRegistry queryConfigs)
        {
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _gasSource = gasSource ?? throw new ArgumentNullException(nameof(gasSource));
            _queryConfigs = queryConfigs ?? throw new ArgumentNullException(nameof(queryConfigs));
        }

        public bool TryGetRevision(in EntityCommandPanelSourceContext context, out uint revision)
        {
            revision = 0;
            if (!TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            unchecked
            {
                uint hash = 2166136261u;
                hash = HashCombine(hash, view.Revision);
                hash = HashCombine(hash, (uint)view.Count);
                int ownerCount = CopyOwners(handle, _ownerScratch);
                for (int i = 0; i < ownerCount; i++)
                {
                    Entity owner = _ownerScratch[i];
                    hash = HashCombine(hash, (uint)owner.Id);
                    hash = HashCombine(hash, (uint)owner.Version);
                    if (_gasSource.TryGetRevision(owner, out uint ownerRevision))
                    {
                        hash = HashCombine(hash, ownerRevision);
                    }
                }

                revision = hash == 0 ? 1u : hash;
            }

            return true;
        }

        public int GetGroupCount(in EntityCommandPanelSourceContext context)
        {
            return TryResolveCollection(context, out _, out _, out EntityCollectionView view) && view.Count > 0 ? 1 : 0;
        }

        public bool TryGetGroup(in EntityCommandPanelSourceContext context, int groupIndex, out EntityCommandPanelGroupView group)
        {
            group = default;
            if (groupIndex != 0 ||
                !TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out EntityCollectionView view))
            {
                return false;
            }

            int slotCount = BuildAggregatedSlots(handle, config, Span<EntityCommandPanelSlotView>.Empty, updateActivationMap: false);
            string title = !string.IsNullOrWhiteSpace(config.Title)
                ? config.Title
                : string.IsNullOrWhiteSpace(view.Title) ? "Collection Commands" : view.Title;
            group = new EntityCommandPanelGroupView(0, title, (byte)Math.Min(byte.MaxValue, slotCount));
            return true;
        }

        public int CopySlots(in EntityCommandPanelSourceContext context, int groupIndex, Span<EntityCommandPanelSlotView> destination)
        {
            if (groupIndex != 0 ||
                destination.IsEmpty ||
                !TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out _))
            {
                return 0;
            }

            return BuildAggregatedSlots(handle, config, destination, updateActivationMap: true);
        }

        public bool ActivateSlot(in EntityCommandPanelSourceContext context, int groupIndex, int slotIndex)
        {
            if (groupIndex != 0 ||
                slotIndex < 0 ||
                !TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out _))
            {
                return false;
            }

            BuildAggregatedSlots(handle, config, Span<EntityCommandPanelSlotView>.Empty, updateActivationMap: true);
            if ((uint)slotIndex >= (uint)_activationOwners.Length ||
                _activationOwners[slotIndex] == Entity.Null)
            {
                return false;
            }

            return _gasSource.ActivateSlot(_activationOwners[slotIndex], groupIndex, _activationSlotIndices[slotIndex]);
        }

        public bool TryGetRevision(Entity target, out uint revision)
        {
            revision = 0;
            return false;
        }

        public int GetGroupCount(Entity target) => 0;

        public bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group)
        {
            group = default;
            return false;
        }

        public int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination) => 0;

        public bool ActivateSlot(Entity target, int groupIndex, int slotIndex) => false;

        private bool TryResolveCollection(
            in EntityCommandPanelSourceContext context,
            out EntityCommandPanelCollectionQueryConfig config,
            out EntityCollectionHandle handle,
            out EntityCollectionView view)
        {
            config = null!;
            handle = EntityCollectionHandle.Invalid;
            view = default;
            if (context.TargetEntity == Entity.Null ||
                string.IsNullOrWhiteSpace(context.InstanceKey))
            {
                return false;
            }

            if (!_queryConfigs.TryGet(context.InstanceKey, out config))
            {
                throw new InvalidOperationException(
                    $"Entity command panel collection query '{context.InstanceKey}' is not registered.");
            }

            return _collections.TryGet(context.TargetEntity, config.CollectionKey, out handle) &&
                   _collections.TryGetView(handle, out view);
        }

        private int CopyOwners(EntityCollectionHandle handle, Span<Entity> destination)
        {
            return _collections.CopyEntities(handle, 0, destination);
        }

        private int BuildAggregatedSlots(
            EntityCollectionHandle handle,
            EntityCommandPanelCollectionQueryConfig config,
            Span<EntityCommandPanelSlotView> destination,
            bool updateActivationMap)
        {
            if (updateActivationMap)
            {
                Array.Clear(_activationOwners, 0, _activationOwners.Length);
                Array.Clear(_activationSlotIndices, 0, _activationSlotIndices.Length);
            }

            Array.Clear(_ownerCounts, 0, _ownerCounts.Length);
            int aggregateCount = 0;
            int ownerCount = CopyOwners(handle, _ownerScratch);
            for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
            {
                Entity owner = _ownerScratch[ownerIndex];
                int ownerSlotCount = _gasSource.CopySlots(owner, 0, _ownerSlots);
                for (int ownerSlotIndex = 0; ownerSlotIndex < ownerSlotCount; ownerSlotIndex++)
                {
                    EntityCommandPanelSlotView ownerSlot = _ownerSlots[ownerSlotIndex];
                    if (HasState(ownerSlot.StateFlags, EntityCommandSlotStateFlags.Empty))
                    {
                        continue;
                    }

                    if (!MatchesFilter(ownerSlot, config.Filter))
                    {
                        continue;
                    }

                    int aggregateIndex = FindAggregate(ownerSlot, aggregateCount);
                    if (aggregateIndex < 0)
                    {
                        if (aggregateCount >= MaxAggregatedSlots)
                        {
                            continue;
                        }

                        aggregateIndex = aggregateCount++;
                        _aggregatedSlots[aggregateIndex] = ownerSlot;
                        _ownerCounts[aggregateIndex] = 0;
                        if (updateActivationMap)
                        {
                            _activationOwners[aggregateIndex] = owner;
                            _activationSlotIndices[aggregateIndex] = ownerSlot.SlotIndex;
                        }
                    }

                    _ownerCounts[aggregateIndex]++;
                    _aggregatedSlots[aggregateIndex] = MergeAggregate(_aggregatedSlots[aggregateIndex], ownerSlot, aggregateIndex, _ownerCounts[aggregateIndex]);
                }
            }

            SortAggregates(aggregateCount, config.Sort);
            int written = Math.Min(destination.Length, aggregateCount);
            for (int i = 0; i < written; i++)
            {
                destination[i] = WithDisplaySlotIndexAndDetail(_aggregatedSlots[i], i, ResolveAggregateDetail(i));
            }

            return destination.IsEmpty ? aggregateCount : written;
        }

        private static bool MatchesFilter(
            in EntityCommandPanelSlotView slot,
            in EntityCommandPanelCollectionFilter filter)
        {
            return filter.Kind switch
            {
                EntityCommandPanelCollectionFilterKind.Any => true,
                EntityCommandPanelCollectionFilterKind.Ready => !HasState(slot.StateFlags, EntityCommandSlotStateFlags.Blocked),
                EntityCommandPanelCollectionFilterKind.Blocked => HasState(slot.StateFlags, EntityCommandSlotStateFlags.Blocked),
                EntityCommandPanelCollectionFilterKind.Active => HasState(slot.StateFlags, EntityCommandSlotStateFlags.Active),
                EntityCommandPanelCollectionFilterKind.AbilityId => slot.AbilityId == filter.AbilityId,
                EntityCommandPanelCollectionFilterKind.ActionId => string.Equals(slot.ActionId, filter.ActionId, StringComparison.Ordinal),
                _ => false
            };
        }

        private int FindAggregate(in EntityCommandPanelSlotView slot, int count)
        {
            for (int i = 0; i < count; i++)
            {
                EntityCommandPanelSlotView existing = _aggregatedSlots[i];
                if (existing.AbilityId == slot.AbilityId &&
                    existing.TemplateEntityId == slot.TemplateEntityId &&
                    existing.SlotIndex == slot.SlotIndex &&
                    string.Equals(existing.ActionId, slot.ActionId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static EntityCommandPanelSlotView MergeAggregate(
            in EntityCommandPanelSlotView existing,
            in EntityCommandPanelSlotView next,
            int aggregateIndex,
            int ownerCount)
        {
            EntityCommandSlotStateFlags flags = existing.StateFlags | next.StateFlags;
            short cooldown = Math.Max(existing.CooldownPermille, next.CooldownPermille);
            return new EntityCommandPanelSlotView(
                aggregateIndex,
                existing.AbilityId,
                existing.TemplateEntityId,
                flags,
                cooldown,
                existing.ChargesCurrent,
                existing.ChargesMax,
                existing.DisplayLabel,
                next.DetailLabel,
                existing.ActionId);
        }

        private string ResolveAggregateDetail(int aggregateIndex)
        {
            int ownerCount = _ownerCounts[aggregateIndex];
            string baseDetail = _aggregatedSlots[aggregateIndex].DetailLabel;
            if (ownerCount <= 1)
            {
                return baseDetail;
            }

            if (_detailCacheOwnerCounts[aggregateIndex] == ownerCount &&
                string.Equals(_detailCacheBaseLabels[aggregateIndex], baseDetail, StringComparison.Ordinal))
            {
                return _detailCacheLabels[aggregateIndex] ?? string.Empty;
            }

            string detail = string.Concat(ownerCount.ToString(System.Globalization.CultureInfo.InvariantCulture), " owners | ", baseDetail);
            _detailCacheOwnerCounts[aggregateIndex] = ownerCount;
            _detailCacheBaseLabels[aggregateIndex] = baseDetail;
            _detailCacheLabels[aggregateIndex] = detail;
            return detail;
        }

        private void SortAggregates(int count, EntityCommandPanelCollectionSortKind sortKind)
        {
            for (int i = 1; i < count; i++)
            {
                EntityCommandPanelSlotView slot = _aggregatedSlots[i];
                int ownerCount = _ownerCounts[i];
                Entity activationOwner = _activationOwners[i];
                int activationSlot = _activationSlotIndices[i];
                int j = i - 1;
                while (j >= 0 && CompareAggregate(_aggregatedSlots[j], _ownerCounts[j], slot, ownerCount, sortKind) > 0)
                {
                    _aggregatedSlots[j + 1] = _aggregatedSlots[j];
                    _ownerCounts[j + 1] = _ownerCounts[j];
                    _activationOwners[j + 1] = _activationOwners[j];
                    _activationSlotIndices[j + 1] = _activationSlotIndices[j];
                    j--;
                }

                _aggregatedSlots[j + 1] = slot;
                _ownerCounts[j + 1] = ownerCount;
                _activationOwners[j + 1] = activationOwner;
                _activationSlotIndices[j + 1] = activationSlot;
            }
        }

        private static int CompareAggregate(
            in EntityCommandPanelSlotView left,
            int leftOwnerCount,
            in EntityCommandPanelSlotView right,
            int rightOwnerCount,
            EntityCommandPanelCollectionSortKind sortKind)
        {
            return sortKind switch
            {
                EntityCommandPanelCollectionSortKind.OwnerCountThenSlotThenLabel => CompareOwnerCountThenSlotThenLabel(left, leftOwnerCount, right, rightOwnerCount),
                EntityCommandPanelCollectionSortKind.LabelThenSlot => CompareLabelThenSlot(left, right),
                EntityCommandPanelCollectionSortKind.AbilityIdThenSlot => CompareAbilityIdThenSlot(left, right),
                EntityCommandPanelCollectionSortKind.StatusThenSlotThenLabel => CompareStatusThenSlotThenLabel(left, leftOwnerCount, right, rightOwnerCount),
                _ => CompareSlotThenOwnerCountThenLabel(left, leftOwnerCount, right, rightOwnerCount)
            };
        }

        private static int CompareSlotThenOwnerCountThenLabel(
            in EntityCommandPanelSlotView left,
            int leftOwnerCount,
            in EntityCommandPanelSlotView right,
            int rightOwnerCount)
        {
            int slotCompare = left.SlotIndex.CompareTo(right.SlotIndex);
            if (slotCompare != 0)
            {
                return slotCompare;
            }

            int ownerCompare = rightOwnerCount.CompareTo(leftOwnerCount);
            if (ownerCompare != 0)
            {
                return ownerCompare;
            }

            return CompareLabelThenAbility(left, right);
        }

        private static int CompareOwnerCountThenSlotThenLabel(
            in EntityCommandPanelSlotView left,
            int leftOwnerCount,
            in EntityCommandPanelSlotView right,
            int rightOwnerCount)
        {
            int ownerCompare = rightOwnerCount.CompareTo(leftOwnerCount);
            if (ownerCompare != 0)
            {
                return ownerCompare;
            }

            int slotCompare = left.SlotIndex.CompareTo(right.SlotIndex);
            return slotCompare != 0 ? slotCompare : CompareLabelThenAbility(left, right);
        }

        private static int CompareLabelThenSlot(in EntityCommandPanelSlotView left, in EntityCommandPanelSlotView right)
        {
            int labelCompare = string.CompareOrdinal(left.DisplayLabel, right.DisplayLabel);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            int slotCompare = left.SlotIndex.CompareTo(right.SlotIndex);
            return slotCompare != 0 ? slotCompare : left.AbilityId.CompareTo(right.AbilityId);
        }

        private static int CompareAbilityIdThenSlot(in EntityCommandPanelSlotView left, in EntityCommandPanelSlotView right)
        {
            int abilityCompare = left.AbilityId.CompareTo(right.AbilityId);
            if (abilityCompare != 0)
            {
                return abilityCompare;
            }

            int slotCompare = left.SlotIndex.CompareTo(right.SlotIndex);
            return slotCompare != 0 ? slotCompare : string.CompareOrdinal(left.DisplayLabel, right.DisplayLabel);
        }

        private static int CompareStatusThenSlotThenLabel(
            in EntityCommandPanelSlotView left,
            int leftOwnerCount,
            in EntityCommandPanelSlotView right,
            int rightOwnerCount)
        {
            int statusCompare = ResolveStatusOrder(left.StateFlags).CompareTo(ResolveStatusOrder(right.StateFlags));
            return statusCompare != 0
                ? statusCompare
                : CompareSlotThenOwnerCountThenLabel(left, leftOwnerCount, right, rightOwnerCount);
        }

        private static int CompareLabelThenAbility(in EntityCommandPanelSlotView left, in EntityCommandPanelSlotView right)
        {
            int labelCompare = string.CompareOrdinal(left.DisplayLabel, right.DisplayLabel);
            return labelCompare != 0 ? labelCompare : left.AbilityId.CompareTo(right.AbilityId);
        }

        private static int ResolveStatusOrder(EntityCommandSlotStateFlags flags)
        {
            if (HasState(flags, EntityCommandSlotStateFlags.Active))
            {
                return 0;
            }

            return HasState(flags, EntityCommandSlotStateFlags.Blocked) ? 2 : 1;
        }

        private static bool HasState(EntityCommandSlotStateFlags value, EntityCommandSlotStateFlags flag)
        {
            return (value & flag) != 0;
        }

        private static EntityCommandPanelSlotView WithDisplaySlotIndexAndDetail(
            in EntityCommandPanelSlotView slot,
            int displaySlotIndex,
            string detailLabel)
        {
            return new EntityCommandPanelSlotView(
                displaySlotIndex,
                slot.AbilityId,
                slot.TemplateEntityId,
                slot.StateFlags,
                slot.CooldownPermille,
                slot.ChargesCurrent,
                slot.ChargesMax,
                slot.DisplayLabel,
                detailLabel,
                slot.ActionId);
        }

        private static uint HashCombine(uint hash, uint value)
        {
            unchecked
            {
                return (hash ^ value) * 16777619u;
            }
        }
    }
}
