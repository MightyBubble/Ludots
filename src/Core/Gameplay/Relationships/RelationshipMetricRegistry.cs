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

        public string? GetName(int id) => _ids.GetName(id);

        /// <summary>#1570：metric 词汇按名惰性映射到 AttributeRegistry（同名同 id）。
        /// 不做注册副作用——静态属性表属于引擎装配/测试重置生命周期，缓存 id 会随
        /// ReplaceAttributes 失效成陈旧映射；每次按名解析，未知则放弃写穿。</summary>
        public bool TryGetAttributeId(int metricId, out int attributeId)
        {
            string? name = GetName(metricId);
            if (string.IsNullOrEmpty(name))
            {
                attributeId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
                return false;
            }

            attributeId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(name);
            return attributeId != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        }

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
