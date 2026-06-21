using System;

namespace Ludots.Core.Gameplay.Progression
{
    public sealed class ProgressionDefinitionRegistry
    {
        private const int Capacity = 4096;
        private readonly ProgressionDefinition[] _items = new ProgressionDefinition[Capacity];
        private readonly ulong[] _hasBits = new ulong[Capacity >> 6];

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            Array.Clear(_hasBits, 0, _hasBits.Length);
        }

        public void Register(int progressionId, in ProgressionDefinition definition)
        {
            if ((uint)progressionId >= Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(progressionId));
            }

            _items[progressionId] = definition;
            _hasBits[progressionId >> 6] |= 1UL << (progressionId & 63);
        }

        public bool TryGet(int progressionId, out ProgressionDefinition definition)
        {
            if ((uint)progressionId >= Capacity ||
                (_hasBits[progressionId >> 6] & (1UL << (progressionId & 63))) == 0)
            {
                definition = default;
                return false;
            }

            definition = _items[progressionId];
            return true;
        }
    }

    public sealed class ProgressionRequirementRegistry
    {
        private const int Capacity = 4096;
        private readonly ProgressionRequirementDefinition?[] _items = new ProgressionRequirementDefinition?[Capacity];

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
        }

        public void Register(int requirementId, ProgressionRequirementDefinition definition)
        {
            if ((uint)requirementId >= Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(requirementId));
            }

            _items[requirementId] = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool TryGet(int requirementId, out ProgressionRequirementDefinition definition)
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
