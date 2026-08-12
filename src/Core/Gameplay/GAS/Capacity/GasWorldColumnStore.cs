using System;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    /// <summary>
    /// P0 scaffold for session-scoped SoA columns sized by <see cref="GasLoadTimeCapacityPlan"/>.
    /// P1 wires attribute Base/Cap/Current; P2 wires tag bit words. Not a hot-path growable buffer.
    /// </summary>
    public sealed class GasWorldColumnStore : IDisposable
    {
        private bool _disposed;

        public GasLoadTimeCapacityPlan Plan { get; }
        public int EntityRowCapacity { get; private set; }
        public int EntityRowCount { get; private set; }

        public float[]? AttributeBaseValues { get; private set; }
        public float[]? AttributeCapValues { get; private set; }
        public float[]? AttributeCurrentValues { get; private set; }
        public ulong[]? AttributeDefinedWords { get; private set; }

        public ulong[]? TagBitWords { get; private set; }

        public GasWorldColumnStore(GasLoadTimeCapacityPlan plan, int initialEntityRowCapacity)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (initialEntityRowCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialEntityRowCapacity));
            }

            EntityRowCapacity = initialEntityRowCapacity;
            AllocateColumns(initialEntityRowCapacity);
        }

        /// <summary>
        /// Load/Schema window only. Gameplay ticks must not call this.
        /// </summary>
        public void EnsureEntityRowCapacity(int requiredRows)
        {
            ThrowIfDisposed();
            if (requiredRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredRows));
            }

            if (requiredRows <= EntityRowCapacity)
            {
                return;
            }

            AllocateColumns(requiredRows);
            EntityRowCapacity = requiredRows;
        }

        public int AllocateEntityRow()
        {
            ThrowIfDisposed();
            if (EntityRowCount >= EntityRowCapacity)
            {
                throw new InvalidOperationException(
                    $"GasWorldColumnStore row capacity {EntityRowCapacity} exhausted. " +
                    "Grow only in load/SchemaUpdate via EnsureEntityRowCapacity — never on the effect hot path.");
            }

            return EntityRowCount++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            AttributeBaseValues = null;
            AttributeCapValues = null;
            AttributeCurrentValues = null;
            AttributeDefinedWords = null;
            TagBitWords = null;
            EntityRowCount = 0;
            EntityRowCapacity = 0;
            _disposed = true;
        }

        private void AllocateColumns(int rowCapacity)
        {
            int attrSlots = Plan.AttributeSlotCount;
            int attrWords = AttrDefinedWordCount(attrSlots);
            int tagWords = Plan.TagUlongWordCount;

            AttributeBaseValues = attrSlots == 0 ? Array.Empty<float>() : new float[rowCapacity * attrSlots];
            AttributeCapValues = attrSlots == 0 ? Array.Empty<float>() : new float[rowCapacity * attrSlots];
            AttributeCurrentValues = attrSlots == 0 ? Array.Empty<float>() : new float[rowCapacity * attrSlots];
            AttributeDefinedWords = attrWords == 0 ? Array.Empty<ulong>() : new ulong[rowCapacity * attrWords];
            TagBitWords = tagWords == 0 ? Array.Empty<ulong>() : new ulong[rowCapacity * tagWords];
        }

        public static int AttrDefinedWordCount(int attributeSlotCount)
        {
            if (attributeSlotCount <= 0)
            {
                return 0;
            }

            return (attributeSlotCount + GasLoadTimeCapacityPlan.TagBitsPerWord - 1) /
                   GasLoadTimeCapacityPlan.TagBitsPerWord;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GasWorldColumnStore));
            }
        }
    }
}
