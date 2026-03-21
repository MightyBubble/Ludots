using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionRuntimeConfig
    {
        public int MutationApplyBudgetPerFrame { get; set; } = 4096;
        public float ClickPickRadiusPixels { get; set; } = 20f;
        public float DragThresholdPixels { get; set; } = 8f;
    }

    public sealed class SelectionRuntime
    {
        private readonly World _world;
        private readonly SelectionRuntimeConfig _config;
        private readonly StringIntRegistry _containerAliasRegistry;
        private readonly StringIntRegistry _viewKeyRegistry;
        private readonly Dictionary<SelectionOwnerAliasKey, Entity> _containers = new();
        private readonly Dictionary<SelectionViewKey, Entity> _viewBindings = new();
        private readonly Dictionary<Entity, List<Entity>> _membersByContainer = new();

        public SelectionRuntime(World world, SelectionRuntimeConfig config, StringIntRegistry containerAliasRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _containerAliasRegistry = containerAliasRegistry ?? throw new ArgumentNullException(nameof(containerAliasRegistry));
            _viewKeyRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);

            _containerAliasRegistry.Register(SelectionSetKeys.LivePrimary);
            _containerAliasRegistry.Register(SelectionSetKeys.FormationPrimary);
            _containerAliasRegistry.Register(SelectionSetKeys.CommandPreview);
            _containerAliasRegistry.Register(SelectionSetKeys.CommandSnapshot);
            _viewKeyRegistry.Register(SelectionViewKeys.Primary);
            _viewKeyRegistry.Register(SelectionViewKeys.Secondary);
            _viewKeyRegistry.Register(SelectionViewKeys.CommandPreview);
            _viewKeyRegistry.Register(SelectionViewKeys.Formation);
            _viewKeyRegistry.Register(SelectionViewKeys.Debug);
        }

        public SelectionRuntimeConfig Config => _config;
        public StringIntRegistry ContainerAliasRegistry => _containerAliasRegistry;

        public bool TryDescribeSelection(Entity owner, string aliasKey, out SelectionContainerDescriptor descriptor)
        {
            descriptor = default;
            return TryGetSelectionEntity(owner, aliasKey, out Entity container) &&
                   TryDescribeContainer(container, out descriptor);
        }

        public bool TryDescribeContainer(Entity container, out SelectionContainerDescriptor descriptor)
        {
            descriptor = default;
            if (!IsContainerAlive(container))
            {
                return false;
            }

            Entity owner = _world.Get<SelectionContainerOwner>(container).Value;
            int aliasId = _world.Get<SelectionContainerAliasId>(container).Value;
            string aliasKey = _containerAliasRegistry.GetName(aliasId);
            SelectionContainerKind kind = _world.Has<SelectionContainerKindComponent>(container)
                ? _world.Get<SelectionContainerKindComponent>(container).Kind
                : SelectionContainerKind.Live;
            uint revision = _world.Has<SelectionContainerRevision>(container)
                ? _world.Get<SelectionContainerRevision>(container).Value
                : 0u;
            int memberCount = GetSelectionCount(container);
            Entity primary = TryGetPrimary(container, out Entity resolvedPrimary)
                ? resolvedPrimary
                : Entity.Null;
            descriptor = new SelectionContainerDescriptor(
                container,
                owner,
                aliasKey,
                kind,
                revision,
                memberCount,
                primary);
            return true;
        }

        public bool TryDescribeView(Entity viewer, string viewKey, out SelectionViewDescriptor descriptor)
        {
            descriptor = default;
            if (!TryResolveViewContainer(viewer, viewKey, out Entity container) ||
                !TryDescribeContainer(container, out SelectionContainerDescriptor containerDescriptor))
            {
                return false;
            }

            descriptor = new SelectionViewDescriptor(viewer, NormalizeViewKey(viewKey), containerDescriptor);
            return true;
        }

        public bool TryGetSelectionEntity(Entity owner, string aliasKey, out Entity selectionEntity)
        {
            selectionEntity = default;
            if (!_world.IsAlive(owner))
            {
                return false;
            }

            if (!_containerAliasRegistry.TryGetId(NormalizeAlias(aliasKey), out int aliasId) || aliasId <= 0)
            {
                return false;
            }

            return TryResolveContainer(new SelectionOwnerAliasKey(owner, aliasId), out selectionEntity);
        }

        public bool TryGetOrCreateSelectionEntity(Entity owner, string aliasKey, out Entity selectionEntity)
        {
            return TryGetOrCreateContainer(owner, aliasKey, SelectionContainerKind.Live, out selectionEntity);
        }

        public bool TryGetOrCreateContainer(Entity owner, string aliasKey, SelectionContainerKind kind, out Entity container)
        {
            container = default;
            if (!_world.IsAlive(owner))
            {
                return false;
            }

            int aliasId = _containerAliasRegistry.Register(NormalizeAlias(aliasKey));
            var key = new SelectionOwnerAliasKey(owner, aliasId);
            if (TryResolveContainer(key, out container))
            {
                return true;
            }

            container = _world.Create(
                default(SelectionContainerTag),
                new SelectionContainerOwner { Value = owner },
                new SelectionContainerAliasId { Value = aliasId },
                new SelectionContainerKindComponent { Kind = kind },
                new SelectionContainerRevision { Value = 1u },
                new SelectionContainerMemberCount { Value = 0 });
            _containers[key] = container;
            _membersByContainer[container] = new List<Entity>();
            return true;
        }

        public bool TryCloneSelection(Entity sourceOwner, string sourceAliasKey, Entity cloneOwner, string cloneAliasKey, SelectionContainerKind kind, out Entity cloneContainer)
        {
            cloneContainer = default;
            if (!TryGetSelectionEntity(sourceOwner, sourceAliasKey, out Entity sourceContainer) ||
                !TryGetOrCreateContainer(cloneOwner, cloneAliasKey, kind, out cloneContainer))
            {
                return false;
            }

            int count = GetSelectionCount(sourceContainer);
            if (count <= 0)
            {
                return ClearSelection(cloneContainer);
            }

            Entity[] snapshot = new Entity[count];
            int written = CopySelection(sourceContainer, snapshot);
            return ReplaceSelection(cloneContainer, snapshot.AsSpan(0, written));
        }

        public bool TryCreateSnapshotLease(Entity sourceOwner, string sourceAliasKey, string snapshotAliasKey, SelectionContainerKind kind, out Entity leaseOwner, out Entity snapshotContainer)
        {
            leaseOwner = default;
            snapshotContainer = default;

            if (!_world.IsAlive(sourceOwner))
            {
                return false;
            }

            leaseOwner = _world.Create(default(SelectionLeaseOwnerTag));
            if (!TryCloneSelection(sourceOwner, sourceAliasKey, leaseOwner, snapshotAliasKey, kind, out snapshotContainer))
            {
                if (_world.IsAlive(leaseOwner))
                {
                    _world.Destroy(leaseOwner);
                }

                leaseOwner = default;
                return false;
            }

            if (_world.Has<SelectionLeaseContainer>(leaseOwner))
            {
                ref var lease = ref _world.Get<SelectionLeaseContainer>(leaseOwner);
                lease.Value = snapshotContainer;
                _world.Set(leaseOwner, lease);
            }
            else
            {
                _world.Add(leaseOwner, new SelectionLeaseContainer { Value = snapshotContainer });
            }

            return true;
        }

        public bool ReplaceSelection(Entity owner, string aliasKey, ReadOnlySpan<Entity> next)
        {
            return TryGetOrCreateSelectionEntity(owner, aliasKey, out Entity container) &&
                   ReplaceSelection(container, next);
        }

        public bool ReplaceSelection(Entity container, ReadOnlySpan<Entity> next)
        {
            if (!IsContainerAlive(container))
            {
                return false;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            bool changed = members.Count > 0;
            for (int i = 0; i < members.Count; i++)
            {
                Entity relation = members[i];
                if (_world.IsAlive(relation))
                {
                    _world.Destroy(relation);
                }
            }
            members.Clear();

            int ordinal = 0;
            for (int i = 0; i < next.Length; i++)
            {
                Entity target = next[i];
                if (!_world.IsAlive(target) || ContainsTarget(next, i, target))
                {
                    continue;
                }

                Entity relation = _world.Create(
                    default(SelectionMemberTag),
                    new SelectionMemberContainer { Value = container },
                    new SelectionMemberTarget { Value = target },
                    new SelectionMemberOrdinal { Value = ordinal++ },
                    new SelectionMemberRoleId { Value = 0 });
                members.Add(relation);
                changed = true;
            }

            UpdateContainerMetadata(container, members.Count, changed);
            return true;
        }

        public bool AddToSelection(Entity owner, string aliasKey, Entity target)
        {
            if (!TryGetOrCreateSelectionEntity(owner, aliasKey, out Entity container))
            {
                return false;
            }

            return AddToSelection(container, target);
        }

        public bool AddToSelection(Entity container, Entity target)
        {
            if (!IsContainerAlive(container) || !_world.IsAlive(target))
            {
                return false;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            for (int i = 0; i < members.Count; i++)
            {
                Entity relation = members[i];
                if (_world.IsAlive(relation) &&
                    _world.Has<SelectionMemberTarget>(relation) &&
                    _world.Get<SelectionMemberTarget>(relation).Value == target)
                {
                    return false;
                }
            }

            Entity member = _world.Create(
                default(SelectionMemberTag),
                new SelectionMemberContainer { Value = container },
                new SelectionMemberTarget { Value = target },
                new SelectionMemberOrdinal { Value = members.Count },
                new SelectionMemberRoleId { Value = 0 });
            members.Add(member);
            UpdateContainerMetadata(container, members.Count, changed: true);
            return true;
        }

        public bool RemoveFromSelection(Entity owner, string aliasKey, Entity target)
        {
            return TryGetSelectionEntity(owner, aliasKey, out Entity container) &&
                   RemoveFromSelection(container, target);
        }

        public bool RemoveFromSelection(Entity container, Entity target)
        {
            if (!IsContainerAlive(container))
            {
                return false;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            bool removed = false;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                Entity relation = members[i];
                if (!_world.IsAlive(relation) || !_world.Has<SelectionMemberTarget>(relation))
                {
                    members.RemoveAt(i);
                    continue;
                }

                if (_world.Get<SelectionMemberTarget>(relation).Value != target)
                {
                    continue;
                }

                _world.Destroy(relation);
                members.RemoveAt(i);
                removed = true;
            }

            if (!removed)
            {
                return false;
            }

            NormalizeOrdinals(members);
            UpdateContainerMetadata(container, members.Count, changed: true);
            return true;
        }

        public bool ClearSelection(Entity owner, string aliasKey)
        {
            return TryGetSelectionEntity(owner, aliasKey, out Entity container) &&
                   ClearSelection(container);
        }

        public bool ClearSelection(Entity container)
        {
            if (!IsContainerAlive(container))
            {
                return false;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            if (members.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < members.Count; i++)
            {
                Entity relation = members[i];
                if (_world.IsAlive(relation))
                {
                    _world.Destroy(relation);
                }
            }

            members.Clear();
            UpdateContainerMetadata(container, 0, changed: true);
            return true;
        }

        public int GetSelectionCount(Entity owner, string aliasKey)
        {
            return TryGetSelectionEntity(owner, aliasKey, out Entity container)
                ? GetSelectionCount(container)
                : 0;
        }

        public int GetSelectionCount(Entity container)
        {
            if (!IsContainerAlive(container))
            {
                return 0;
            }

            if (_world.Has<SelectionContainerMemberCount>(container))
            {
                return Math.Max(0, _world.Get<SelectionContainerMemberCount>(container).Value);
            }

            return GetOrCreateMemberList(container).Count;
        }

        public bool TryGetPrimary(Entity owner, string aliasKey, out Entity primary)
        {
            primary = default;
            return TryGetSelectionEntity(owner, aliasKey, out Entity container) &&
                   TryGetPrimary(container, out primary);
        }

        public bool TryGetPrimary(Entity container, out Entity primary)
        {
            primary = default;
            if (!IsContainerAlive(container))
            {
                return false;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            for (int i = 0; i < members.Count; i++)
            {
                Entity relation = members[i];
                if (!_world.IsAlive(relation) || !_world.Has<SelectionMemberTarget>(relation))
                {
                    continue;
                }

                Entity target = _world.Get<SelectionMemberTarget>(relation).Value;
                if (!_world.IsAlive(target))
                {
                    continue;
                }

                primary = target;
                return true;
            }

            return false;
        }

        public int CopySelection(Entity owner, string aliasKey, Span<Entity> destination)
        {
            return TryGetSelectionEntity(owner, aliasKey, out Entity container)
                ? CopySelection(container, destination)
                : 0;
        }

        public int CopySelection(Entity container, Span<Entity> destination)
        {
            if (!IsContainerAlive(container) || destination.Length == 0)
            {
                return 0;
            }

            List<Entity> members = GetOrCreateMemberList(container);
            int written = 0;
            for (int i = 0; i < members.Count && written < destination.Length; i++)
            {
                Entity relation = members[i];
                if (!_world.IsAlive(relation) || !_world.Has<SelectionMemberTarget>(relation))
                {
                    continue;
                }

                Entity target = _world.Get<SelectionMemberTarget>(relation).Value;
                if (!_world.IsAlive(target))
                {
                    continue;
                }

                destination[written++] = target;
            }

            return written;
        }

        public bool TryBindView(Entity viewer, string viewKey, Entity owner, string aliasKey)
        {
            return TryGetOrCreateSelectionEntity(owner, aliasKey, out Entity container) &&
                   TryBindView(viewer, viewKey, container);
        }

        public bool TryBindView(Entity viewer, string viewKey, Entity container)
        {
            if (!_world.IsAlive(viewer) || !IsContainerAlive(container))
            {
                return false;
            }

            int viewKeyId = _viewKeyRegistry.Register(NormalizeViewKey(viewKey));
            var key = new SelectionViewKey(viewer, viewKeyId);
            if (TryResolveViewBindingEntity(key, out Entity binding))
            {
                ref var bindingContainer = ref _world.Get<SelectionViewBindingContainer>(binding);
                if (bindingContainer.Value == container)
                {
                    return false;
                }

                bindingContainer.Value = container;
                _world.Set(binding, bindingContainer);
                return true;
            }

            binding = _world.Create(
                default(SelectionViewBindingTag),
                new SelectionViewBindingViewer { Value = viewer },
                new SelectionViewBindingKeyId { Value = viewKeyId },
                new SelectionViewBindingContainer { Value = container });
            _viewBindings[key] = binding;
            return true;
        }

        public bool TryResolveViewContainer(Entity viewer, string viewKey, out Entity container)
        {
            container = default;
            if (!_world.IsAlive(viewer))
            {
                return false;
            }

            if (!_viewKeyRegistry.TryGetId(NormalizeViewKey(viewKey), out int viewKeyId) || viewKeyId <= 0)
            {
                return false;
            }

            return TryResolveViewBindingEntity(new SelectionViewKey(viewer, viewKeyId), out Entity binding) &&
                   _world.Has<SelectionViewBindingContainer>(binding) &&
                   (container = _world.Get<SelectionViewBindingContainer>(binding).Value) != Entity.Null &&
                   IsContainerAlive(container);
        }

        public bool TryGetViewedPrimary(Entity viewer, string viewKey, out Entity primary)
        {
            primary = default;
            return TryResolveViewContainer(viewer, viewKey, out Entity container) &&
                   TryGetPrimary(container, out primary);
        }

        public int GetViewCount(Entity viewer, string viewKey)
        {
            return TryResolveViewContainer(viewer, viewKey, out Entity container)
                ? GetSelectionCount(container)
                : 0;
        }

        public int CopyView(Entity viewer, string viewKey, Span<Entity> destination)
        {
            return TryResolveViewContainer(viewer, viewKey, out Entity container)
                ? CopySelection(container, destination)
                : 0;
        }

        public bool SweepDanglingState()
        {
            bool changed = false;

            var deadContainers = new List<SelectionOwnerAliasKey>();
            foreach (var pair in _containers)
            {
                if (!IsContainerAlive(pair.Value))
                {
                    deadContainers.Add(pair.Key);
                    continue;
                }

                Entity owner = _world.Get<SelectionContainerOwner>(pair.Value).Value;
                if (_world.IsAlive(owner))
                {
                    continue;
                }

                DestroyContainer(pair.Value);
                deadContainers.Add(pair.Key);
                changed = true;
            }

            for (int i = 0; i < deadContainers.Count; i++)
            {
                _containers.Remove(deadContainers[i]);
            }

            foreach (var pair in _membersByContainer)
            {
                Entity container = pair.Key;
                List<Entity> members = pair.Value;
                bool containerChanged = false;

                if (!IsContainerAlive(container))
                {
                    continue;
                }

                for (int i = members.Count - 1; i >= 0; i--)
                {
                    Entity relation = members[i];
                    if (!_world.IsAlive(relation) || !_world.Has<SelectionMemberTarget>(relation))
                    {
                        members.RemoveAt(i);
                        containerChanged = true;
                        continue;
                    }

                    Entity target = _world.Get<SelectionMemberTarget>(relation).Value;
                    if (_world.IsAlive(target))
                    {
                        continue;
                    }

                    _world.Destroy(relation);
                    members.RemoveAt(i);
                    containerChanged = true;
                }

                if (!containerChanged)
                {
                    continue;
                }

                NormalizeOrdinals(members);
                UpdateContainerMetadata(container, members.Count, changed: true);
                changed = true;
            }

            var deadViews = new List<SelectionViewKey>();
            foreach (var pair in _viewBindings)
            {
                if (!TryResolveViewBindingEntity(pair.Key, out Entity binding))
                {
                    deadViews.Add(pair.Key);
                    continue;
                }

                Entity viewer = _world.Get<SelectionViewBindingViewer>(binding).Value;
                Entity container = _world.Get<SelectionViewBindingContainer>(binding).Value;
                if (_world.IsAlive(viewer) && IsContainerAlive(container))
                {
                    continue;
                }

                if (_world.IsAlive(binding))
                {
                    _world.Destroy(binding);
                }

                deadViews.Add(pair.Key);
                changed = true;
            }

            for (int i = 0; i < deadViews.Count; i++)
            {
                _viewBindings.Remove(deadViews[i]);
            }

            return changed;
        }

        private bool TryResolveContainer(in SelectionOwnerAliasKey key, out Entity container)
        {
            container = default;
            if (!_containers.TryGetValue(key, out Entity cached))
            {
                return false;
            }

            if (!IsContainerAlive(cached))
            {
                _containers.Remove(key);
                _membersByContainer.Remove(cached);
                return false;
            }

            container = cached;
            return true;
        }

        private bool TryResolveViewBindingEntity(in SelectionViewKey key, out Entity binding)
        {
            binding = default;
            if (!_viewBindings.TryGetValue(key, out Entity cached))
            {
                return false;
            }

            if (!_world.IsAlive(cached) ||
                !_world.Has<SelectionViewBindingViewer>(cached) ||
                !_world.Has<SelectionViewBindingKeyId>(cached) ||
                !_world.Has<SelectionViewBindingContainer>(cached))
            {
                _viewBindings.Remove(key);
                return false;
            }

            binding = cached;
            return true;
        }

        private bool IsContainerAlive(Entity container)
        {
            return _world.IsAlive(container) &&
                   _world.Has<SelectionContainerTag>(container) &&
                   _world.Has<SelectionContainerOwner>(container) &&
                   _world.Has<SelectionContainerAliasId>(container);
        }

        private List<Entity> GetOrCreateMemberList(Entity container)
        {
            if (!_membersByContainer.TryGetValue(container, out List<Entity>? members))
            {
                members = new List<Entity>();
                _membersByContainer[container] = members;
            }

            return members;
        }

        private void UpdateContainerMetadata(Entity container, int memberCount, bool changed)
        {
            if (!_world.Has<SelectionContainerMemberCount>(container))
            {
                _world.Add(container, new SelectionContainerMemberCount { Value = memberCount });
            }
            else
            {
                ref var count = ref _world.Get<SelectionContainerMemberCount>(container);
                count.Value = memberCount;
                _world.Set(container, count);
            }

            if (!changed)
            {
                return;
            }

            if (!_world.Has<SelectionContainerRevision>(container))
            {
                _world.Add(container, new SelectionContainerRevision { Value = 1u });
                return;
            }

            ref var revision = ref _world.Get<SelectionContainerRevision>(container);
            revision.Value++;
            if (revision.Value == 0)
            {
                revision.Value = 1;
            }
            _world.Set(container, revision);
        }

        private void NormalizeOrdinals(List<Entity> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                Entity relation = members[i];
                if (!_world.IsAlive(relation) || !_world.Has<SelectionMemberOrdinal>(relation))
                {
                    continue;
                }

                ref var ordinal = ref _world.Get<SelectionMemberOrdinal>(relation);
                ordinal.Value = i;
                _world.Set(relation, ordinal);
            }
        }

        private void DestroyContainer(Entity container)
        {
            if (_membersByContainer.TryGetValue(container, out List<Entity>? members))
            {
                for (int i = 0; i < members.Count; i++)
                {
                    Entity relation = members[i];
                    if (_world.IsAlive(relation))
                    {
                        _world.Destroy(relation);
                    }
                }

                _membersByContainer.Remove(container);
            }

            if (_world.IsAlive(container))
            {
                _world.Destroy(container);
            }
        }

        private static string NormalizeAlias(string? aliasKey)
        {
            return string.IsNullOrWhiteSpace(aliasKey) ? SelectionSetKeys.LivePrimary : aliasKey.Trim();
        }

        private static string NormalizeViewKey(string? viewKey)
        {
            return string.IsNullOrWhiteSpace(viewKey) ? SelectionViewKeys.Primary : viewKey.Trim();
        }

        private static bool ContainsTarget(ReadOnlySpan<Entity> next, int uptoExclusive, Entity target)
        {
            for (int i = 0; i < uptoExclusive; i++)
            {
                if (next[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct SelectionOwnerAliasKey : IEquatable<SelectionOwnerAliasKey>
        {
            public SelectionOwnerAliasKey(Entity owner, int aliasId)
            {
                OwnerId = owner.Id;
                OwnerWorldId = owner.WorldId;
                OwnerVersion = owner.Version;
                AliasId = aliasId;
            }

            public int OwnerId { get; }
            public int OwnerWorldId { get; }
            public int OwnerVersion { get; }
            public int AliasId { get; }

            public bool Equals(SelectionOwnerAliasKey other)
            {
                return OwnerId == other.OwnerId &&
                       OwnerWorldId == other.OwnerWorldId &&
                       OwnerVersion == other.OwnerVersion &&
                       AliasId == other.AliasId;
            }

            public override bool Equals(object? obj)
            {
                return obj is SelectionOwnerAliasKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(OwnerId, OwnerWorldId, OwnerVersion, AliasId);
            }
        }

        private readonly struct SelectionViewKey : IEquatable<SelectionViewKey>
        {
            public SelectionViewKey(Entity viewer, int viewKeyId)
            {
                ViewerId = viewer.Id;
                ViewerWorldId = viewer.WorldId;
                ViewerVersion = viewer.Version;
                ViewKeyId = viewKeyId;
            }

            public int ViewerId { get; }
            public int ViewerWorldId { get; }
            public int ViewerVersion { get; }
            public int ViewKeyId { get; }

            public bool Equals(SelectionViewKey other)
            {
                return ViewerId == other.ViewerId &&
                       ViewerWorldId == other.ViewerWorldId &&
                       ViewerVersion == other.ViewerVersion &&
                       ViewKeyId == other.ViewKeyId;
            }

            public override bool Equals(object? obj)
            {
                return obj is SelectionViewKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ViewerId, ViewerWorldId, ViewerVersion, ViewKeyId);
            }
        }
    }
}
