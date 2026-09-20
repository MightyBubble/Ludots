using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed class RelationshipMetricRegistry
    {
        private readonly StringIntRegistry _ids = new(capacity: 16, startId: 0, invalidId: -1, comparer: StringComparer.Ordinal);
        private RelationshipMetricDefinition[] _definitions = new RelationshipMetricDefinition[16];
        private int _count;

        public int Count => _count;

        public int Register(string name, short minValue = -100, short maxValue = 100, short defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Relationship metric name must not be empty.", nameof(name));
            }

            if (_ids.TryGetId(name, out int existingId))
            {
                return existingId;
            }

            if (minValue > maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(minValue), "Relationship metric min value must be <= max value.");
            }

            if (defaultValue < minValue || defaultValue > maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultValue), "Relationship metric default value must be within min/max bounds.");
            }

            int id = _count;
            _ids.Register(name);
            EnsureDefinitionCapacity(id + 1);
            _definitions[id] = new RelationshipMetricDefinition(id, name, minValue, maxValue, defaultValue);
            _count++;
            return id;
        }

        public bool TryGetId(string name, out int id) => _ids.TryGetId(name, out id);

        /// <summary>#1570 单轨化：metric 词汇是 AttributeRegistry 的别名——注册同名属性，
        /// metricId→attributeId 的映射供写穿边实体 AttributeBuffer（唯一真相）使用。</summary>
        private readonly Dictionary<int, int> _attributeIds = new();

        public void RegisterAliasAttribute(int metricId, string name)
        {
            int attributeId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(name);
            _attributeIds[metricId] = attributeId;
        }

        public bool TryGetAttributeId(int metricId, out int attributeId)
            => _attributeIds.TryGetValue(metricId, out attributeId!);

        public int GetId(string name)
        {
            if (!_ids.TryGetId(name, out int id))
            {
                throw new InvalidOperationException($"Unknown relationship metric '{name}'.");
            }

            return id;
        }

        public ref readonly RelationshipMetricDefinition Get(int id)
        {
            if ((uint)id >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(id), $"Relationship metric id {id} is not registered.");
            }

            return ref _definitions[id];
        }

        private void EnsureDefinitionCapacity(int requiredCount)
        {
            if (requiredCount <= _definitions.Length)
            {
                return;
            }

            int newLength = Math.Max(_definitions.Length * 2, requiredCount);
            Array.Resize(ref _definitions, newLength);
        }
    }
}
