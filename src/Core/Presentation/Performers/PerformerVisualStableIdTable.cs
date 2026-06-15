using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Presentation;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerVisualStableIdTable
    {
        private const float MaxLoadFactor = 0.7f;

        private readonly PresentationStableIdAllocator _allocator;
        private readonly PerformerVisualStableKey[] _keys;
        private readonly int[] _stableIds;
        private readonly byte[] _occupied;
        private readonly int _mask;
        private readonly int _maxEntries;
        private int _count;

        public int Count => _count;
        public int Capacity => _keys.Length;
        public int MaxEntries => _maxEntries;

        public PerformerVisualStableIdTable(PresentationStableIdAllocator allocator, int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
            int size = CeilPow2(Math.Max(8, (int)(capacity / MaxLoadFactor) + 1));
            _keys = new PerformerVisualStableKey[size];
            _stableIds = new int[size];
            _occupied = new byte[size];
            _mask = size - 1;
            _maxEntries = Math.Max(1, (int)(size * MaxLoadFactor));
        }

        public int GetOrAllocate(in PerformerVisualStableKey key)
        {
            ValidateKey(in key);
            int slot = FindSlot(in key, out bool found);
            if (found)
            {
                return _stableIds[slot];
            }

            if (_count + 1 > _maxEntries)
            {
                throw new InvalidOperationException(
                    $"PerformerVisualStableIdTable capacity exhausted. count={_count}, maxEntries={_maxEntries}, " +
                    $"performerStableId={key.PerformerStableId}, slotIndex={key.SlotIndex}, assetKind={key.AssetKind}, discriminator={key.Discriminator}.");
            }

            _keys[slot] = key;
            _stableIds[slot] = _allocator.Allocate();
            _occupied[slot] = 1;
            _count++;
            return _stableIds[slot];
        }

        public bool TryGet(in PerformerVisualStableKey key, out int stableId)
        {
            if (!IsValidKey(in key))
            {
                stableId = 0;
                return false;
            }

            int slot = FindSlot(in key, out bool found);
            if (!found)
            {
                stableId = 0;
                return false;
            }

            stableId = _stableIds[slot];
            return true;
        }

        public bool Remove(in PerformerVisualStableKey key, out int stableId)
        {
            if (!IsValidKey(in key))
            {
                stableId = 0;
                return false;
            }

            int slot = FindSlot(in key, out bool found);
            if (!found)
            {
                stableId = 0;
                return false;
            }

            RemoveAt(slot, out stableId);
            return true;
        }

        public int ReleasePerformer(int performerStableId)
        {
            if (performerStableId <= 0)
            {
                return 0;
            }

            int released = 0;
            for (int slot = 0; slot < _occupied.Length;)
            {
                if (_occupied[slot] != 0 && _keys[slot].PerformerStableId == performerStableId)
                {
                    RemoveAt(slot, out _);
                    released++;
                    continue;
                }

                slot++;
            }

            return released;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindSlot(in PerformerVisualStableKey key, out bool found)
        {
            int slot = (int)key.Hash() & _mask;
            while (true)
            {
                if (_occupied[slot] == 0)
                {
                    found = false;
                    return slot;
                }

                if (_keys[slot].Equals(key))
                {
                    found = true;
                    return slot;
                }

                slot = (slot + 1) & _mask;
            }
        }

        private void RemoveAt(int slot, out int stableId)
        {
            stableId = _stableIds[slot];
            ClearSlot(slot);
            _count--;

            int next = (slot + 1) & _mask;
            while (_occupied[next] != 0)
            {
                PerformerVisualStableKey movedKey = _keys[next];
                int movedStableId = _stableIds[next];
                ClearSlot(next);
                _count--;
                InsertExisting(in movedKey, movedStableId);
                next = (next + 1) & _mask;
            }
        }

        private void InsertExisting(in PerformerVisualStableKey key, int stableId)
        {
            int slot = FindSlot(in key, out bool found);
            if (found)
            {
                throw new InvalidOperationException("PerformerVisualStableIdTable internal reinsert encountered a duplicate key.");
            }

            _keys[slot] = key;
            _stableIds[slot] = stableId;
            _occupied[slot] = 1;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearSlot(int slot)
        {
            _keys[slot] = default;
            _stableIds[slot] = 0;
            _occupied[slot] = 0;
        }

        private static void ValidateKey(in PerformerVisualStableKey key)
        {
            if (!IsValidKey(in key))
            {
                throw new InvalidOperationException(
                    $"Performer visual stable identity requires positive performerStableId, non-negative slotIndex, and a non-zero assetKind. " +
                    $"Got performerStableId={key.PerformerStableId}, slotIndex={key.SlotIndex}, assetKind={key.AssetKind}, discriminator={key.Discriminator}.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidKey(in PerformerVisualStableKey key)
        {
            return key.PerformerStableId > 0 &&
                   key.SlotIndex >= 0 &&
                   key.AssetKind != default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CeilPow2(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value < 8 ? 8 : value;
        }
    }
}
