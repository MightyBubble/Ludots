using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Bridges owner-keyed entity collection row diffs into presentation events.
    /// Presenter rules stay responsible for visual lifecycle and asset choice.
    /// </summary>
    public sealed class EntityCollectionPresentationEventSystem : BaseSystem<World, float>
    {
        private const int InitialCollectionCapacity = 64;
        private const int InitialRowCapacity = 128;

        private readonly EntityCollectionStore _collections;
        private readonly PresentationEventStream _events;
        private readonly GameSession? _session;
        private readonly Dictionary<CollectionKey, CollectionSnapshot> _snapshots = new();
        private readonly HashSet<RowKey> _currentRows = new();
        private readonly List<CollectionKey> _staleKeys = new(InitialCollectionCapacity);
        private EntityCollectionHandle[] _handles = new EntityCollectionHandle[InitialCollectionCapacity];
        private Entity[] _entities = new Entity[InitialRowCapacity];
        private int[] _ordinals = new int[InitialRowCapacity];
        private int[] _roleIds = new int[InitialRowCapacity];
        private EntityCollectionRowFlags[] _flags = new EntityCollectionRowFlags[InitialRowCapacity];

        public EntityCollectionPresentationEventSystem(
            World world,
            EntityCollectionStore collections,
            PresentationEventStream events,
            GameSession? session = null)
            : base(world)
        {
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _session = session;
        }

        public override void Update(in float dt)
        {
            EnsureHandleCapacity(_collections.CollectionCount);
            int handleCount = _collections.CopyActiveHandles(_handles);
            _staleKeys.Clear();
            foreach (CollectionKey key in _snapshots.Keys)
            {
                _staleKeys.Add(key);
            }

            for (int i = 0; i < handleCount; i++)
            {
                EntityCollectionHandle handle = _handles[i];
                if (!_collections.TryGetView(handle, out EntityCollectionView view))
                {
                    continue;
                }

                var key = new CollectionKey(view.Owner, view.KeyId);
                _staleKeys.Remove(key);
                if (_snapshots.TryGetValue(key, out CollectionSnapshot? snapshot) &&
                    snapshot.Revision == view.Revision &&
                    snapshot.Signature == view.Signature)
                {
                    continue;
                }

                PublishDiff(handle, in view, key, snapshot);
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                CollectionKey key = _staleKeys[i];
                if (!_snapshots.TryGetValue(key, out CollectionSnapshot? snapshot))
                {
                    continue;
                }

                PublishRemovedRows(snapshot, key);
                _snapshots.Remove(key);
            }
        }

        private void PublishDiff(
            EntityCollectionHandle handle,
            in EntityCollectionView view,
            CollectionKey key,
            CollectionSnapshot? previous)
        {
            EnsureRowCapacity(view.Count);
            int count = view.Count == 0
                ? 0
                : _collections.CopyWindow(
                    handle,
                    0,
                    _entities.AsSpan(0, view.Count),
                    _ordinals.AsSpan(0, view.Count),
                    _roleIds.AsSpan(0, view.Count),
                    _flags.AsSpan(0, view.Count));

            _currentRows.Clear();
            _currentRows.EnsureCapacity(count);
            for (int i = 0; i < count; i++)
            {
                _currentRows.Add(new RowKey(_entities[i]));
            }

            if (previous != null)
            {
                for (int i = 0; i < previous.Count; i++)
                {
                    RowSnapshot oldRow = previous.GetRow(i);
                    RowKey rowKey = oldRow.ToKey();
                    if (!_currentRows.Contains(rowKey))
                    {
                        PublishOne(PresentationEventKind.EntityCollectionMemberRemoved, key, oldRow, previous.Revision);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                var row = new RowSnapshot(_entities[i], _ordinals[i], _roleIds[i], _flags[i]);
                if (previous == null ||
                    !previous.TryGet(row.ToKey(), out RowSnapshot oldRow) ||
                    !oldRow.MetadataEquals(row))
                {
                    PublishOne(PresentationEventKind.EntityCollectionMemberAdded, key, row, view.Revision);
                }
            }

            if (previous != null)
            {
                previous.Replace(view.Revision, view.Signature, _entities, _ordinals, _roleIds, _flags, count);
            }
            else
            {
                _snapshots.Add(
                    key,
                    new CollectionSnapshot(view.Revision, view.Signature, _entities, _ordinals, _roleIds, _flags, count));
            }
        }

        private void PublishRemovedRows(CollectionSnapshot snapshot, CollectionKey key)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                PublishOne(PresentationEventKind.EntityCollectionMemberRemoved, key, snapshot.GetRow(i), snapshot.Revision);
            }
        }

        private void PublishOne(
            PresentationEventKind kind,
            CollectionKey key,
            RowSnapshot row,
            uint revision)
        {
            if (!World.IsAlive(row.Entity))
            {
                return;
            }

            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = kind,
                KeyId = key.KeyId,
                Source = row.Entity,
                Target = key.Owner,
                Viewer = ResolveViewer(key.Owner),
                PayloadA = ComposeCollectionMemberScope(key, row),
                PayloadB = row.RoleId,
                Magnitude = (float)row.Flags,
                FloatA = row.Ordinal,
                FloatB = row.RoleId,
                FloatC = (float)row.Flags,
                FloatD = revision,
                Position = ResolveVisualPosition(row.Entity),
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException("PresentationEventStream is full while publishing entity collection presentation events.");
            }
        }

        private Vector3 ResolveVisualPosition(Entity entity)
        {
            if (World.Has<VisualTransform>(entity))
            {
                return World.Get<VisualTransform>(entity).Position;
            }

            if (World.Has<WorldPositionCm>(entity))
            {
                WorldCmInt2 cm = World.Get<WorldPositionCm>(entity).ToWorldCmInt2();
                return new Vector3(WorldUnits.CmToM(cm.X), 0f, WorldUnits.CmToM(cm.Y));
            }

            return Vector3.Zero;
        }

        private Entity ResolveViewer(Entity owner)
        {
            if (World.IsAlive(owner) && World.Has<AbilityAimSessionState>(owner))
            {
                Entity viewer = World.Get<AbilityAimSessionState>(owner).Viewer;
                if (World.IsAlive(viewer))
                {
                    return viewer;
                }
            }

            return owner;
        }

        private static int ComposeCollectionMemberScope(CollectionKey key, RowSnapshot row)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + key.KeyId;
                hash = (hash * 31) + key.Owner.Id;
                hash = (hash * 31) + key.Owner.WorldId;
                hash = (hash * 31) + key.Owner.Version;
                hash = (hash * 31) + row.Entity.Id;
                hash = (hash * 31) + row.Entity.WorldId;
                hash = (hash * 31) + row.Entity.Version;
                hash &= int.MaxValue;
                return hash == 0 ? key.KeyId : hash;
            }
        }

        private void EnsureHandleCapacity(int required)
        {
            if (_handles.Length >= required)
            {
                return;
            }

            int next = _handles.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _handles, next);
        }

        private void EnsureRowCapacity(int required)
        {
            if (_entities.Length >= required)
            {
                return;
            }

            int next = _entities.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _entities, next);
            Array.Resize(ref _ordinals, next);
            Array.Resize(ref _roleIds, next);
            Array.Resize(ref _flags, next);
        }

        private readonly struct CollectionKey : IEquatable<CollectionKey>
        {
            public readonly Entity Owner;
            public readonly int KeyId;
            private readonly int _ownerId;
            private readonly int _ownerWorldId;
            private readonly int _ownerVersion;

            public CollectionKey(Entity owner, int keyId)
            {
                Owner = owner;
                KeyId = keyId;
                _ownerId = owner.Id;
                _ownerWorldId = owner.WorldId;
                _ownerVersion = owner.Version;
            }

            public bool Equals(CollectionKey other)
            {
                return KeyId == other.KeyId &&
                       _ownerId == other._ownerId &&
                       _ownerWorldId == other._ownerWorldId &&
                       _ownerVersion == other._ownerVersion;
            }

            public override bool Equals(object? obj)
            {
                return obj is CollectionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_ownerId, _ownerWorldId, _ownerVersion, KeyId);
            }
        }

        private readonly struct RowKey : IEquatable<RowKey>
        {
            private readonly int _entityId;
            private readonly int _entityWorldId;
            private readonly int _entityVersion;

            public RowKey(Entity entity)
            {
                _entityId = entity.Id;
                _entityWorldId = entity.WorldId;
                _entityVersion = entity.Version;
            }

            public bool Equals(RowKey other)
            {
                return _entityId == other._entityId &&
                       _entityWorldId == other._entityWorldId &&
                       _entityVersion == other._entityVersion;
            }

            public override bool Equals(object? obj)
            {
                return obj is RowKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_entityId, _entityWorldId, _entityVersion);
            }
        }

        private readonly struct RowSnapshot
        {
            public readonly Entity Entity;
            public readonly int Ordinal;
            public readonly int RoleId;
            public readonly EntityCollectionRowFlags Flags;

            public RowSnapshot(Entity entity, int ordinal, int roleId, EntityCollectionRowFlags flags)
            {
                Entity = entity;
                Ordinal = ordinal;
                RoleId = roleId;
                Flags = flags;
            }

            public RowKey ToKey() => new(Entity);

            public bool MetadataEquals(RowSnapshot other)
            {
                return Entity == other.Entity &&
                       Ordinal == other.Ordinal &&
                       RoleId == other.RoleId &&
                       Flags == other.Flags;
            }
        }

        private sealed class CollectionSnapshot
        {
            private readonly Dictionary<RowKey, RowSnapshot> _rowMap = new();
            private RowSnapshot[] _rows = Array.Empty<RowSnapshot>();

            public CollectionSnapshot(
                uint revision,
                ulong signature,
                Entity[] entities,
                int[] ordinals,
                int[] roleIds,
                EntityCollectionRowFlags[] flags,
                int count)
            {
                Replace(revision, signature, entities, ordinals, roleIds, flags, count);
            }

            public uint Revision { get; private set; }
            public ulong Signature { get; private set; }
            public int Count { get; private set; }

            public RowSnapshot GetRow(int index) => _rows[index];

            public bool TryGet(RowKey key, out RowSnapshot row)
            {
                return _rowMap.TryGetValue(key, out row);
            }

            public void Replace(
                uint revision,
                ulong signature,
                Entity[] entities,
                int[] ordinals,
                int[] roleIds,
                EntityCollectionRowFlags[] flags,
                int count)
            {
                Revision = revision;
                Signature = signature;
                Count = count;
                EnsureRowCapacity(count);
                _rowMap.Clear();
                _rowMap.EnsureCapacity(count);
                for (int i = 0; i < count; i++)
                {
                    var row = new RowSnapshot(entities[i], ordinals[i], roleIds[i], flags[i]);
                    _rows[i] = row;
                    _rowMap[row.ToKey()] = row;
                }
            }

            private void EnsureRowCapacity(int count)
            {
                if (_rows.Length >= count)
                {
                    return;
                }

                int next = Math.Max(count, _rows.Length == 0 ? InitialRowCapacity : _rows.Length * 2);
                Array.Resize(ref _rows, next);
            }
        }
    }
}
