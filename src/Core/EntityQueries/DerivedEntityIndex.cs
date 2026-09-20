using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.EntityCollections;

namespace Ludots.Core.EntityQueries;

public delegate bool EntityQueryPredicate(World world, Entity entity);

public sealed class DerivedEntityIndex : IDisposable, IEntityCollectionSource
{
    private readonly World _world;
    private readonly MapId? _map;
    private readonly QueryDescription _query;
    private readonly Query _matchedQuery;
    private readonly object _pendingLock = new();
    private readonly EntityQueryPredicate _predicate;
    private readonly Func<bool>? _isCurrent;
    private readonly HashSet<int> _dependencies;
    private readonly HashSet<Entity> _dirty;
    private readonly Dictionary<Entity, int> _positions;
    private Entity[] _members;
    private int _count;
    private bool _disposed;

    public DerivedEntityIndex(World world, MapId? map, in QueryDescription query,
        ReadOnlySpan<ComponentType> dependencies, EntityQueryPredicate predicate, int initialCapacity = 256, Func<bool>? isCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _world = world;
        _map = map;
        _query = query;
        _matchedQuery = world.Query(in query);
        _predicate = predicate;
        _isCurrent = isCurrent;
        _dependencies = new HashSet<int>();
        foreach (ComponentType dependency in dependencies) _dependencies.Add(dependency.Id);
        foreach (ComponentType component in query.All) _dependencies.Add(component.Id);
        foreach (ComponentType component in query.Any) _dependencies.Add(component.Id);
        foreach (ComponentType component in query.None) _dependencies.Add(component.Id);
        _dependencies.Add(Component<MapEntity>.ComponentType.Id);
        _dirty = new HashSet<Entity>(initialCapacity);
        _positions = new Dictionary<Entity, int>(initialCapacity);
        _members = new Entity[initialCapacity];
        foreach (ref var chunk in world.Query(in _query))
        {
            foreach (int row in chunk)
            {
                Entity entity = chunk.Entity(row);
                InitialVisitedCount++;
                if (Matches(entity)) Add(entity);
            }
        }
        world.ComponentChanged += OnComponentChanged;
        world.EntityMaterialized += OnMaterialized;
        world.SubscribeEntityDestroyed(OnDestroyed);
    }

    public long InitialVisitedCount { get; private set; }
    public long ReevaluatedCount { get; private set; }
    public long MembershipChanges { get; private set; }
    public uint Revision { get; private set; } = 1;

    public ReadOnlySpan<Entity> Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isCurrent != null && !_isCurrent()) throw new InvalidOperationException("ENTITY_QUERY.ERR.PlanReplaced");
        lock (_pendingLock)
        {
            foreach (Entity entity in _dirty)
            {
                ReevaluatedCount++;
                if (Matches(entity)) Add(entity);
                else Remove(entity);
            }
            _dirty.Clear();
        }
        return _members.AsSpan(0, _count);
    }

    public bool Contains(Entity entity)
    {
        Read();
        return _positions.ContainsKey(entity);
    }

    public int CopyPage(int offset, uint expectedRevision, Span<Entity> destination, out bool hasMore)
    {
        ReadOnlySpan<Entity> members = Read();
        if (expectedRevision != Revision)
            throw new InvalidOperationException("ENTITY_QUERY.ERR.RevisionChanged");
        if ((uint)offset > (uint)members.Length || destination.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(offset));
        int count = Math.Min(destination.Length, members.Length - offset);
        members.Slice(offset, count).CopyTo(destination);
        hasMore = offset + count < members.Length;
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _world.ComponentChanged -= OnComponentChanged;
        _world.EntityMaterialized -= OnMaterialized;
        _world.UnsubscribeEntityDestroyed(OnDestroyed);
        _dirty.Clear();
        _positions.Clear();
        _members = Array.Empty<Entity>();
        _count = 0;
    }

    private bool Matches(Entity entity) => _world.IsAlive(entity) &&
        _matchedQuery.Matches(_world.GetArchetype(entity).BitSet) &&
        _world.Has<MapEntity>(entity) &&
        (!_map.HasValue || _world.Get<MapEntity>(entity).MapId == _map.Value) &&
        _predicate(_world, entity);

    private void OnMaterialized(Entity entity)
    {
        lock (_pendingLock)
            if (!_disposed) _dirty.Add(entity);
    }

    private void OnComponentChanged(Entity entity, ComponentType type)
    {
        if (_query.Exclusive.Count == 0 && !_dependencies.Contains(type.Id)) return;
        lock (_pendingLock)
            if (!_disposed) _dirty.Add(entity);
    }

    private void OnDestroyed(in Entity entity)
    {
        lock (_pendingLock)
            if (!_disposed) _dirty.Add(entity);
    }

    private void Add(Entity entity)
    {
        if (_positions.ContainsKey(entity)) return;
        if (_count == _members.Length) Array.Resize(ref _members, checked(_count * 2));
        _positions.Add(entity, _count);
        _members[_count++] = entity;
        Changed();
    }

    private void Remove(Entity entity)
    {
        if (!_positions.Remove(entity, out int position)) return;
        Entity last = _members[--_count];
        if (position != _count)
        {
            _members[position] = last;
            _positions[last] = position;
        }
        _members[_count] = default;
        Changed();
    }

    private void Changed()
    {
        MembershipChanges++;
        Revision = checked(Revision + 1);
    }
}
