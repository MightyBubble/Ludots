using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.EntityQueries
{
    /// <summary>
    /// Code-facing entity set query API used by both C# systems and graph ops.
    /// All hot methods operate on caller-owned spans and resolved ids.
    /// </summary>
    public sealed class EntitySetQueryRuntime
    {
        private static readonly QueryDescription MapEntityQuery = new QueryDescription()
            .WithAll<MapEntity>();

        private readonly World _world;
        private readonly TagOps _tagOps;
        private readonly RelationshipRuntime _relationships;

        public EntitySetQueryRuntime(World world, TagOps tagOps, RelationshipRuntime relationships)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        }

        public int CollectMapEntities(Span<Entity> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            foreach (ref var chunk in _world.Query(in MapEntityQuery))
            {
                ref Entity first = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    if (written >= destination.Length)
                    {
                        return written;
                    }

                    destination[written++] = Unsafe.Add(ref first, index);
                }
            }

            return written;
        }

        public int CopyCollection(EntityCollectionStore store, Entity owner, string collectionKey, Span<Entity> destination)
        {
            ArgumentNullException.ThrowIfNull(store);
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(collectionKey))
            {
                throw new ArgumentException("Entity collection key is required.", nameof(collectionKey));
            }

            return store.CopyEntities(owner, collectionKey, destination);
        }

        public int FilterTeam(Span<Entity> entities, int count, int teamId)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity) || !_world.Has<Team>(entity))
                {
                    continue;
                }

                if (_world.Get<Team>(entity).Id != teamId)
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterTeamRelationship(Span<Entity> entities, int count, Entity reference, RelationshipFilter filter)
        {
            count = ClampCount(entities, count);
            if (!_world.IsAlive(reference) || !_world.Has<Team>(reference))
            {
                return 0;
            }

            int sourceTeamId = _world.Get<Team>(reference).Id;
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity) || !_world.Has<Team>(entity))
                {
                    continue;
                }

                if (!RelationshipFilterUtil.Passes(filter, sourceTeamId, _world.Get<Team>(entity).Id))
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterTemplate(Span<Entity> entities, int count, int templateKeyId)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity) || !_world.Has<EntityTemplateKeyRef>(entity))
                {
                    continue;
                }

                if (_world.Get<EntityTemplateKeyRef>(entity).TemplateKeyId != templateKeyId)
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterAttributeRange(Span<Entity> entities, int count, int attributeId, float minInclusive, float maxInclusive)
        {
            if (minInclusive > maxInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(minInclusive), "Attribute range minimum must be <= maximum.");
            }

            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!TryGetDefinedAttribute(entity, attributeId, out float value))
                {
                    continue;
                }

                if (value < minInclusive || value > maxInclusive)
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterTagAny(Span<Entity> entities, int count, int tagId)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity))
                {
                    continue;
                }

                ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
                if (!_tagOps.HasTag(ref tags, tagId, TagSense.Effective))
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterTagNone(Span<Entity> entities, int count, int tagId)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity))
                {
                    continue;
                }

                bool hasTag = false;
                if (_world.Has<GameplayTagContainer>(entity))
                {
                    ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
                    hasTag = _tagOps.HasTag(ref tags, tagId, TagSense.Effective);
                }

                if (hasTag)
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterLayer(Span<Entity> entities, int count, uint requiredMask)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (!_world.IsAlive(entity) || !_world.Has<EntityLayer>(entity))
                {
                    continue;
                }

                uint category = _world.Get<EntityLayer>(entity).Value.Category;
                if ((category & requiredMask) == 0)
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int FilterNotEntity(Span<Entity> entities, int count, Entity exclude)
        {
            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity entity = entities[read];
                if (entity.Equals(exclude))
                {
                    continue;
                }

                entities[write++] = entity;
            }

            return write;
        }

        public int SortStableDedup(Span<Entity> entities, int count)
        {
            count = ClampCount(entities, count);
            return SpatialQueryPostProcessor.SortStableDedup(entities.Slice(0, count));
        }

        public int Limit(Span<Entity> entities, int count, int limit)
        {
            count = ClampCount(entities, count);
            if (limit < 0)
            {
                return 0;
            }

            return count > limit ? limit : count;
        }

        public void SortByAttribute(Span<Entity> entities, int count, int attributeId, bool descending)
        {
            count = ClampCount(entities, count);
            SortByAttributeInPlace(entities.Slice(0, count), attributeId, descending);
        }

        public float SumAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            float sum = 0f;
            for (int i = 0; i < entities.Length; i++)
            {
                if (TryGetDefinedAttribute(entities[i], attributeId, out float value))
                {
                    sum += value;
                }
            }

            return sum;
        }

        public float AverageAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryGetDefinedAttribute(entities[i], attributeId, out float value))
                {
                    continue;
                }

                sum += value;
                count++;
            }

            return count == 0 ? 0f : sum / count;
        }

        public float MaxAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return TryExtremeAttribute(entities, attributeId, findMax: true, out _, out float value)
                ? value
                : 0f;
        }

        public float MinAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return TryExtremeAttribute(entities, attributeId, findMax: false, out _, out float value)
                ? value
                : 0f;
        }

        public bool TryMaxEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return TryExtremeAttribute(entities, attributeId, findMax: true, out entity, out value);
        }

        public bool TryMinEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return TryExtremeAttribute(entities, attributeId, findMax: false, out entity, out value);
        }

        public int FilterRelationshipMetricRange(Span<Entity> entities, int count, Entity source, int typeId, int metricId, short minInclusive, short maxInclusive)
        {
            if (minInclusive > maxInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(minInclusive), "Relationship metric range minimum must be <= maximum.");
            }

            if (!_world.IsAlive(source))
            {
                return 0;
            }

            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity target = entities[read];
                if (!_world.IsAlive(target))
                {
                    continue;
                }

                short value = _relationships.GetMetric(source, target, typeId, metricId);
                if (value < minInclusive || value > maxInclusive)
                {
                    continue;
                }

                entities[write++] = target;
            }

            return write;
        }

        public int FilterRelationshipFlag(Span<Entity> entities, int count, Entity source, int typeId, int flagId, bool expected)
        {
            if (!_world.IsAlive(source))
            {
                return 0;
            }

            count = ClampCount(entities, count);
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Entity target = entities[read];
                if (!_world.IsAlive(target))
                {
                    continue;
                }

                if (_relationships.HasFlag(source, target, typeId, flagId) != expected)
                {
                    continue;
                }

                entities[write++] = target;
            }

            return write;
        }

        public void SortByRelationshipMetric(Span<Entity> entities, int count, Entity source, int typeId, int metricId, bool descending)
        {
            count = ClampCount(entities, count);
            SortByRelationshipMetricInPlace(entities.Slice(0, count), source, typeId, metricId, descending);
        }

        public int SumRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            if (!_world.IsAlive(source))
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (_world.IsAlive(entities[i]))
                {
                    sum += _relationships.GetMetric(source, entities[i], typeId, metricId);
                }
            }

            return sum;
        }

        public int AverageRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            if (!_world.IsAlive(source))
            {
                return 0;
            }

            int sum = 0;
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!_world.IsAlive(entities[i]))
                {
                    continue;
                }

                sum += _relationships.GetMetric(source, entities[i], typeId, metricId);
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }

        public int MaxRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            return TryExtremeRelationshipMetric(entities, source, typeId, metricId, findMax: true, out _, out int value)
                ? value
                : 0;
        }

        public int MinRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            return TryExtremeRelationshipMetric(entities, source, typeId, metricId, findMax: false, out _, out int value)
                ? value
                : 0;
        }

        public bool TryMaxEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
        {
            return TryExtremeRelationshipMetric(entities, source, typeId, metricId, findMax: true, out entity, out value);
        }

        public bool TryMinEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
        {
            return TryExtremeRelationshipMetric(entities, source, typeId, metricId, findMax: false, out entity, out value);
        }

        public bool TryMinEntityByDistance(ReadOnlySpan<Entity> entities, IntVector2 center, out Entity entity, out long distanceSquared)
        {
            entity = Entity.Null;
            distanceSquared = long.MaxValue;
            bool found = false;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity candidate = entities[i];
                if (!_world.IsAlive(candidate) || !_world.Has<Position>(candidate))
                {
                    continue;
                }

                IntVector2 gridPos = _world.Get<Position>(candidate).GridPos;
                long dx = gridPos.X - center.X;
                long dy = gridPos.Y - center.Y;
                long current = dx * dx + dy * dy;
                if (!found ||
                    current < distanceSquared ||
                    (current == distanceSquared && CompareEntityStable(candidate, entity) < 0))
                {
                    entity = candidate;
                    distanceSquared = current;
                    found = true;
                }
            }

            return found;
        }

        private bool TryExtremeAttribute(ReadOnlySpan<Entity> entities, int attributeId, bool findMax, out Entity entity, out float value)
        {
            entity = Entity.Null;
            value = 0f;
            bool found = false;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity candidate = entities[i];
                if (!TryGetDefinedAttribute(candidate, attributeId, out float current))
                {
                    continue;
                }

                if (!found ||
                    (findMax ? current > value : current < value) ||
                    (current == value && CompareEntityStable(candidate, entity) < 0))
                {
                    entity = candidate;
                    value = current;
                    found = true;
                }
            }

            return found;
        }

        private bool TryExtremeRelationshipMetric(
            ReadOnlySpan<Entity> entities,
            Entity source,
            int typeId,
            int metricId,
            bool findMax,
            out Entity entity,
            out int value)
        {
            entity = Entity.Null;
            value = 0;
            if (!_world.IsAlive(source))
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity candidate = entities[i];
                if (!_world.IsAlive(candidate))
                {
                    continue;
                }

                int current = _relationships.GetMetric(source, candidate, typeId, metricId);
                if (!found ||
                    (findMax ? current > value : current < value) ||
                    (current == value && CompareEntityStable(candidate, entity) < 0))
                {
                    entity = candidate;
                    value = current;
                    found = true;
                }
            }

            return found;
        }

        private bool TryGetDefinedAttribute(Entity entity, int attributeId, out float value)
        {
            value = 0f;
            if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return false;
            }

            ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
            if (!attributes.HasAttribute(attributeId))
            {
                return false;
            }

            value = attributes.GetCurrent(attributeId);
            return true;
        }

        private float GetSortAttributeValue(Entity entity, int attributeId)
        {
            return TryGetDefinedAttribute(entity, attributeId, out float value) ? value : 0f;
        }

        private int GetSortRelationshipMetricValue(Entity source, Entity target, int typeId, int metricId)
        {
            return _world.IsAlive(source) && _world.IsAlive(target)
                ? _relationships.GetMetric(source, target, typeId, metricId)
                : 0;
        }

        private static int ClampCount(Span<Entity> entities, int count)
        {
            if (count < 0)
            {
                return 0;
            }

            return count > entities.Length ? entities.Length : count;
        }

        private static int CompareAttributeThenEntity(float leftValue, Entity left, float rightValue, Entity right, bool descending)
        {
            int valueCompare = leftValue.CompareTo(rightValue);
            if (descending)
            {
                valueCompare = -valueCompare;
            }

            return valueCompare != 0 ? valueCompare : CompareEntityStable(left, right);
        }

        private static int CompareIntThenEntity(int leftValue, Entity left, int rightValue, Entity right, bool descending)
        {
            int valueCompare = leftValue.CompareTo(rightValue);
            if (descending)
            {
                valueCompare = -valueCompare;
            }

            return valueCompare != 0 ? valueCompare : CompareEntityStable(left, right);
        }

        private static int CompareEntityStable(Entity left, Entity right)
        {
            int c = left.WorldId.CompareTo(right.WorldId);
            if (c != 0)
            {
                return c;
            }

            c = left.Id.CompareTo(right.Id);
            if (c != 0)
            {
                return c;
            }

            return left.Version.CompareTo(right.Version);
        }

        private void SortByAttributeInPlace(Span<Entity> entities, int attributeId, bool descending)
        {
            int length = entities.Length;
            if (length <= 1)
            {
                return;
            }

            for (int start = (length / 2) - 1; start >= 0; start--)
            {
                SiftDownByAttribute(entities, start, length, attributeId, descending);
            }

            for (int end = length - 1; end > 0; end--)
            {
                (entities[0], entities[end]) = (entities[end], entities[0]);
                SiftDownByAttribute(entities, 0, end, attributeId, descending);
            }
        }

        private void SortByRelationshipMetricInPlace(Span<Entity> entities, Entity source, int typeId, int metricId, bool descending)
        {
            int length = entities.Length;
            if (length <= 1)
            {
                return;
            }

            for (int start = (length / 2) - 1; start >= 0; start--)
            {
                SiftDownByRelationshipMetric(entities, start, length, source, typeId, metricId, descending);
            }

            for (int end = length - 1; end > 0; end--)
            {
                (entities[0], entities[end]) = (entities[end], entities[0]);
                SiftDownByRelationshipMetric(entities, 0, end, source, typeId, metricId, descending);
            }
        }

        private void SiftDownByAttribute(Span<Entity> entities, int root, int length, int attributeId, bool descending)
        {
            while (true)
            {
                int child = (root * 2) + 1;
                if (child >= length)
                {
                    return;
                }

                int swap = root;
                if (CompareAttributeEntities(entities[swap], entities[child], attributeId, descending) < 0)
                {
                    swap = child;
                }

                int right = child + 1;
                if (right < length && CompareAttributeEntities(entities[swap], entities[right], attributeId, descending) < 0)
                {
                    swap = right;
                }

                if (swap == root)
                {
                    return;
                }

                (entities[root], entities[swap]) = (entities[swap], entities[root]);
                root = swap;
            }
        }

        private void SiftDownByRelationshipMetric(
            Span<Entity> entities,
            int root,
            int length,
            Entity source,
            int typeId,
            int metricId,
            bool descending)
        {
            while (true)
            {
                int child = (root * 2) + 1;
                if (child >= length)
                {
                    return;
                }

                int swap = root;
                if (CompareRelationshipMetricEntities(entities[swap], entities[child], source, typeId, metricId, descending) < 0)
                {
                    swap = child;
                }

                int right = child + 1;
                if (right < length && CompareRelationshipMetricEntities(entities[swap], entities[right], source, typeId, metricId, descending) < 0)
                {
                    swap = right;
                }

                if (swap == root)
                {
                    return;
                }

                (entities[root], entities[swap]) = (entities[swap], entities[root]);
                root = swap;
            }
        }

        private int CompareAttributeEntities(Entity left, Entity right, int attributeId, bool descending)
        {
            return CompareAttributeThenEntity(
                GetSortAttributeValue(left, attributeId),
                left,
                GetSortAttributeValue(right, attributeId),
                right,
                descending);
        }

        private int CompareRelationshipMetricEntities(Entity left, Entity right, Entity source, int typeId, int metricId, bool descending)
        {
            return CompareIntThenEntity(
                GetSortRelationshipMetricValue(source, left, typeId, metricId),
                left,
                GetSortRelationshipMetricValue(source, right, typeId, metricId),
                right,
                descending);
        }
    }
}
