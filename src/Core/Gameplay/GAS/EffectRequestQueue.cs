using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public struct EffectRequest
    {
        public int RootId;
        public Entity Source;
        public Entity Target;
        public Entity TargetContext;
        public int TemplateId;

        /// <summary>
        /// Optional caller-supplied parameter overrides.
        /// Merged into template ConfigParams at runtime (caller wins on key conflict).
        /// Replaces the legacy F0-F3/I0-I1 untyped payload fields.
        /// </summary>
        public EffectConfigParams CallerParams;
        public bool HasCallerParams;
    }

    public sealed class EffectRequestQueue
    {
        private EffectRequest[] _items;
        private int _count;
        private int _nextRootId = 1;

        private EffectRequest[] _overflow;
        private int _overflowHead;
        private int _overflowTail;
        private int _overflowCount;
        private int _dropped;
        private bool _budgetFused;

        public EffectRequestQueue(int initialCapacity = GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)
        {
            int capacity = initialCapacity;
            if (capacity < GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME) capacity = GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME;
            _items = new EffectRequest[capacity];
            _overflow = new EffectRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int OverflowCount => _overflowCount;
        public int DroppedCount => _dropped;
        public bool BudgetFused => _budgetFused;

        public EffectRequest this[int index] => _items[index];

        public void Reserve(int capacity)
        {
            if (capacity <= _items.Length) return;

            var newItems = new EffectRequest[capacity];
            System.Array.Copy(_items, 0, newItems, 0, _count);
            _items = newItems;

            var newOverflow = new EffectRequest[capacity];
            int take = _overflowCount;
            for (int i = 0; i < take; i++)
            {
                newOverflow[i] = _overflow[_overflowHead];
                _overflowHead++;
                if (_overflowHead == _overflow.Length) _overflowHead = 0;
            }
            _overflow = newOverflow;
            _overflowHead = 0;
            _overflowTail = take;
            _overflowCount = take;
        }

        public void Publish(in EffectRequest req)
        {
            var r = req;
            if (r.RootId == 0) r.RootId = _nextRootId++;

            if (_count < _items.Length)
            {
                _items[_count++] = r;
                return;
            }

            if (!_budgetFused)
            {
                _budgetFused = true;
            }

            if (_overflowCount >= _overflow.Length)
            {
                _dropped++;
                return;
            }

            _overflow[_overflowTail] = r;
            _overflowTail++;
            if (_overflowTail == _overflow.Length) _overflowTail = 0;
            _overflowCount++;
        }

        public void Clear()
        {
            _count = 0;
            RefillFromOverflow();
        }

        public void ConsumePrefix(int count)
        {
            if (count <= 0) return;
            if (count >= _count)
            {
                _count = 0;
                RefillFromOverflow();
                return;
            }

            int remaining = _count - count;
            System.Array.Copy(_items, count, _items, 0, remaining);
            _count = remaining;

            RefillFromOverflow();
        }

        private void RefillFromOverflow()
        {
            int space = _items.Length - _count;
            if (space <= 0) return;
            if (_overflowCount <= 0) return;

            int take = _overflowCount < space ? _overflowCount : space;
            for (int i = 0; i < take; i++)
            {
                _items[_count++] = _overflow[_overflowHead];
                _overflowHead++;
                if (_overflowHead == _overflow.Length) _overflowHead = 0;
            }
            _overflowCount -= take;
            if (_overflowCount == 0)
            {
                _overflowHead = 0;
                _overflowTail = 0;
            }
        }
    }
}
