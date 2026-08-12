using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipRuntime
    {
        private static readonly QueryDescription RelationshipEntityQuery = new QueryDescription()
            .WithAll<RelationshipInstanceCm>();

        private readonly World _world;
        private readonly RelationshipTypeRegistry _types;
        private readonly RelationshipMetricRegistry _metrics;
        private readonly RelationshipFlagRegistry _flags;
        private readonly RelationshipBandRegistry _bands;
        private readonly RelationshipChangeBuffer _changes;
        private readonly RelationshipReverseIndex _reverseIndex;
        private readonly Dictionary<RelationshipEntityKey, Entity> _entityIndex = new();

        public RelationshipRuntime(
            World world,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands,
            RelationshipChangeBuffer changes,
            RelationshipReverseIndex reverseIndex)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _bands = bands ?? throw new ArgumentNullException(nameof(bands));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
            _reverseIndex = reverseIndex ?? throw new ArgumentNullException(nameof(reverseIndex));
            _reverseIndex.RebuildFromWorld();
            RebuildEntityIndexFromWorld();
        }

        public RelationshipTypeRegistry TypeRegistry => _types;
        public World World => _world;

        /// <summary>Reverse adjacency index backing incoming-edge queries.</summary>
        public RelationshipReverseIndex ReverseIndex => _reverseIndex;

        public void RebuildEntityIndexFromWorld()
        {
            _entityIndex.Clear();
            _world.Query(in RelationshipEntityQuery, (Entity entity, ref RelationshipInstanceCm relationship) =>
            {
                ValidateMaterializedRelationship(entity, in relationship);
                RelationshipEntityKey key = new(relationship.Source, relationship.Target, relationship.TypeId);
                if (_entityIndex.TryGetValue(key, out Entity existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate relationship entity projection for {key.Source.Id}:{key.Source.WorldId}:{key.Source.Version} -> " +
                        $"{key.Target.Id}:{key.Target.WorldId}:{key.Target.Version} type {key.TypeId}: " +
                        $"{existing.Id}:{existing.WorldId}:{existing.Version} and {entity.Id}:{entity.WorldId}:{entity.Version}.");
                }

                _entityIndex[key] = entity;
            });
        }

        public bool TryResolveRelationshipEntity(Entity source, Entity target, int typeId, out Entity relationshipEntity)
        {
            relationshipEntity = Entity.Null;
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEntityKey key = new(source, target, validatedTypeId);
            if (_entityIndex.TryGetValue(key, out Entity indexed) &&
                IsAliveInRuntimeWorld(indexed) &&
                _world.Has<RelationshipInstanceCm>(indexed) &&
                HasLink(source, target, validatedTypeId))
            {
                relationshipEntity = indexed;
                return true;
            }

            RebuildEntityIndexFromWorld();
            if (_entityIndex.TryGetValue(key, out indexed) &&
                IsAliveInRuntimeWorld(indexed) &&
                _world.Has<RelationshipInstanceCm>(indexed) &&
                HasLink(source, target, validatedTypeId))
            {
                relationshipEntity = indexed;
                return true;
            }

            return false;
        }

        public Entity MaterializeRelationshipEntity(Entity source, Entity target, int typeId)
        {
            EnsureAliveInRuntimeWorld(source, target);

            int validatedTypeId = ValidateTypeId(typeId);
            if (!HasLink(source, target, validatedTypeId))
            {
                throw new InvalidOperationException(
                    "RelationshipRuntime cannot materialize a relationship entity without an existing relationship edge. " +
                    DescribeEdgeState(source, target, validatedTypeId));
            }

            RelationshipEntityKey key = new(source, target, validatedTypeId);
            if (_entityIndex.TryGetValue(key, out Entity existing) &&
                IsAliveInRuntimeWorld(existing) &&
                _world.Has<RelationshipInstanceCm>(existing))
            {
                return existing;
            }

            Entity relationshipEntity = _world.Create(
                new RelationshipInstanceCm
                {
                    Source = source,
                    Target = target,
                    TypeId = validatedTypeId,
                    Revision = 1
                },
                AttributeBuffer.CreateAttached(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                new ActiveEffectContainer());
            _entityIndex[key] = relationshipEntity;
            return relationshipEntity;
        }

        public bool HasLink(Entity source, Entity target)
        {
            return HasLink(source, target, RelationshipTypeRegistry.AnyTypeId);
        }

        public bool HasLink(Entity source, Entity target, int typeId)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return false;
            }

            return typeId == RelationshipTypeRegistry.AnyTypeId
                ? set.Count > 0
                : set.HasType(ValidateTypeId(typeId));
        }

        public void EnsureLink(Entity source, Entity target, int typeId)
        {
            EnsureAliveInRuntimeWorld(source, target);

            int validatedTypeId = ValidateTypeId(typeId);
            bool hasExisting = TryGetEdgeSet(source, target, out RelationshipEdgeSet set);

            if (set.HasType(validatedTypeId))
            {
                MaterializeRelationshipEntity(source, target, validatedTypeId);
                return;
            }

            set.Set(validatedTypeId, RelationshipEdge.CreateDefault(_metrics));
            if (hasExisting)
            {
                _world.SetRelationship(source, target, set);
            }
            else
            {
                _world.AddRelationship(source, target, set);
            }

            _reverseIndex.OnLinkAdded(source, target, validatedTypeId);
            MaterializeRelationshipEntity(source, target, validatedTypeId);
        }

        public void RemoveLink(Entity source, Entity target, int typeId)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return;
            }

            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!set.Remove(validatedTypeId))
            {
                return;
            }

            RemoveMaterializedRelationshipEntity(source, target, validatedTypeId);

            if (set.Count == 0)
            {
                _world.RemoveRelationship<RelationshipEdgeSet>(source, target);
            }
            else
            {
                _world.SetRelationship(source, target, set);
            }

            _reverseIndex.OnLinkRemoved(source, target, validatedTypeId);
        }

        public bool TryGetMetric(Entity source, Entity target, int typeId, int metricId, out short value)
        {
            value = _metrics.Get(metricId).DefaultValue;
            if (!TryGetEdge(source, target, typeId, out RelationshipEdge edge))
            {
                return false;
            }

            value = edge.GetMetric(metricId);
            return true;
        }

        public bool TryGetEdge(Entity source, Entity target, int typeId, out RelationshipEdge edge)
        {
            return TryGetEdge(source, target, typeId, out edge, out _);
        }

        public short GetMetric(Entity source, Entity target, int typeId, int metricId)
        {
            return TryGetMetric(source, target, typeId, metricId, out short value)
                ? value
                : _metrics.Get(metricId).DefaultValue;
        }

        public short SetMetric(Entity source, Entity target, int typeId, int metricId, int value, int reasonId = 0)
        {
            EnsureLink(source, target, typeId);
            _metrics.Get(metricId);

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEdgeSet set = _world.GetRelationship<RelationshipEdgeSet>(source, target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            Entity relationshipEntity = MaterializeRelationshipEntity(source, target, validatedTypeId);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            short oldValue = edge.GetMetric(metricId);
            short clamped = ClampToDefinition(metricId, value);
            if (oldValue == clamped)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    _world.SetRelationship(source, target, set);
                }

                return clamped;
            }

            BumpMaterializedRelationshipRevision(relationshipEntity);
            uint oldFlags = edge.Flags;
            edge.SetMetric(metricId, clamped);
            edge.Flags = ApplyBands(validatedTypeId, metricId, edge.Flags, clamped);
            edge.Version++;
            set.Set(validatedTypeId, edge);
            _world.SetRelationship(source, target, set);
            _changes.TryAdd(new RelationshipChangeRecord(source, target, validatedTypeId, metricId, reasonId, oldValue, clamped, oldFlags, edge.Flags));
            return clamped;
        }

        public short AddMetric(Entity source, Entity target, int typeId, int metricId, int delta, int reasonId = 0)
        {
            short current = GetMetric(source, target, typeId, metricId);
            return SetMetric(source, target, typeId, metricId, current + delta, reasonId);
        }

        public bool HasFlag(Entity source, Entity target, int typeId, int flagId)
        {
            return TryHasFlag(source, target, typeId, flagId, out bool enabled) && enabled;
        }

        public bool TryHasFlag(Entity source, Entity target, int typeId, int flagId, out bool enabled)
        {
            enabled = false;
            if (!TryGetEdge(source, target, typeId, out RelationshipEdge edge))
            {
                return false;
            }

            enabled = (edge.Flags & _flags.GetMask(flagId)) != 0;
            return true;
        }

        public void SetFlag(Entity source, Entity target, int typeId, int flagId, bool enabled, int reasonId = 0)
        {
            EnsureLink(source, target, typeId);

            int validatedTypeId = ValidateTypeId(typeId);
            RelationshipEdgeSet set = _world.GetRelationship<RelationshipEdgeSet>(source, target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            Entity relationshipEntity = MaterializeRelationshipEntity(source, target, validatedTypeId);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            uint mask = _flags.GetMask(flagId);
            uint oldFlags = edge.Flags;
            uint newFlags = enabled ? oldFlags | mask : oldFlags & ~mask;
            if (oldFlags == newFlags)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    _world.SetRelationship(source, target, set);
                }

                return;
            }

            BumpMaterializedRelationshipRevision(relationshipEntity);
            edge.Flags = newFlags;
            edge.Version++;
            set.Set(validatedTypeId, edge);
            _world.SetRelationship(source, target, set);
            _changes.TryAdd(new RelationshipChangeRecord(source, target, validatedTypeId, metricId: -1, reasonId, oldValue: 0, newValue: 0, oldFlags, newFlags));
        }

        public bool TryGetHighestMetricTarget(Entity source, ReadOnlySpan<Entity> candidates, int typeId, int metricId, out Entity target, out short value)
        {
            target = Entity.Null;
            value = short.MinValue;
            bool found = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                Entity candidate = candidates[i];
                if (!IsAliveInRuntimeWorld(candidate))
                {
                    continue;
                }

                if (!TryGetMetric(source, candidate, typeId, metricId, out short current))
                {
                    continue;
                }

                if (!found || current > value)
                {
                    target = candidate;
                    value = current;
                    found = true;
                }
            }

            return found;
        }

        public int CollectOutgoing(Entity source, Span<Entity> buffer)
        {
            return CollectOutgoing(source, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectOutgoing(Entity source, int typeId, Span<Entity> buffer)
        {
            if (!IsAliveInRuntimeWorld(source) || buffer.Length == 0 || !_world.Has<Relationship<RelationshipEdgeSet>>(source))
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(source);
            int count = 0;
            foreach ((Entity target, RelationshipEdgeSet set) in relationships)
            {
                if (count >= buffer.Length)
                {
                    break;
                }

                if (!MatchesType(set, validatedTypeId))
                {
                    continue;
                }

                buffer[count++] = target;
            }

            return count;
        }

        public int CollectIncoming(Entity target, Span<Entity> buffer)
        {
            return CollectIncoming(target, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        /// <summary>
        /// Collects live incoming sources straight from the reverse index. No per-source edge re-verification:
        /// <see cref="EnsureLink"/>/<see cref="RemoveLink"/> are the only edge mutation paths (M9 guardrail)
        /// and both notify the index, while entity death is covered by the index's lazy IsAlive reclamation.
        /// </summary>
        public int CollectIncoming(Entity target, int typeId, Span<Entity> buffer)
        {
            if (!IsAliveInRuntimeWorld(target) || buffer.Length == 0)
            {
                return 0;
            }

            return _reverseIndex.CopyIncoming(target, ValidateFilterTypeId(typeId), buffer);
        }

        public int CollectMutual(Entity first, Entity second, Span<Entity> buffer)
        {
            return CollectMutual(first, second, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectMutual(Entity first, Entity second, int typeId, Span<Entity> buffer)
        {
            if (!IsAliveInRuntimeWorld(first) || !IsAliveInRuntimeWorld(second) || buffer.Length == 0 || !_world.Has<Relationship<RelationshipEdgeSet>>(first))
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(first);
            int count = 0;
            foreach ((Entity candidate, RelationshipEdgeSet set) in relationships)
            {
                if (count >= buffer.Length)
                {
                    break;
                }

                if (!IsAliveInRuntimeWorld(candidate) || !MatchesType(set, validatedTypeId))
                {
                    continue;
                }

                if (HasLink(candidate, second, validatedTypeId) && HasLink(second, candidate, validatedTypeId))
                {
                    buffer[count++] = candidate;
                }
            }

            return count;
        }

        public int CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer)
        {
            return CollectBetweenPair(source, target, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectBetweenPair(Entity source, Entity target, int typeId, Span<Entity> buffer)
        {
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target) || buffer.Length == 0)
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            int count = 0;
            if (HasLink(source, target, validatedTypeId))
            {
                buffer[count++] = target;
            }

            if (count < buffer.Length && HasLink(target, source, validatedTypeId))
            {
                buffer[count++] = source;
            }

            return count;
        }

        private bool TryGetEdge(Entity source, Entity target, int typeId, out RelationshipEdge edge, out bool resized)
        {
            edge = default;
            resized = false;
            if (!IsAliveInRuntimeWorld(source) || !IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!TryGetEdgeSet(source, target, out RelationshipEdgeSet set))
            {
                return false;
            }

            if (!set.TryGet(validatedTypeId, out edge))
            {
                return false;
            }

            return true;
        }

        private bool TryGetEdgeSet(Entity source, Entity target, out RelationshipEdgeSet set)
        {
            set = default;
            ref Relationship<RelationshipEdgeSet> relationships = ref _world.TryGetRef<Relationship<RelationshipEdgeSet>>(source, out bool exists);
            return exists && relationships.TryGetValueNoAlloc(target, out set);
        }

        private void RemoveMaterializedRelationshipEntity(Entity source, Entity target, int typeId)
        {
            RelationshipEntityKey key = new(source, target, typeId);
            if (!_entityIndex.TryGetValue(key, out Entity entity))
            {
                RebuildEntityIndexFromWorld();
                if (!_entityIndex.TryGetValue(key, out entity))
                {
                    return;
                }
            }

            _entityIndex.Remove(key);
            if (IsAliveInRuntimeWorld(entity) && _world.Has<RelationshipInstanceCm>(entity))
            {
                _world.Destroy(entity);
            }
        }

        private void BumpMaterializedRelationshipRevision(Entity relationshipEntity)
        {
            if (!IsAliveInRuntimeWorld(relationshipEntity) || !_world.Has<RelationshipInstanceCm>(relationshipEntity))
            {
                return;
            }

            ref RelationshipInstanceCm relationship = ref _world.Get<RelationshipInstanceCm>(relationshipEntity);
            relationship.Revision++;
        }

        private void ValidateMaterializedRelationship(Entity entity, in RelationshipInstanceCm relationship)
        {
            if (relationship.TypeId < 0)
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} has invalid type id {relationship.TypeId}.");
            }

            if (!IsAliveInRuntimeWorld(relationship.Source) || !IsAliveInRuntimeWorld(relationship.Target))
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} references a missing source or target entity.");
            }

            if (!HasLink(relationship.Source, relationship.Target, relationship.TypeId))
            {
                throw new InvalidOperationException(
                    $"Relationship entity {entity.Id}:{entity.WorldId}:{entity.Version} has no matching relationship edge for type {relationship.TypeId}.");
            }
        }

        private int ValidateTypeId(int typeId)
        {
            _types.Get(typeId);
            return typeId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsAliveInRuntimeWorld(Entity entity)
            => entity != Entity.Null && entity.WorldId == _world.Id && _world.IsAlive(entity);

        private void EnsureAliveInRuntimeWorld(Entity source, Entity target)
        {
            if (IsAliveInRuntimeWorld(source) && IsAliveInRuntimeWorld(target))
            {
                return;
            }

            throw new InvalidOperationException(
                $"RelationshipRuntime requires both source and target entities to be alive in World {_world.Id}; " +
                $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}.");
        }

        private string DescribeEdgeState(Entity source, Entity target, int typeId)
        {
            bool sourceHasRelationships = _world.Has<Relationship<RelationshipEdgeSet>>(source);
            if (!sourceHasRelationships)
            {
                return $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}, type={typeId}, sourceHasRelationships=false.";
            }

            ref Relationship<RelationshipEdgeSet> relationships = ref _world.Get<Relationship<RelationshipEdgeSet>>(source);
            bool hasTarget = relationships.TryGetValueNoAlloc(target, out RelationshipEdgeSet set);
            return $"source={source.Id}:{source.WorldId}:{source.Version}, target={target.Id}:{target.WorldId}:{target.Version}, type={typeId}, sourceHasRelationships=true, targetEdgeSet={hasTarget}, edgeTypeCount={(hasTarget ? set.Count : 0)}.";
        }

        private int ValidateFilterTypeId(int typeId)
        {
            if (typeId == RelationshipTypeRegistry.AnyTypeId)
            {
                return typeId;
            }

            return ValidateTypeId(typeId);
        }

        private static bool MatchesType(in RelationshipEdgeSet set, int typeId)
        {
            return typeId == RelationshipTypeRegistry.AnyTypeId
                ? set.Count > 0
                : set.HasType(typeId);
        }

        private short ClampToDefinition(int metricId, int value)
        {
            ref readonly RelationshipMetricDefinition definition = ref _metrics.Get(metricId);
            if (value < definition.MinValue)
            {
                return definition.MinValue;
            }

            if (value > definition.MaxValue)
            {
                return definition.MaxValue;
            }

            return (short)value;
        }

        private uint ApplyBands(int typeId, int metricId, uint flags, short value)
        {
            var bands = _bands.Bands;
            for (int i = 0; i < bands.Count; i++)
            {
                RelationshipBandDefinition band = bands[i];
                if (band.TypeId != typeId || band.MetricId != metricId)
                {
                    continue;
                }

                uint mask = _flags.GetMask(band.FlagId);
                bool isActive = band.Comparison switch
                {
                    RelationshipBandComparison.GreaterOrEqual => value >= band.Threshold,
                    RelationshipBandComparison.LessOrEqual => value <= band.Threshold,
                    _ => false,
                };

                flags = isActive ? flags | mask : flags & ~mask;
            }

            return flags;
        }

        private readonly struct RelationshipEntityKey : IEquatable<RelationshipEntityKey>
        {
            public RelationshipEntityKey(Entity source, Entity target, int typeId)
            {
                Source = source;
                Target = target;
                TypeId = typeId;
            }

            public Entity Source { get; }
            public Entity Target { get; }
            public int TypeId { get; }

            public bool Equals(RelationshipEntityKey other)
            {
                return Source.Equals(other.Source) &&
                       Target.Equals(other.Target) &&
                       TypeId == other.TypeId;
            }

            public override bool Equals(object? obj)
            {
                return obj is RelationshipEntityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Source, Target, TypeId);
            }
        }
    }
}
