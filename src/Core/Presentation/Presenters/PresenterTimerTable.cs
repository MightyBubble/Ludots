using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// SoA named-timer storage for presenter-local visual sequencing (SC2 actor timer analogue).
    /// Render-dt clock only; timers belong to a presenter instance (keyed by its stable id)
    /// and are removed through the presenter destruction funnel.
    /// Lookups go through a pre-allocated open-addressing index on (stable id, name id);
    /// Set/Kill/Tick stay O(1)-per-entry with zero steady-state allocation.
    /// </summary>
    public sealed class PresenterTimerTable
    {
        private const ulong FibonacciHashMultiplier = 0x9E3779B97F4A7C15ul;

        private readonly int[] _ownerStableIds;
        private readonly Entity[] _presenters;
        private readonly Entity[] _owners;
        private readonly int[] _nameIds;
        private readonly float[] _remainingSeconds;

        private readonly Entity[] _expiredPresenters;
        private readonly Entity[] _expiredOwners;
        private readonly int[] _expiredNameIds;
        private readonly int[] _expiredStableIds;

        private readonly long[] _indexKeys;
        private readonly int[] _indexSlots;
        private readonly int _indexMask;
        private readonly int _indexShift;

        private uint _rngState;

        public PresenterTimerTable(int capacity, uint randomSeed = 0x9E3779B9u)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Presenter timer capacity must be > 0.");
            }

            Capacity = capacity;
            _ownerStableIds = new int[capacity];
            _presenters = new Entity[capacity];
            _owners = new Entity[capacity];
            _nameIds = new int[capacity];
            _remainingSeconds = new float[capacity];
            _expiredPresenters = new Entity[capacity];
            _expiredOwners = new Entity[capacity];
            _expiredNameIds = new int[capacity];
            _expiredStableIds = new int[capacity];

            int indexCapacity = 16;
            while (indexCapacity < capacity * 2)
            {
                indexCapacity <<= 1;
            }

            _indexKeys = new long[indexCapacity];
            _indexSlots = new int[indexCapacity];
            _indexMask = indexCapacity - 1;
            int shift = 64;
            for (int v = indexCapacity; v > 1; v >>= 1)
            {
                shift--;
            }

            _indexShift = shift;
            _rngState = randomSeed != 0 ? randomSeed : 0x9E3779B9u;
        }

        public int Capacity { get; }

        public int Count { get; private set; }

        public int ExpiredCount { get; private set; }

        public Entity GetExpiredPresenter(int index) => _expiredPresenters[index];

        public Entity GetExpiredOwner(int index) => _expiredOwners[index];

        public int GetExpiredNameId(int index) => _expiredNameIds[index];

        public int GetExpiredStableId(int index) => _expiredStableIds[index];

        public void Set(int ownerStableId, Entity presenter, Entity owner, int nameId, float durationSeconds, float durationRangeSeconds)
        {
            if (ownerStableId <= 0)
            {
                throw new InvalidOperationException($"TimerSet requires a positive presenter stable id, got {ownerStableId}.");
            }

            if (nameId <= 0)
            {
                throw new InvalidOperationException($"TimerSet requires a registered timer name id, got {nameId}.");
            }

            if (!float.IsFinite(durationSeconds) || durationSeconds <= 0f)
            {
                throw new InvalidOperationException($"TimerSet durationSeconds must be finite and > 0, got {durationSeconds}.");
            }

            if (!float.IsFinite(durationRangeSeconds) || durationRangeSeconds < 0f)
            {
                throw new InvalidOperationException($"TimerSet durationRangeSeconds must be finite and >= 0, got {durationRangeSeconds}.");
            }

            float duration = durationSeconds + durationRangeSeconds * Next01();
            long key = KeyOf(ownerStableId, nameId);
            int h = ProbeHome(key);
            while (true)
            {
                long existing = _indexKeys[h];
                if (existing == key)
                {
                    _remainingSeconds[_indexSlots[h]] = duration;
                    return;
                }

                if (existing == 0)
                {
                    break;
                }

                h = (h + 1) & _indexMask;
            }

            if (Count >= Capacity)
            {
                throw new InvalidOperationException($"PresenterTimerTable overflowed while setting timer nameId={nameId}; capacity={Capacity}.");
            }

            int slot = Count++;
            _ownerStableIds[slot] = ownerStableId;
            _presenters[slot] = presenter;
            _owners[slot] = owner;
            _nameIds[slot] = nameId;
            _remainingSeconds[slot] = duration;
            _indexKeys[h] = key;
            _indexSlots[h] = slot;
        }

        public bool Kill(int ownerStableId, int nameId)
        {
            int h = FindIndexSlot(KeyOf(ownerStableId, nameId));
            if (h < 0)
            {
                return false;
            }

            RemoveAt(_indexSlots[h]);
            return true;
        }

        public int KillAll(int ownerStableId)
        {
            int removed = 0;
            for (int i = Count - 1; i >= 0; i--)
            {
                if (_ownerStableIds[i] == ownerStableId)
                {
                    RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Advances every timer by <paramref name="dt"/> and moves expired entries into the
        /// expired scratch view. Expired entries are removed before the scratch is filled,
        /// so re-setting a timer from an expiry rule never collides with its predecessor.
        /// </summary>
        public int Tick(float dt)
        {
            ExpiredCount = 0;
            if (Count == 0)
            {
                return 0;
            }

            if (dt <= 0f)
            {
                return 0;
            }

            for (int i = Count - 1; i >= 0; i--)
            {
                _remainingSeconds[i] -= dt;
                if (_remainingSeconds[i] > 0f)
                {
                    continue;
                }

                int slot = ExpiredCount++;
                _expiredPresenters[slot] = _presenters[i];
                _expiredOwners[slot] = _owners[i];
                _expiredNameIds[slot] = _nameIds[i];
                _expiredStableIds[slot] = _ownerStableIds[i];
                RemoveAt(i);
            }

            return ExpiredCount;
        }

        private void RemoveAt(int index)
        {
            RemoveIndexEntry(KeyOf(_ownerStableIds[index], _nameIds[index]));
            int last = --Count;
            if (index == last)
            {
                return;
            }

            _ownerStableIds[index] = _ownerStableIds[last];
            _presenters[index] = _presenters[last];
            _owners[index] = _owners[last];
            _nameIds[index] = _nameIds[last];
            _remainingSeconds[index] = _remainingSeconds[last];

            int moved = FindIndexSlot(KeyOf(_ownerStableIds[index], _nameIds[index]));
            _indexSlots[moved] = index;
        }

        private static long KeyOf(int ownerStableId, int nameId) => ((long)ownerStableId << 32) | (uint)nameId;

        private int ProbeHome(long key) => (int)(((ulong)key * FibonacciHashMultiplier) >> _indexShift);

        private int FindIndexSlot(long key)
        {
            int h = ProbeHome(key);
            while (true)
            {
                long existing = _indexKeys[h];
                if (existing == 0)
                {
                    return -1;
                }

                if (existing == key)
                {
                    return h;
                }

                h = (h + 1) & _indexMask;
            }
        }

        // Backward-shift deletion keeps the probe run tombstone-free, so steady-state
        // Set/Kill churn never degrades lookup length.
        private void RemoveIndexEntry(long key)
        {
            int i = FindIndexSlot(key);
            if (i < 0)
            {
                throw new InvalidOperationException("PresenterTimerTable index desynchronized: key missing during removal.");
            }

            int j = i;
            while (true)
            {
                _indexKeys[i] = 0;
                _indexSlots[i] = 0;

                do
                {
                    j = (j + 1) & _indexMask;
                    if (_indexKeys[j] == 0)
                    {
                        return;
                    }
                }
                while (!CanMoveTo(i, j, ProbeHome(_indexKeys[j])));

                _indexKeys[i] = _indexKeys[j];
                _indexSlots[i] = _indexSlots[j];
                i = j;
            }
        }

        // An entry can fill the empty slot iff its home position lies outside the cyclic probe run (empty, current].
        private bool CanMoveTo(int empty, int current, int home)
        {
            return empty <= current
                ? home <= empty || home > current
                : home <= empty && home > current;
        }

        private uint NextUInt()
        {
            uint x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x != 0 ? x : 0x9E3779B9u;
            return _rngState;
        }

        private float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }
    }
}
