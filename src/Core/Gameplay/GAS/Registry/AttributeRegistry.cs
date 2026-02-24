using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    /// <summary>
    /// Maps Attribute names to integer IDs.
    /// Used by configuration system to resolve string keys to high-performance indices.
    /// </summary>
    public static class AttributeRegistry
    {
        private static readonly Dictionary<string, int> _nameToId = new();
        private static readonly Dictionary<int, string> _idToName = new();
        private static readonly AttributeConstraints[] _constraints = new AttributeConstraints[MaxAttributes];
        private static int _nextId = 0;
        private static bool _frozen;

        public const int InvalidId = -1;
        public const int MaxAttributes = 64;

        public static bool IsFrozen => _frozen;

        public static void Freeze()
        {
            _frozen = true;
        }

        public static int Register(string name)
        {
            if (_frozen)
            {
                throw new System.InvalidOperationException("AttributeRegistry is frozen.");
            }

            if (_nameToId.TryGetValue(name, out var id))
            {
                return id;
            }

            if (_nextId >= MaxAttributes)
            {
                throw new System.InvalidOperationException($"AttributeBuffer supports up to {MaxAttributes} attributes.");
            }

            id = _nextId++;
            _nameToId[name] = id;
            _idToName[id] = name;
            return id;
        }

        public static int GetId(string name)
        {
            return _nameToId.TryGetValue(name, out var id) ? id : InvalidId;
        }

        public static string GetName(int id)
        {
            return _idToName.TryGetValue(id, out var name) ? name : string.Empty;
        }

        public static bool TryGetConstraints(int attributeId, out AttributeConstraints constraints)
        {
            constraints = default;
            if ((uint)attributeId >= (uint)MaxAttributes) return false;
            constraints = _constraints[attributeId];
            return constraints.HasAny;
        }

        public static void SetConstraints(int attributeId, in AttributeConstraints constraints)
        {
            if ((uint)attributeId >= (uint)MaxAttributes) return;
            _constraints[attributeId] = constraints;
        }

        public static void SetConstraints(string attributeName, in AttributeConstraints constraints)
        {
            if (string.IsNullOrEmpty(attributeName)) return;
            int id = GetId(attributeName);
            if (id == InvalidId)
            {
                id = Register(attributeName);
            }
            SetConstraints(id, in constraints);
        }

        public readonly struct AttributeConstraints
        {
            public readonly bool HasAny;
            public readonly bool ClampCurrentToBase;
            public readonly bool HasMin;
            public readonly float Min;
            public readonly bool HasMax;
            public readonly float Max;

            private AttributeConstraints(bool clampToBase, bool hasMin, float min, bool hasMax, float max)
            {
                ClampCurrentToBase = clampToBase;
                HasMin = hasMin;
                Min = min;
                HasMax = hasMax;
                Max = max;
                HasAny = clampToBase || hasMin || hasMax;
            }

            public static AttributeConstraints ClampToBase(float min = 0f)
            {
                return new AttributeConstraints(clampToBase: true, hasMin: true, min: min, hasMax: false, max: 0f);
            }

            public static AttributeConstraints Create(bool clampToBase, bool hasMin, float min, bool hasMax, float max)
            {
                return new AttributeConstraints(clampToBase: clampToBase, hasMin: hasMin, min: min, hasMax: hasMax, max: max);
            }
        }
    }
}
