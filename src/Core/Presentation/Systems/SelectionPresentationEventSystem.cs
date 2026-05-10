using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Bridges formal selection set mutations into the presentation event stream.
    /// SelectionRuntime remains the gameplay SSOT; performer rules consume these facts.
    /// </summary>
    public sealed class SelectionPresentationEventSystem : BaseSystem<World, float>
    {
        private readonly SelectionRuntime _selection;
        private readonly PresentationEventStream _events;
        private readonly List<Entity> _containers = new(32);
        private readonly List<Entity> _members = new(128);
        private readonly Dictionary<ContainerKey, ContainerSnapshot> _snapshots = new();
        private readonly HashSet<Entity> _currentMembers = new();
        private readonly List<ContainerKey> _staleKeys = new(16);

        public SelectionPresentationEventSystem(World world, SelectionRuntime selection, PresentationEventStream events)
            : base(world)
        {
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public override void Update(in float dt)
        {
            _selection.CopyContainerEntities(_containers);
            _staleKeys.Clear();
            foreach (ContainerKey key in _snapshots.Keys)
            {
                _staleKeys.Add(key);
            }

            for (int i = 0; i < _containers.Count; i++)
            {
                Entity container = _containers[i];
                if (!_selection.TryDescribeContainer(container, out SelectionContainerDescriptor descriptor) ||
                    !_selection.TryGetSetKeyId(descriptor.SetKey, out int setKeyId))
                {
                    continue;
                }

                var key = new ContainerKey(container);
                _staleKeys.Remove(key);
                if (_snapshots.TryGetValue(key, out ContainerSnapshot? snapshot) &&
                    snapshot.Revision == descriptor.Revision)
                {
                    continue;
                }

                PublishDiff(key, descriptor, setKeyId, snapshot);
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                ContainerKey key = _staleKeys[i];
                if (_snapshots.TryGetValue(key, out ContainerSnapshot? snapshot))
                {
                    PublishRemoved(snapshot, key.Container);
                    _snapshots.Remove(key);
                }
            }
        }

        private void PublishDiff(
            ContainerKey key,
            in SelectionContainerDescriptor descriptor,
            int setKeyId,
            ContainerSnapshot? previous)
        {
            _members.Clear();
            int count = _selection.GetSelectionCount(descriptor.Container);
            EnsureMemberCapacity(count);
            Entity[] buffer = count <= 0 ? Array.Empty<Entity>() : new Entity[count];
            int written = count > 0 ? _selection.CopySelection(descriptor.Container, buffer) : 0;
            for (int i = 0; i < written; i++)
            {
                Entity target = buffer[i];
                if (target != Entity.Null && World.IsAlive(target))
                {
                    _members.Add(target);
                }
            }

            _currentMembers.Clear();
            for (int i = 0; i < _members.Count; i++)
            {
                _currentMembers.Add(_members[i]);
            }

            if (previous != null)
            {
                for (int i = 0; i < previous.Members.Length; i++)
                {
                    Entity oldMember = previous.Members[i];
                    if (!_currentMembers.Contains(oldMember))
                    {
                        PublishOne(PresentationEventKind.SelectionMemberRemoved, oldMember, descriptor.Container, previous.SetKeyId);
                    }
                }
            }

            for (int i = 0; i < _members.Count; i++)
            {
                Entity member = _members[i];
                if (previous == null || !previous.Contains(member))
                {
                    PublishOne(PresentationEventKind.SelectionMemberAdded, member, descriptor.Container, setKeyId);
                }
            }

            _snapshots[key] = new ContainerSnapshot(descriptor.Revision, setKeyId, _members.ToArray());
        }

        private void PublishRemoved(ContainerSnapshot snapshot, Entity container)
        {
            for (int i = 0; i < snapshot.Members.Length; i++)
            {
                PublishOne(PresentationEventKind.SelectionMemberRemoved, snapshot.Members[i], container, snapshot.SetKeyId);
            }
        }

        private void PublishOne(PresentationEventKind kind, Entity member, Entity container, int setKeyId)
        {
            if (!World.IsAlive(member))
            {
                return;
            }

            int stableId = World.Has<PresentationStableId>(member)
                ? World.Get<PresentationStableId>(member).Value
                : 0;

            if (!_events.TryAdd(new PresentationEvent
            {
                Kind = kind,
                KeyId = setKeyId,
                Source = member,
                Target = container,
                PayloadA = stableId,
            }))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing selection presentation events.");
            }
        }

        private void EnsureMemberCapacity(int count)
        {
            if (_members.Capacity < count)
            {
                _members.Capacity = count;
            }
        }

        private readonly struct ContainerKey : IEquatable<ContainerKey>
        {
            public readonly Entity Container;
            private readonly int _id;
            private readonly int _worldId;
            private readonly int _version;

            public ContainerKey(Entity container)
            {
                Container = container;
                _id = container.Id;
                _worldId = container.WorldId;
                _version = container.Version;
            }

            public bool Equals(ContainerKey other)
            {
                return _id == other._id &&
                       _worldId == other._worldId &&
                       _version == other._version;
            }

            public override bool Equals(object? obj)
            {
                return obj is ContainerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_id, _worldId, _version);
            }
        }

        private sealed class ContainerSnapshot
        {
            private readonly HashSet<Entity> _memberSet;

            public ContainerSnapshot(uint revision, int setKeyId, Entity[] members)
            {
                Revision = revision;
                SetKeyId = setKeyId;
                Members = members;
                _memberSet = new HashSet<Entity>(members);
            }

            public uint Revision { get; }
            public int SetKeyId { get; }
            public Entity[] Members { get; }

            public bool Contains(Entity member)
            {
                return _memberSet.Contains(member);
            }
        }
    }
}
