using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// Projects a CommandDeck profile into a DataPlane snapshot. Wraps existing
    /// <see cref="IEntityCommandPanelSource"/> / collection aggregation; does not scan entities in Web
    /// and does not own a parallel command truth store.
    /// </summary>
    public sealed class CommandDeckProjector
    {
        private const int MaxSlots = 64;
        private const int MaxRouteMembers = 256;

        private readonly IEntityCommandPanelSourceRegistry _sources;
        private readonly EntityCollectionStore? _collections;
        private readonly ControlPlaneView? _controlPlane;
        private readonly StringIntRegistry? _collectionKeys;
        private readonly CommandDeckRouteResolver? _routeResolver;
        private readonly FilterProfileRegistry? _filterProfiles;
        private readonly EntityCommandPanelSlotView[] _slotScratch = new EntityCommandPanelSlotView[MaxSlots];
        private readonly CommandDeckRouteMember[] _routeMemberScratch = new CommandDeckRouteMember[MaxRouteMembers];
        private readonly EntityCommandPanelAggregationMember[] _aggregationMemberScratch =
            new EntityCommandPanelAggregationMember[MaxRouteMembers];
        private readonly Entity[] _controlPlaneScratch = new Entity[MaxRouteMembers];
        private readonly Entity[] _filterRawScratch = new Entity[MaxRouteMembers];
        private readonly Entity[] _filterOutScratch = new Entity[MaxRouteMembers];

        public CommandDeckProjector(
            IEntityCommandPanelSourceRegistry sources,
            EntityCollectionStore? collections = null,
            ControlPlaneView? controlPlane = null,
            StringIntRegistry? collectionKeys = null,
            CommandDeckRouteResolver? routeResolver = null,
            FilterProfileRegistry? filterProfiles = null)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _collections = collections;
            _controlPlane = controlPlane;
            _collectionKeys = collectionKeys;
            _routeResolver = routeResolver;
            _filterProfiles = filterProfiles;
        }

        public CommandDeckSnapshot Project(
            CommandDeckProfile profile,
            in CommandDeckBindingContext binding,
            World? worldForRouting = null,
            Vector3 routeTargetWorldCm = default)
        {
            ArgumentNullException.ThrowIfNull(profile);

            bool visible = EvaluateVisibility(profile, in binding);
            if (!visible)
            {
                uint hiddenRevision = HashCombine(2166136261u, HashString(profile.Id));
                hiddenRevision = HashCombine(hiddenRevision, 0u);
                return new CommandDeckSnapshot(profile.Id, profile.DisplayMode, hiddenRevision == 0 ? 1u : hiddenRevision, false, Array.Empty<CommandDeckEntry>());
            }

            if (!_sources.TryGet(profile.CommandPanelSourceId, out IEntityCommandPanelSource source))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' references unknown command panel source '{profile.CommandPanelSourceId}'.");
            }

            EntityCommandPanelSourceContext sourceContext = ResolveSourceContext(profile, in binding);
            sourceContext = ApplyFilterProfileToCollection(profile, in binding, in sourceContext);

            if (!EntityCommandPanelSourceDispatch.TryGetRevision(source, in sourceContext, out uint sourceRevision))
            {
                sourceRevision = 1u;
            }

            uint revision = HashCombine(sourceRevision, HashString(profile.Id));
            revision = HashCombine(revision, (uint)profile.DisplayMode);
            revision = HashCombine(revision, binding.VisibilityConditionSatisfied ? 1u : 0u);
            revision = HashCombine(revision, HashString(profile.FilterProfileId));

            int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in sourceContext);
            if (groupCount <= 0)
            {
                return new CommandDeckSnapshot(profile.Id, profile.DisplayMode, revision == 0 ? 1u : revision, true, Array.Empty<CommandDeckEntry>());
            }

            int slotCount = EntityCommandPanelSourceDispatch.CopySlots(source, in sourceContext, 0, _slotScratch);
            var entries = new List<CommandDeckEntry>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = _slotScratch[i];
                if ((slot.StateFlags & EntityCommandSlotStateFlags.Empty) != 0)
                {
                    continue;
                }

                int ownerCount = ParseOwnerCount(slot.DetailLabel);
                string status = ResolveStatus(slot.StateFlags);
                string blockedReason = ResolveBlockedReason(slot);
                string categoryId = ResolveCategory(profile, slot);

                CommandDeckRouteTarget route = ResolveRoute(
                    profile,
                    in binding,
                    in sourceContext,
                    source,
                    slot,
                    worldForRouting,
                    routeTargetWorldCm,
                    groupKey: AbilityAggregationKeyKinds.MakeKey(AbilityAggregationKeyKinds.AbilityId, slot.AbilityId));

                entries.Add(new CommandDeckEntry(
                    slot.SlotIndex,
                    slot.AbilityId,
                    slot.ActionId,
                    slot.DisplayLabel,
                    categoryId,
                    status,
                    blockedReason,
                    ownerCount,
                    profile.RouteProfileId,
                    route.Owner.Id,
                    route.Owner.Version,
                    route.SlotIndex,
                    slot.CooldownPermille,
                    slot.ChargesCurrent,
                    slot.ChargesMax));
            }

            revision = HashCombine(revision, (uint)entries.Count);
            return new CommandDeckSnapshot(
                profile.Id,
                profile.DisplayMode,
                revision == 0 ? 1u : revision,
                true,
                entries);
        }

        private EntityCommandPanelSourceContext ResolveSourceContext(
            CommandDeckProfile profile,
            in CommandDeckBindingContext binding)
        {
            switch (profile.SourceKind)
            {
                case CommandDeckSourceKind.ExplicitEntity:
                {
                    if (binding.FocusedEntity == Entity.Null)
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{profile.Id}' entity mode requires an explicit focused entity in the binding context.");
                    }

                    return new EntityCommandPanelSourceContext(
                        binding.FocusedEntity,
                        profile.CommandPanelSourceId,
                        binding.InstanceKey);
                }

                case CommandDeckSourceKind.SolePossessedRep:
                case CommandDeckSourceKind.EntityCollection:
                {
                    Entity owner = ResolveCollectionOwner(profile, in binding);
                    string instanceKey = string.IsNullOrWhiteSpace(binding.InstanceKey)
                        ? profile.SourceRef
                        : binding.InstanceKey;
                    return new EntityCommandPanelSourceContext(owner, profile.CommandPanelSourceId, instanceKey);
                }

                case CommandDeckSourceKind.ControlPlaneView:
                {
                    Entity owner = ResolveCollectionOwner(profile, in binding);
                    string materializationKey = EnsureControlPlaneMaterialized(profile, owner);
                    return new EntityCommandPanelSourceContext(owner, profile.CommandPanelSourceId, materializationKey);
                }

                default:
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{profile.Id}' has unsupported sourceKind '{profile.SourceKind}'.");
            }
        }

        private Entity ResolveCollectionOwner(CommandDeckProfile profile, in CommandDeckBindingContext binding)
        {
            if (binding.CollectionOwner != Entity.Null)
            {
                return binding.CollectionOwner;
            }

            if (binding.SolePossessedRep != Entity.Null)
            {
                return binding.SolePossessedRep;
            }

            throw new InvalidOperationException(
                $"CommandDeck profile '{profile.Id}' requires a sole possessed rep or collection owner in the binding context.");
        }

        /// <summary>
        /// Materialize ControlPlaneView members into a dedicated collection key so domain
        /// <c>collection.command.source</c> rows are not overwritten. Returns the materialization key
        /// used as the panel source instanceKey.
        /// </summary>
        private string EnsureControlPlaneMaterialized(CommandDeckProfile profile, Entity owner)
        {
            if (_controlPlane == null || _collections == null || _collectionKeys == null)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' controlPlaneView source requires ControlPlaneView, EntityCollectionStore, and collection key registry.");
            }

            if (!_collectionKeys.TryGetId(profile.SourceRef, out int collectionKeyId))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' controlPlaneView sourceRef '{profile.SourceRef}' is not a registered collection key.");
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
        /// When <see cref="CommandDeckProfile.FilterProfileId"/> is set, evaluate the FilterProfile
        /// against the current collection members and materialize survivors into
        /// <see cref="EntityViewKeys.CommandDeckFiltered"/> so the reused EntityCommandPanel /
        /// CollectionGas source only sees the filtered view. The original sourceRef / control-plane
        /// collection is never overwritten. Missing filter ids fail fast.
        /// </summary>
        private EntityCommandPanelSourceContext ApplyFilterProfileToCollection(
            CommandDeckProfile profile,
            in CommandDeckBindingContext binding,
            in EntityCommandPanelSourceContext sourceContext)
        {
            if (string.IsNullOrWhiteSpace(profile.FilterProfileId))
            {
                return sourceContext;
            }

            if (_filterProfiles == null)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' references filterProfileId '{profile.FilterProfileId}' but FilterProfileRegistry was not supplied to the projector.");
            }

            if (_collections == null || _collectionKeys == null)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' filterProfileId '{profile.FilterProfileId}' requires EntityCollectionStore and collection key registry.");
            }

            if (!_filterProfiles.ProfileIdRegistry.TryGetId(profile.FilterProfileId, out int filterId) ||
                !_filterProfiles.IsInstalled(filterId))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' references unknown filter profile '{profile.FilterProfileId}'.");
            }

            string collectionKey = string.IsNullOrWhiteSpace(sourceContext.InstanceKey)
                ? profile.SourceRef
                : sourceContext.InstanceKey;
            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' filterProfileId '{profile.FilterProfileId}' requires a collection key (sourceRef/instanceKey).");
            }

            if (!_collectionKeys.TryGetId(collectionKey, out _))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' collection key '{collectionKey}' is not registered.");
            }

            Entity owner = sourceContext.TargetEntity;
            if (!_collections.TryGet(owner, collectionKey, out EntityCollectionHandle handle))
            {
                // Empty collection: nothing to filter; leave as-is.
                return sourceContext;
            }

            int rawCount = _collections.CopyEntities(handle, 0, _filterRawScratch);
            Entity anchor = binding.SolePossessedRep != Entity.Null
                ? binding.SolePossessedRep
                : owner;
            int filteredCount = _filterProfiles.Evaluate(
                filterId,
                anchor,
                _filterRawScratch.AsSpan(0, rawCount),
                _filterOutScratch.AsSpan(0, Math.Max(rawCount, 1)));

            string materializationKey = EntityViewKeys.CommandDeckFiltered;
            if (!_collectionKeys.TryGetId(materializationKey, out _))
            {
                _collectionKeys.Register(materializationKey);
            }

            var descriptor = EntityCollectionDescriptor.Create(
                materializationKey,
                EntityCollectionSourceKind.CollectionView,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: filteredCount > 0 ? _filterOutScratch[0] : Entity.Null,
                title: profile.Id);
            _collections.Replace(owner, descriptor, _filterOutScratch.AsSpan(0, filteredCount));

            return new EntityCommandPanelSourceContext(
                sourceContext.TargetEntity,
                sourceContext.SourceId,
                materializationKey);
        }

        private CommandDeckRouteTarget ResolveRoute(
            CommandDeckProfile profile,
            in CommandDeckBindingContext binding,
            in EntityCommandPanelSourceContext sourceContext,
            IEntityCommandPanelSource source,
            in EntityCommandPanelSlotView slot,
            World? world,
            Vector3 targetWorldCm,
            long groupKey)
        {
            if (string.IsNullOrWhiteSpace(profile.RouteProfileId))
            {
                Entity owner = profile.SourceKind == CommandDeckSourceKind.ExplicitEntity
                    ? binding.FocusedEntity
                    : ResolveCollectionOwner(profile, in binding);
                return new CommandDeckRouteTarget(owner, slot.SlotIndex);
            }

            if (_routeResolver == null || world == null)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' declares routeProfileId '{profile.RouteProfileId}' but route resolver/world was not supplied.");
            }

            int memberCount = CopyRouteMembers(profile, in sourceContext, source, slot);
            return _routeResolver.Resolve(
                profile.RouteProfileId,
                _routeMemberScratch.AsSpan(0, memberCount),
                world,
                targetWorldCm,
                groupKey);
        }

        private int CopyRouteMembers(
            CommandDeckProfile profile,
            in EntityCommandPanelSourceContext sourceContext,
            IEntityCommandPanelSource source,
            in EntityCommandPanelSlotView slot)
        {
            if (EntityCommandPanelSourceDispatch.TryCopyAggregationMembers(
                    source,
                    in sourceContext,
                    groupIndex: 0,
                    slot.SlotIndex,
                    _aggregationMemberScratch,
                    out int written) &&
                written > 0)
            {
                int count = Math.Min(written, MaxRouteMembers);
                for (int i = 0; i < count; i++)
                {
                    EntityCommandPanelAggregationMember member = _aggregationMemberScratch[i];
                    _routeMemberScratch[i] = new CommandDeckRouteMember(member.Owner, member.SlotIndex);
                }

                return count;
            }

            if (profile.DisplayMode == CommandDeckDisplayMode.AggregateFiltered)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' aggregateFiltered slot {slot.SlotIndex} requires an aggregation member source that exposes members for route profile '{profile.RouteProfileId}'.");
            }

            // Non-aggregate sources with a route profile: single explicit owner.
            _routeMemberScratch[0] = new CommandDeckRouteMember(sourceContext.TargetEntity, slot.SlotIndex);
            return 1;
        }

        /// <summary>
        /// Resolve activation for an explicit aggregation member set (contract tests / panel router).
        /// Does not silently pick the first member when a route profile is declared.
        /// </summary>
        public CommandDeckRouteTarget ResolveActivationRoute(
            CommandDeckProfile profile,
            ReadOnlySpan<CommandDeckRouteMember> members,
            World world,
            Vector3 targetWorldCm,
            long groupKey)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (string.IsNullOrWhiteSpace(profile.RouteProfileId))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' has no routeProfileId; cannot resolve activation route.");
            }

            if (_routeResolver == null)
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{profile.Id}' requires a CommandDeckRouteResolver.");
            }

            return _routeResolver.Resolve(profile.RouteProfileId, members, world, targetWorldCm, groupKey);
        }

        private static bool EvaluateVisibility(CommandDeckProfile profile, in CommandDeckBindingContext binding)
        {
            if (profile.DisplayMode != CommandDeckDisplayMode.ConditionalPinned)
            {
                return true;
            }

            string conditionId = profile.VisibilityConditionId;
            if (string.IsNullOrWhiteSpace(conditionId) ||
                string.Equals(conditionId, CommandDeckVisibilityConditionIds.Always, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(conditionId, CommandDeckVisibilityConditionIds.Never, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(conditionId, CommandDeckVisibilityConditionIds.BindingFlag, StringComparison.Ordinal))
            {
                return binding.VisibilityConditionSatisfied;
            }

            // Unknown condition ids fail fast — no silent "always show".
            throw new InvalidOperationException(
                $"CommandDeck profile '{profile.Id}' references unknown visibilityConditionId '{conditionId}'.");
        }

        private static string ResolveStatus(EntityCommandSlotStateFlags flags)
        {
            if ((flags & EntityCommandSlotStateFlags.Blocked) != 0)
            {
                return "blocked";
            }

            if ((flags & EntityCommandSlotStateFlags.Active) != 0)
            {
                return "active";
            }

            if ((flags & EntityCommandSlotStateFlags.PendingTarget) != 0)
            {
                return "pendingTarget";
            }

            return "ready";
        }

        private static string ResolveBlockedReason(in EntityCommandPanelSlotView slot)
        {
            if ((slot.StateFlags & EntityCommandSlotStateFlags.Blocked) == 0)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(slot.DetailLabel) ? "blocked" : slot.DetailLabel;
        }

        private static string ResolveCategory(CommandDeckProfile profile, in EntityCommandPanelSlotView slot)
        {
            if (!string.IsNullOrWhiteSpace(profile.CategoryTagPrefix) &&
                !string.IsNullOrWhiteSpace(slot.ActionId) &&
                slot.ActionId.StartsWith(profile.CategoryTagPrefix, StringComparison.Ordinal))
            {
                return slot.ActionId;
            }

            return string.IsNullOrWhiteSpace(slot.ActionId) ? string.Empty : slot.ActionId;
        }

        private static int ParseOwnerCount(string detailLabel)
        {
            if (string.IsNullOrWhiteSpace(detailLabel))
            {
                return 1;
            }

            // Collection aggregation detail format: "{n} owners | ..."
            int ownersIndex = detailLabel.IndexOf(" owners", StringComparison.Ordinal);
            if (ownersIndex <= 0)
            {
                return 1;
            }

            ReadOnlySpan<char> prefix = detailLabel.AsSpan(0, ownersIndex);
            return int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0
                ? count
                : 1;
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
}
