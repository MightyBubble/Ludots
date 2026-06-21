using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Relationships;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipRuntime
    {
        private static readonly QueryDescription RelationshipQuery = new QueryDescription()
            .WithAll<Relationship<RelationshipEdgeSet>>();

        private readonly World _world;
        private readonly RelationshipTypeRegistry _types;
        private readonly RelationshipMetricRegistry _metrics;
        private readonly RelationshipFlagRegistry _flags;
        private readonly RelationshipBandRegistry _bands;
        private readonly RelationshipChangeBuffer _changes;

        public RelationshipRuntime(
            World world,
            RelationshipTypeRegistry types,
            RelationshipMetricRegistry metrics,
            RelationshipFlagRegistry flags,
            RelationshipBandRegistry bands,
            RelationshipChangeBuffer changes)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _bands = bands ?? throw new ArgumentNullException(nameof(bands));
            _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        }

        public bool HasLink(Entity source, Entity target)
        {
            return HasLink(source, target, RelationshipTypeRegistry.AnyTypeId);
        }

        public bool HasLink(Entity source, Entity target, int typeId)
        {
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                return false;
            }

            if (!source.TryGetRelationship(target, out RelationshipEdgeSet set))
            {
                return false;
            }

            return typeId == RelationshipTypeRegistry.AnyTypeId
                ? set.Count > 0
                : set.HasType(ValidateTypeId(typeId));
        }

        public void EnsureLink(Entity source, Entity target, int typeId)
        {
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                throw new InvalidOperationException("RelationshipRuntime requires both source and target entities to be alive.");
            }

            int validatedTypeId = ValidateTypeId(typeId);
            bool hasExisting = source.TryGetRelationship(target, out RelationshipEdgeSet set);

            if (set.HasType(validatedTypeId))
            {
                return;
            }

            set.Set(validatedTypeId, RelationshipEdge.CreateDefault(_metrics));
            if (hasExisting)
            {
                source.SetRelationship(target, set);
            }
            else
            {
                source.AddRelationship(target, set);
            }
        }

        public void RemoveLink(Entity source, Entity target, int typeId)
        {
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                return;
            }

            if (!source.TryGetRelationship(target, out RelationshipEdgeSet set))
            {
                return;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!set.Remove(validatedTypeId))
            {
                return;
            }

            if (set.Count == 0)
            {
                source.RemoveRelationship<RelationshipEdgeSet>(target);
                return;
            }

            source.SetRelationship(target, set);
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
            RelationshipEdgeSet set = source.GetRelationship<RelationshipEdgeSet>(target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            short oldValue = edge.GetMetric(metricId);
            short clamped = ClampToDefinition(metricId, value);
            if (oldValue == clamped)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    source.SetRelationship(target, set);
                }

                return clamped;
            }

            uint oldFlags = edge.Flags;
            edge.SetMetric(metricId, clamped);
            edge.Flags = ApplyBands(validatedTypeId, metricId, edge.Flags, clamped);
            edge.Version++;
            set.Set(validatedTypeId, edge);
            source.SetRelationship(target, set);
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
            RelationshipEdgeSet set = source.GetRelationship<RelationshipEdgeSet>(target);
            RelationshipEdge edge = set.GetOrAdd(validatedTypeId, _metrics, out _);
            bool resized = edge.EnsureMetricCapacity(_metrics);
            uint mask = _flags.GetMask(flagId);
            uint oldFlags = edge.Flags;
            uint newFlags = enabled ? oldFlags | mask : oldFlags & ~mask;
            if (oldFlags == newFlags)
            {
                if (resized)
                {
                    set.Set(validatedTypeId, edge);
                    source.SetRelationship(target, set);
                }

                return;
            }

            edge.Flags = newFlags;
            edge.Version++;
            set.Set(validatedTypeId, edge);
            source.SetRelationship(target, set);
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
                if (!_world.IsAlive(candidate))
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
            if (!_world.IsAlive(source) || buffer.Length == 0 || !_world.Has<Relationship<RelationshipEdgeSet>>(source))
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

        public int CollectIncoming(Entity target, int typeId, Span<Entity> buffer)
        {
            if (!_world.IsAlive(target) || buffer.Length == 0)
            {
                return 0;
            }

            int validatedTypeId = ValidateFilterTypeId(typeId);
            int count = 0;
            foreach (ref var chunk in _world.Query(in RelationshipQuery))
            {
                ref Entity sourceFirst = ref chunk.Entity(0);
                Span<Relationship<RelationshipEdgeSet>> relationshipSpans = chunk.GetSpan<Relationship<RelationshipEdgeSet>>();
                foreach (int index in chunk)
                {
                    if (count >= buffer.Length)
                    {
                        break;
                    }

                    Entity source = Unsafe.Add(ref sourceFirst, index);
                    ref Relationship<RelationshipEdgeSet> relationships = ref relationshipSpans[index];
                    foreach ((Entity key, RelationshipEdgeSet set) in relationships)
                    {
                        if (key != target || !MatchesType(set, validatedTypeId))
                        {
                            continue;
                        }

                        buffer[count++] = source;
                        break;
                    }
                }

                if (count >= buffer.Length)
                {
                    break;
                }
            }

            return count;
        }

        public int CollectMutual(Entity first, Entity second, Span<Entity> buffer)
        {
            return CollectMutual(first, second, RelationshipTypeRegistry.AnyTypeId, buffer);
        }

        public int CollectMutual(Entity first, Entity second, int typeId, Span<Entity> buffer)
        {
            if (!_world.IsAlive(first) || !_world.IsAlive(second) || buffer.Length == 0 || !_world.Has<Relationship<RelationshipEdgeSet>>(first))
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

                if (!_world.IsAlive(candidate) || !MatchesType(set, validatedTypeId))
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
            if (!_world.IsAlive(source) || !_world.IsAlive(target) || buffer.Length == 0)
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
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                return false;
            }

            int validatedTypeId = ValidateTypeId(typeId);
            if (!source.TryGetRelationship(target, out RelationshipEdgeSet set))
            {
                return false;
            }

            if (!set.TryGet(validatedTypeId, out edge))
            {
                return false;
            }

            return true;
        }

        private int ValidateTypeId(int typeId)
        {
            _types.Get(typeId);
            return typeId;
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
    }
}
