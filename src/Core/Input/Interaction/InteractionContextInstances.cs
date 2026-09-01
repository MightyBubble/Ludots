using System;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Entity-mounted set of interaction context instances (#1398 S2b, constitution §8.2):
    /// the coexisting set — a base instance (mounted by the exec/cast/spawn chains as the
    /// single-slot <see cref="InteractionContextInstance"/> component) plus the instances
    /// activated on top of it by the <c>ActivateContext</c> graph op. Present on the
    /// interaction subject only while at least one such instance is active, absent otherwise.
    /// Plain inline data (no references), so save round trips only need the same install
    /// order. Capacity is fixed and small (constitution examples: aim while moving);
    /// overflowing it fails fast instead of growing silently.
    /// </summary>
    public struct InteractionContextInstances
    {
        public const int Capacity = 4;

        public int Count;

        private InteractionContextInstance _instance0;
        private InteractionContextInstance _instance1;
        private InteractionContextInstance _instance2;
        private InteractionContextInstance _instance3;

        public readonly InteractionContextInstance this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return index switch
                {
                    0 => _instance0,
                    1 => _instance1,
                    2 => _instance2,
                    _ => _instance3,
                };
            }
        }

        /// <summary>Writes one storage slot; the caller owns <see cref="Count"/> bookkeeping.</summary>
        public void Set(int index, in InteractionContextInstance instance)
        {
            if ((uint)index >= (uint)Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            switch (index)
            {
                case 0: _instance0 = instance; break;
                case 1: _instance1 = instance; break;
                case 2: _instance2 = instance; break;
                default: _instance3 = instance; break;
            }
        }

        public void Add(in InteractionContextInstance instance)
        {
            if (Count >= Capacity)
            {
                throw new InvalidOperationException(
                    $"InteractionContextInstances capacity {Capacity} exceeded on entity; context instances are a small coexisting set and overflowing one is a configuration error.");
            }

            Set(Count, instance);
            Count++;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            for (int i = index; i < Count - 1; i++)
            {
                Set(i, this[i + 1]);
            }

            Count--;
            Set(Count, default);
        }

        /// <summary>Index of the instance carrying <paramref name="contextId"/>; -1 when absent.</summary>
        public readonly int IndexOf(int contextId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (this[i].ContextId == contextId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
