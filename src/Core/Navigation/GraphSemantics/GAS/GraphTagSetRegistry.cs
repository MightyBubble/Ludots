using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Navigation.GraphCore;

namespace Ludots.Core.Navigation.GraphSemantics.GAS
{
    public sealed class GraphTagSetRegistry
    {
        private TagBits256[] _pool;
        private ushort _count;
        private readonly Dictionary<TagBits256, ushort> _map;

        public GraphTagSetRegistry(int initialCapacity = 256)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            _pool = new TagBits256[initialCapacity];
            _pool[0] = default;
            _count = 1;
            _map = new Dictionary<TagBits256, ushort>(initialCapacity);
            _map[default] = 0;
        }

        public TagBits256[] Pool => _pool;

        public void Clear()
        {
            _map.Clear();
            _pool = new TagBits256[1];
            _pool[0] = default;
            _count = 1;
            _map[default] = 0;
        }

        public ushort GetOrAdd(in TagBits256 bits)
        {
            if (_map.TryGetValue(bits, out ushort id)) return id;
            id = _count++;
            if (id == 0) throw new InvalidOperationException("TagSetId overflow.");
            EnsureCapacity(id + 1);
            _pool[id] = bits;
            _map[bits] = id;
            return id;
        }

        public ushort GetOrAddFromTagIds(ReadOnlySpan<int> tagIds)
        {
            var bits = TagBitsFromIds(tagIds);
            return GetOrAdd(in bits);
        }

        public unsafe ushort GetOrAddFromContainer(in GameplayTagContainer container)
        {
            var bits = new TagBits256(container.Bits[0], container.Bits[1], container.Bits[2], container.Bits[3]);
            return GetOrAdd(in bits);
        }

        public static TagBits256 TagBitsFromIds(ReadOnlySpan<int> tagIds)
        {
            ulong u0 = 0;
            ulong u1 = 0;
            ulong u2 = 0;
            ulong u3 = 0;

            for (int i = 0; i < tagIds.Length; i++)
            {
                int tagId = tagIds[i];
                if ((uint)tagId >= 256u) continue;
                int index = tagId >> 6;
                int bit = tagId & 63;
                ulong mask = 1UL << bit;
                switch (index)
                {
                    case 0: u0 |= mask; break;
                    case 1: u1 |= mask; break;
                    case 2: u2 |= mask; break;
                    case 3: u3 |= mask; break;
                }
            }

            return new TagBits256(u0, u1, u2, u3);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _pool.Length) return;
            int newCap = _pool.Length * 2;
            if (newCap < required) newCap = required;
            Array.Resize(ref _pool, newCap);
        }
    }
}

