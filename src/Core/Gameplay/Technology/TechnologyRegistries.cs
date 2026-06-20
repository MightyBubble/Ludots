using System;

namespace Ludots.Core.Gameplay.Technology
{
    public sealed class TechnologyDefinitionRegistry
    {
        private const int Capacity = 4096;
        private readonly TechnologyDefinition[] _items = new TechnologyDefinition[Capacity];
        private readonly ulong[] _hasBits = new ulong[Capacity >> 6];

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
        }

        public void Register(int technologyId, in TechnologyDefinition definition)
        {
            if ((uint)technologyId >= Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(technologyId));
            }

            _items[technologyId] = definition;
            _hasBits[technologyId >> 6] |= 1UL << (technologyId & 63);
        }

        public bool TryGet(int technologyId, out TechnologyDefinition definition)
        {
            if ((uint)technologyId >= Capacity ||
                (_hasBits[technologyId >> 6] & (1UL << (technologyId & 63))) == 0)
            {
                definition = default;
                return false;
            }

            definition = _items[technologyId];
            return true;
        }
    }

    public sealed class TechnologyRequirementRegistry
    {
        private const int Capacity = 4096;
        private readonly TechnologyRequirementDefinition?[] _items = new TechnologyRequirementDefinition?[Capacity];

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
        }

        public void Register(int requirementId, TechnologyRequirementDefinition definition)
        {
            if ((uint)requirementId >= Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(requirementId));
            }

            _items[requirementId] = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool TryGet(int requirementId, out TechnologyRequirementDefinition definition)
        {
            if ((uint)requirementId >= Capacity || _items[requirementId] == null)
            {
                definition = null!;
                return false;
            }

            definition = _items[requirementId]!;
            return true;
        }
    }
}
