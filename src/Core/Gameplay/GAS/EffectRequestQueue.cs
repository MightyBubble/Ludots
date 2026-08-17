using System;
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
        /// </summary>
        public EffectConfigParams CallerParams;
        public bool HasCallerParams;
    }

    public sealed class EffectRequestQueue
    {
        public const string CapacityExceededError = "GAS.EFFECT_REQUEST_QUEUE.ERR.CapacityExceeded";

        private EffectRequest[] _items;
        private int _count;
        private int _nextRootId = 1;

        private EffectRequest[] _overflow;
        private int _overflowHead;
        private int _overflowTail;
        private int _overflowCount;
        private int _responseChainListenerRevision;
        private World? _responseChainListenerWorld;

        public EffectRequestQueue(int initialCapacity = GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "EffectRequestQueue capacity must be positive.");
            }

            _items = new EffectRequest[initialCapacity];
            _overflow = new EffectRequest[initialCapacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int TotalCapacity => _items.Length + _overflow.Length;
        public int AvailableCapacity => (_items.Length - _count) + (_overflow.Length - _overflowCount);
        public int OverflowCount => _overflowCount;
        public int ResponseChainListenerRevision => _responseChainListenerRevision;

        internal readonly struct WriteCheckpoint
        {
            internal readonly int Count;
            internal readonly int NextRootId;
            internal readonly int OverflowHead;
            internal readonly int OverflowTail;
            internal readonly int OverflowCount;

            internal WriteCheckpoint(
                int count,
                int nextRootId,
                int overflowHead,
                int overflowTail,
                int overflowCount)
            {
                Count = count;
                NextRootId = nextRootId;
                OverflowHead = overflowHead;
                OverflowTail = overflowTail;
                OverflowCount = overflowCount;
            }
        }

        public EffectRequest this[int index] => _items[index];

        internal WriteCheckpoint CaptureWriteCheckpoint()
        {
            return new WriteCheckpoint(
                _count,
                _nextRootId,
                _overflowHead,
                _overflowTail,
                _overflowCount);
        }

        internal void RollbackWrites(in WriteCheckpoint checkpoint)
        {
            if (_count < checkpoint.Count ||
                _overflowHead != checkpoint.OverflowHead ||
                _overflowCount < checkpoint.OverflowCount)
            {
                throw new InvalidOperationException("GAS.EFFECT_REQUEST.ERR.InvalidWriteRollback");
            }

            _count = checkpoint.Count;
            _nextRootId = checkpoint.NextRootId;
            _overflowHead = checkpoint.OverflowHead;
            _overflowTail = checkpoint.OverflowTail;
            _overflowCount = checkpoint.OverflowCount;
        }

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

        public int AllocateRootId()
        {
            return _nextRootId++;
        }

        public void Publish(in EffectRequest req)
        {
            var r = req;
            if (r.RootId == 0) r.RootId = AllocateRootId();

            if (_count < _items.Length)
            {
                _items[_count++] = r;
                return;
            }

            if (_overflowCount >= _overflow.Length)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: capacity={_items.Length}, overflowCapacity={_overflow.Length}, requestedTemplateId={r.TemplateId}, rootId={r.RootId}.");
            }

            _overflow[_overflowTail] = r;
            _overflowTail++;
            if (_overflowTail == _overflow.Length) _overflowTail = 0;
            _overflowCount++;
        }

        public void RequireAvailable(int needed, string source)
        {
            if (needed < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(needed));
            }

            if (AvailableCapacity >= needed)
            {
                return;
            }

            throw new System.InvalidOperationException(
                $"{CapacityExceededError}: source={source}, needed={needed}, available={AvailableCapacity}, capacity={_items.Length}, overflowCapacity={_overflow.Length}.");
        }

        public void Clear()
        {
            _count = 0;
            _overflowHead = 0;
            _overflowTail = 0;
            _overflowCount = 0;
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

        internal void NotifyResponseChainListenersChanged()
        {
            unchecked
            {
                _responseChainListenerRevision++;
            }
        }

        internal void TrackResponseChainListenerLifecycle(World world)
        {
            if (world == null)
            {
                throw new System.ArgumentNullException(nameof(world));
            }

            if (ReferenceEquals(_responseChainListenerWorld, world))
            {
                return;
            }

            if (_responseChainListenerWorld != null)
            {
                throw new System.InvalidOperationException(
                    "GAS.RESPONSE_CHAIN.ERR.ListenerWorldAlreadyBound");
            }

            _responseChainListenerWorld = world;
            world.SubscribeEntityDestroyed(OnResponseChainEntityDestroyed);
        }

        private void OnResponseChainEntityDestroyed(in Entity entity)
        {
            World world = _responseChainListenerWorld!;
            if (world.Has<ResponseChainListener>(entity))
            {
                NotifyResponseChainListenersChanged();
            }
        }
    }
}
