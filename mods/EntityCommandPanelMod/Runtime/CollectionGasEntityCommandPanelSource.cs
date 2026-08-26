using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    /// <summary>
    /// Collection-selection GAS command panel source (RFC-0065 PNL-4). Grouping is fully delegated
    /// to the <see cref="AbilityAggregationProfileRegistry"/> kernel (DEC-10): every kernel group is
    /// one panel cell, the cell view is composed from the group's member slot views (representative
    /// icon/label from the first member, state flags OR-ed, lockout max, badge = member count).
    /// <see cref="CopyAggregationMembers"/> exposes the surviving member set for CommandDeck route
    /// profiles; <see cref="ActivateSlot"/> activates every surviving member behind the displayed
    /// group cell. <see cref="SetAggregationProfile"/> switches the active profile at
    /// runtime (P3): the next build regroups and the revision hash changes, so open panels re-pull
    /// without rebinding. FormSet switches need no extra code here — the kernel groups over the
    /// <c>AbilitySlotResolver</c> effective abilities on every build.
    /// </summary>
    public sealed class CollectionGasEntityCommandPanelSource :
        IEntityCommandPanelContextSource,
        IEntityCommandPanelContextActionSource,
        IEntityCommandPanelAggregationMemberSource
    {
        public const string SourceId = "gas.collection-ability-slots";

        private const int MaxOwners = 256;
        private const int MaxAggregatedSlots = 64;
        private const int MaxMembersPerSlot = MaxOwners;
        private const int OwnerViewStride = AbilityStateBuffer.CAPACITY;

        private readonly GameEngine _engine;
        private readonly EntityCollectionStore _collections;
        private readonly GasEntityCommandPanelSource _gasSource;
        private readonly IEntityCommandPanelCollectionQueryConfigRegistry _queryConfigs;
        private readonly AbilityAggregationProfileRegistry _aggregationProfiles;
        private readonly AbilityDefinitionRegistry _abilityDefinitions;
        private AbilityAggregationResult _aggregationResult = new();
        private readonly Entity[] _ownerScratch = new Entity[MaxOwners];
        private readonly EntityCommandPanelSlotView[] _ownerViews = new EntityCommandPanelSlotView[MaxOwners * OwnerViewStride];
        private readonly int[] _ownerViewCounts = new int[MaxOwners];
        private readonly Dictionary<int, int> _ownerIndexByEntityId = new(MaxOwners);
        private readonly EntityCommandPanelSlotView[] _aggregatedSlots = new EntityCommandPanelSlotView[MaxAggregatedSlots];
        private readonly int[] _ownerCounts = new int[MaxAggregatedSlots];
        private readonly Entity[] _activationOwners = new Entity[MaxAggregatedSlots];
        private readonly int[] _activationSlotIndices = new int[MaxAggregatedSlots];
        private readonly EntityCommandPanelAggregationMember[] _memberScratch =
            new EntityCommandPanelAggregationMember[MaxAggregatedSlots * MaxMembersPerSlot];
        private readonly int[] _memberStarts = new int[MaxAggregatedSlots + 1];
        private int _memberTotal;
        private readonly string[] _detailCacheBaseLabels = new string[MaxAggregatedSlots];
        private readonly int[] _detailCacheOwnerCounts = new int[MaxAggregatedSlots];
        private readonly string[] _detailCacheLabels = new string[MaxAggregatedSlots];

        private int _profileId;

        internal CollectionGasEntityCommandPanelSource(
            GameEngine engine,
            EntityCollectionStore collections,
            GasEntityCommandPanelSource gasSource,
            IEntityCommandPanelCollectionQueryConfigRegistry queryConfigs,
            AbilityAggregationProfileRegistry aggregationProfiles,
            string aggregationProfileId)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _gasSource = gasSource ?? throw new ArgumentNullException(nameof(gasSource));
            _queryConfigs = queryConfigs ?? throw new ArgumentNullException(nameof(queryConfigs));
            _aggregationProfiles = aggregationProfiles ?? throw new ArgumentNullException(nameof(aggregationProfiles));
            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException(
                    "AbilityDefinitionRegistry must be registered before the collection command panel source is created.");
            SetAggregationProfile(aggregationProfileId);
        }

        /// <summary>
        /// Switch the active aggregation profile (RFC-0065 P3 player preference). Takes effect on
        /// the next build; the panel revision changes so open panels regroup without re-selection.
        /// Fails fast on profiles that are not installed in the kernel registry.
        /// </summary>
        public void SetAggregationProfile(string profileId)
        {
            if (!_aggregationProfiles.ProfileIdRegistry.TryGetId(profileId, out int id) ||
                !_aggregationProfiles.IsInstalled(id))
            {
                throw new InvalidOperationException($"Aggregation profile '{profileId}' is not installed.");
            }

            _profileId = id;
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
                hash = HashCombine(hash, (uint)_profileId);
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

        public InputOrderActivationResult ActivateSlot(in EntityCommandPanelSourceContext context, int groupIndex, int slotIndex)
        {
            if (groupIndex != 0 ||
                slotIndex < 0 ||
                !TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out _))
            {
                return RecordActivationResult(InputOrderActivationResult.Rejected(
                    context.TargetEntity,
                    OrderSubmitResult.RejectedValidation));
            }

            BuildAggregatedSlots(handle, config, Span<EntityCommandPanelSlotView>.Empty, updateActivationMap: true);
            if ((uint)slotIndex >= (uint)MaxAggregatedSlots)
            {
                return RecordActivationResult(InputOrderActivationResult.Rejected(
                    context.TargetEntity,
                    OrderSubmitResult.RejectedValidation));
            }

            int start = _memberStarts[slotIndex];
            int count = _ownerCounts[slotIndex];
            if (count <= 0 ||
                start < 0 ||
                start + count > _memberScratch.Length)
            {
                return RecordActivationResult(InputOrderActivationResult.Rejected(
                    context.TargetEntity,
                    OrderSubmitResult.RejectedValidation));
            }

            InputOrderActivationResult firstAccepted = default;
            InputOrderActivationResult firstRejected = default;
            bool hasAccepted = false;
            bool hasRejected = false;

            if (count > 1 &&
                TryFindAimingMember(start, count, out Entity aimingActor))
            {
                return RecordActivationResult(InputOrderActivationResult.Rejected(
                    aimingActor,
                    OrderSubmitResult.RejectedByRule));
            }

            for (int i = 0; i < count; i++)
            {
                EntityCommandPanelAggregationMember member = _memberScratch[start + i];
                InputOrderActivationResult result = _gasSource.ActivateSlot(member.Owner, groupIndex, member.SlotIndex);
                if (result.State == InputOrderActivationState.Rejected)
                {
                    if (!hasRejected)
                    {
                        firstRejected = result;
                        hasRejected = true;
                    }

                    continue;
                }

                if (!hasAccepted)
                {
                    firstAccepted = result;
                    hasAccepted = true;
                }

                if (result.State == InputOrderActivationState.EnteredAiming)
                {
                    break;
                }
            }

            if (hasRejected)
            {
                return RecordActivationResult(firstRejected);
            }

            return RecordActivationResult(hasAccepted
                ? firstAccepted
                : InputOrderActivationResult.Rejected(
                    context.TargetEntity,
                    OrderSubmitResult.RejectedValidation));
        }

        private InputOrderActivationResult RecordActivationResult(in InputOrderActivationResult result)
        {
            InputOrderMappingSystem? mapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            return mapping != null
                ? mapping.RecordExternalActivationResult(in result)
                : result;
        }

        private bool TryFindAimingMember(int start, int count, out Entity actor)
        {
            for (int i = 0; i < count; i++)
            {
                EntityCommandPanelAggregationMember member = _memberScratch[start + i];
                if (_gasSource.WouldEnterUiAiming(member.Owner, member.SlotIndex))
                {
                    actor = member.Owner;
                    return true;
                }
            }

            actor = Entity.Null;
            return false;
        }

        public int CopyAggregationMembers(
            in EntityCommandPanelSourceContext context,
            int groupIndex,
            int slotIndex,
            Span<EntityCommandPanelAggregationMember> destination)
        {
            if (groupIndex != 0 ||
                slotIndex < 0 ||
                destination.IsEmpty ||
                !TryResolveCollection(context, out EntityCommandPanelCollectionQueryConfig config, out EntityCollectionHandle handle, out _))
            {
                return 0;
            }

            BuildAggregatedSlots(handle, config, Span<EntityCommandPanelSlotView>.Empty, updateActivationMap: true);
            if ((uint)slotIndex >= (uint)MaxAggregatedSlots)
            {
                return 0;
            }

            int start = _memberStarts[slotIndex];
            int count = _ownerCounts[slotIndex];
            if (count <= 0 || start < 0)
            {
                return 0;
            }

            int written = Math.Min(destination.Length, count);
            for (int i = 0; i < written; i++)
            {
                destination[i] = _memberScratch[start + i];
            }

            return written;
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

        public InputOrderActivationResult ActivateSlot(Entity target, int groupIndex, int slotIndex) =>
            InputOrderActivationResult.Rejected(target, OrderSubmitResult.RejectedByRule);

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

        /// <summary>
        /// One kernel group = one panel cell: <see cref="AbilityAggregationProfileRegistry.BuildGroups"/>
        /// decides membership, this method only composes the per-cell view from the members' slot
        /// views (query filter, flags OR, lockout max, badge count) and maps activation to the
        /// group's surviving members.
        /// </summary>
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
                _memberTotal = 0;
                Array.Clear(_memberStarts, 0, _memberStarts.Length);
            }

            Array.Clear(_ownerCounts, 0, _ownerCounts.Length);
            _ownerIndexByEntityId.Clear();
            int ownerCount = CopyOwners(handle, _ownerScratch);
            for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
            {
                _ownerViewCounts[ownerIndex] = _gasSource.CopySlots(
                    _ownerScratch[ownerIndex], 0, _ownerViews.AsSpan(ownerIndex * OwnerViewStride, OwnerViewStride));
                _ownerIndexByEntityId[_ownerScratch[ownerIndex].Id] = ownerIndex;
            }

            int groupCount = _aggregationProfiles.BuildGroups(
                _profileId, _ownerScratch.AsSpan(0, ownerCount), _engine.World, _abilityDefinitions, ref _aggregationResult);

            int aggregateCount = 0;
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                if (aggregateCount >= MaxAggregatedSlots)
                {
                    // Overflow handling beyond the panel capacity is the router's concern (PNL-3).
                    break;
                }

                ReadOnlySpan<Entity> members = _aggregationResult.GroupEntities(groupIndex);
                ReadOnlySpan<int> memberSlotIndices = _aggregationResult.GroupSlotIndices(groupIndex);
                int memberCount = 0;
                EntityCommandPanelSlotView view = default;
                Entity activationOwner = Entity.Null;
                int activationSlotIndex = 0;
                int memberStart = _memberTotal;
                for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
                {
                    if (!TryGetOwnerView(members[memberIndex], memberSlotIndices[memberIndex], out EntityCommandPanelSlotView memberView) ||
                        HasState(memberView.StateFlags, EntityCommandSlotStateFlags.Empty) ||
                        !MatchesFilter(memberView, config.Filter))
                    {
                        continue;
                    }

                    if (memberCount == 0)
                    {
                        view = memberView;
                        activationOwner = members[memberIndex];
                        activationSlotIndex = memberSlotIndices[memberIndex];
                    }
                    else
                    {
                        view = ComposeGroupView(in view, in memberView);
                    }

                    if (updateActivationMap)
                    {
                        if (_memberTotal >= _memberScratch.Length)
                        {
                            throw new InvalidOperationException(
                                $"ENTITY_COMMAND_PANEL.ERR.MemberScratchCapacity: required={_memberTotal + 1}, capacity={_memberScratch.Length}.");
                        }

                        _memberScratch[_memberTotal++] = new EntityCommandPanelAggregationMember(
                            members[memberIndex],
                            memberSlotIndices[memberIndex]);
                    }

                    memberCount++;
                }

                if (memberCount == 0)
                {
                    continue;
                }

                long groupKey = _aggregationResult.GetGroupKey(groupIndex);
                string displayLabel = ResolveAggregateDisplayLabel(groupKey, view.DisplayLabel);
                int aggregateIndex = aggregateCount++;
                _aggregatedSlots[aggregateIndex] = WithDisplaySlotIndexLabelAndDetail(
                    in view,
                    aggregateIndex,
                    displayLabel,
                    view.DetailLabel);
                _ownerCounts[aggregateIndex] = memberCount;
                if (updateActivationMap)
                {
                    _activationOwners[aggregateIndex] = activationOwner;
                    _activationSlotIndices[aggregateIndex] = activationSlotIndex;
                    _memberStarts[aggregateIndex] = memberStart;
                }
            }

            SortAggregates(aggregateCount, config.Sort);
            int written = Math.Min(destination.Length, aggregateCount);
            for (int i = 0; i < written; i++)
            {
                destination[i] = WithDisplaySlotIndexLabelAndDetail(
                    _aggregatedSlots[i],
                    i,
                    _aggregatedSlots[i].DisplayLabel,
                    ResolveAggregateDetail(i));
            }

            return destination.IsEmpty ? aggregateCount : written;
        }

        /// <summary>
        /// Slot view of one kernel group member, from the per-owner views cached during this build.
        /// False when the slot is outside the owner's displayed slot range (e.g. hidden by a failed
        /// show requirement).
        /// </summary>
        private bool TryGetOwnerView(Entity owner, int ownerSlotIndex, out EntityCommandPanelSlotView view)
        {
            if (_ownerIndexByEntityId.TryGetValue(owner.Id, out int ownerIndex) &&
                _ownerScratch[ownerIndex] == owner &&
                (uint)ownerSlotIndex < (uint)_ownerViewCounts[ownerIndex])
            {
                view = _ownerViews[ownerIndex * OwnerViewStride + ownerSlotIndex];
                return true;
            }

            view = default;
            return false;
        }

        /// <summary>Group view synthesis: representative icon/label stays, flags OR, lockout max.</summary>
        private static EntityCommandPanelSlotView ComposeGroupView(
            in EntityCommandPanelSlotView representative,
            in EntityCommandPanelSlotView next)
        {
            return new EntityCommandPanelSlotView(
                representative.SlotIndex,
                representative.AbilityId,
                representative.TemplateEntityId,
                representative.StateFlags | next.StateFlags,
                Math.Max(representative.LockoutPermille, next.LockoutPermille),
                representative.ChargesCurrent,
                representative.ChargesMax,
                representative.DisplayLabel,
                next.DetailLabel,
                representative.ActionId);
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
                int memberStart = _memberStarts[i];
                int j = i - 1;
                while (j >= 0 && CompareAggregate(_aggregatedSlots[j], _ownerCounts[j], slot, ownerCount, sortKind) > 0)
                {
                    _aggregatedSlots[j + 1] = _aggregatedSlots[j];
                    _ownerCounts[j + 1] = _ownerCounts[j];
                    _activationOwners[j + 1] = _activationOwners[j];
                    _activationSlotIndices[j + 1] = _activationSlotIndices[j];
                    _memberStarts[j + 1] = _memberStarts[j];
                    j--;
                }

                _aggregatedSlots[j + 1] = slot;
                _ownerCounts[j + 1] = ownerCount;
                _activationOwners[j + 1] = activationOwner;
                _activationSlotIndices[j + 1] = activationSlot;
                _memberStarts[j + 1] = memberStart;
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

        private static string ResolveAggregateDisplayLabel(long groupKey, string fallback)
        {
            int kind = (int)(groupKey >> 32);
            if (kind != AbilityAggregationKeyKinds.CatalogCategory)
            {
                return fallback;
            }

            string categoryName = AbilityCategoryRegistry.GetName((int)(uint)groupKey);
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return fallback;
            }

            int dot = categoryName.LastIndexOf('.');
            string leaf = dot >= 0 && dot + 1 < categoryName.Length ? categoryName[(dot + 1)..] : categoryName;
            if (leaf.Length == 0)
            {
                return fallback;
            }

            leaf = leaf.Replace('_', ' ');
            return string.Concat(char.ToUpperInvariant(leaf[0]), leaf.Length == 1 ? string.Empty : leaf[1..]);
        }

        private static EntityCommandPanelSlotView WithDisplaySlotIndexLabelAndDetail(
            in EntityCommandPanelSlotView slot,
            int displaySlotIndex,
            string displayLabel,
            string detailLabel)
        {
            return new EntityCommandPanelSlotView(
                displaySlotIndex,
                slot.AbilityId,
                slot.TemplateEntityId,
                slot.StateFlags,
                slot.LockoutPermille,
                slot.ChargesCurrent,
                slot.ChargesMax,
                displayLabel,
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
