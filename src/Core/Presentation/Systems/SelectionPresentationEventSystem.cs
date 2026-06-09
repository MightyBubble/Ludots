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
        private const int InitialContainerCapacity = 32;
        private const int InitialMemberCapacity = 128;
        private const int InitialStaleKeyCapacity = 16;

        private readonly SelectionRuntime _selection;
        private readonly PresentationEventStream _events;
        private readonly List<Entity> _containers = new(InitialContainerCapacity);
        private readonly List<Entity> _members = new(InitialMemberCapacity);
        private readonly Dictionary<ContainerKey, ContainerSnapshot> _snapshots = new();
        private readonly HashSet<Entity> _currentMembers = new();
        private readonly List<ContainerKey> _staleKeys = new(InitialStaleKeyCapacity);
        private Entity[] _selectionCopyBuffer = Array.Empty<Entity>();

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
            EnsureSelectionCopyCapacity(count);
            int written = count > 0
                ? _selection.CopySelection(descriptor.Container, _selectionCopyBuffer.AsSpan(0, count))
                : 0;
            for (int i = 0; i < written; i++)
            {
                Entity target = _selectionCopyBuffer[i];
                if (target != Entity.Null && World.IsAlive(target))
                {
                    _members.Add(target);
                }
            }

            _currentMembers.Clear();
            _currentMembers.EnsureCapacity(_members.Count);
            for (int i = 0; i < _members.Count; i++)
            {
                _currentMembers.Add(_members[i]);
            }

            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    Entity oldMember = previous.GetMember(i);
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

            if (previous != null)
            {
                previous.Replace(descriptor.Revision, setKeyId, _members);
            }
            else
            {
                _snapshots.Add(key, new ContainerSnapshot(descriptor.Revision, setKeyId, _members));
            }
        }

        private void PublishRemoved(ContainerSnapshot snapshot, Entity container)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                PublishOne(PresentationEventKind.SelectionMemberRemoved, snapshot.GetMember(i), container, snapshot.SetKeyId);
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

        private void EnsureSelectionCopyCapacity(int count)
        {
            if (_selectionCopyBuffer.Length >= count)
            {
                return;
            }

            int nextCapacity = Math.Max(
                count,
                _selectionCopyBuffer.Length == 0 ? InitialMemberCapacity : _selectionCopyBuffer.Length * 2);
            Array.Resize(ref _selectionCopyBuffer, nextCapacity);
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
            private readonly HashSet<Entity> _memberSet = new();
            private Entity[] _members = Array.Empty<Entity>();

            public ContainerSnapshot(uint revision, int setKeyId, List<Entity> members)
            {
                Replace(revision, setKeyId, members);
            }

            public uint Revision { get; private set; }
            public int SetKeyId { get; private set; }
            public int Count { get; private set; }

            public bool Contains(Entity member)
            {
                return _memberSet.Contains(member);
            }

            public Entity GetMember(int index)
            {
                return _members[index];
            }

            public void Replace(uint revision, int setKeyId, List<Entity> members)
            {
                Revision = revision;
                SetKeyId = setKeyId;
                Count = members.Count;
                EnsureMemberCapacity(members.Count);
                _memberSet.Clear();
                _memberSet.EnsureCapacity(members.Count);

                for (int i = 0; i < members.Count; i++)
                {
                    Entity member = members[i];
                    _members[i] = member;
                    _memberSet.Add(member);
                }
            }

            private void EnsureMemberCapacity(int count)
            {
                if (_members.Length >= count)
                {
                    return;
                }

                int nextCapacity = Math.Max(
                    count,
                    _members.Length == 0 ? InitialMemberCapacity : _members.Length * 2);
                Array.Resize(ref _members, nextCapacity);
            }
        }
    }
}
