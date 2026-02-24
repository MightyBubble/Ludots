using System;

namespace Ludots.Core.Gameplay.GAS
{
    public readonly struct GasConditionHandle
    {
        public readonly int Id;
        public GasConditionHandle(int id) => Id = id;
        public bool IsValid => Id > 0;
    }

    public readonly struct GasCondition
    {
        public readonly GasConditionKind Kind;
        public readonly int TagId;
        public readonly TagSense TagSense;

        public GasCondition(GasConditionKind kind, int tagId, TagSense tagSense)
        {
            Kind = kind;
            TagId = tagId;
            TagSense = tagSense;
        }
    }

    public sealed class GasConditionRegistry
    {
        public const int MaxConditions = 4096;

        private readonly GasCondition[] _conditions = new GasCondition[MaxConditions];
        private int _nextId = 1;

        public void Clear()
        {
            Array.Clear(_conditions, 0, _conditions.Length);
            _nextId = 1;
        }

        public GasConditionHandle Register(in GasCondition condition)
        {
            int id = _nextId++;
            if ((uint)id >= MaxConditions) throw new InvalidOperationException($"GasConditionRegistry exceeded max {MaxConditions}.");
            _conditions[id] = condition;
            return new GasConditionHandle(id);
        }

        public ref readonly GasCondition Get(in GasConditionHandle handle)
        {
            if (!handle.IsValid || (uint)handle.Id >= MaxConditions) throw new ArgumentOutOfRangeException(nameof(handle));
            return ref _conditions[handle.Id];
        }
    }
}
