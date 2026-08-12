using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Capacity
{
    /// <summary>
    /// Session-scoped SoA columns sized by <see cref="GasLoadTimeCapacityPlan"/>.
    /// Attribute Base/Cap/Current live here; entity <c>AttributeBuffer</c> holds only a row id.
    /// Not a hot-path growable buffer — <see cref="EnsureEntityRowCapacity"/> is load/Schema only.
    /// </summary>
    public sealed class GasWorldColumnStore : IDisposable
    {
        private bool _disposed;
        private bool _gameplaySealed;
        private int[] _freeList = Array.Empty<int>();
        private int _freeCount;

        public GasLoadTimeCapacityPlan Plan { get; }
        public int EntityRowCapacity { get; private set; }
        public int EntityRowCount { get; private set; }
        public bool IsDisposed => _disposed;
        public bool IsGameplaySealed => _gameplaySealed;

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

            // Row ids are 1-based so default(AttributeBuffer).RowId == 0 stays InvalidRow.
            EntityRowCapacity = initialEntityRowCapacity;
            AllocateColumns(initialEntityRowCapacity);
            _freeList = initialEntityRowCapacity == 0 ? Array.Empty<int>() : new int[initialEntityRowCapacity];
            EntityRowCount = 0;
        }

        /// <summary>
        /// After seal, <see cref="EnsureEntityRowCapacity"/> throws. Call at end of load/Schema window.
        /// </summary>
        public void SealGameplay()
        {
            ThrowIfDisposed();
            _gameplaySealed = true;
        }

        /// <summary>
        /// Load/Schema window only. Must not be called from effect systems or after <see cref="SealGameplay"/>.
        /// </summary>
        public void EnsureEntityRowCapacity(int requiredRows)
        {
            ThrowIfDisposed();
            if (_gameplaySealed)
            {
                throw new InvalidOperationException(
                    "GasWorldColumnStore is gameplay-sealed; EnsureEntityRowCapacity is load/Schema only.");
            }

            if (requiredRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredRows));
            }

            if (requiredRows <= EntityRowCapacity)
            {
                return;
            }

            AllocateColumns(requiredRows);
            var grownFree = new int[requiredRows];
            if (_freeCount > 0)
            {
                Array.Copy(_freeList, grownFree, _freeCount);
            }

            _freeList = grownFree;
            EntityRowCapacity = requiredRows;
        }

        public int AllocateEntityRow()
        {
            ThrowIfDisposed();
            if (_freeCount > 0)
            {
                int reused = _freeList[--_freeCount];
                ClearAttributeRow(reused);
                return reused;
            }

            if (EntityRowCount >= EntityRowCapacity)
            {
                throw new InvalidOperationException(
                    $"GasWorldColumnStore row capacity {EntityRowCapacity} exhausted. " +
                    "Grow only in load/SchemaUpdate via EnsureEntityRowCapacity — never on the effect hot path.");
            }

            EntityRowCount++;
            return EntityRowCount; // 1-based
        }

        public void ReleaseEntityRow(int rowId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ClearAttributeRow(rowId);
            if (_freeCount >= _freeList.Length)
            {
                throw new InvalidOperationException(
                    "GasWorldColumnStore freelist overflow — ReleaseEntityRow called more times than AllocateEntityRow.");
            }

            _freeList[_freeCount++] = rowId;
        }

        public void CopyAttributeRow(int fromRowId, int toRowId)
        {
            ThrowIfDisposed();
            ValidateRowId(fromRowId);
            ValidateRowId(toRowId);
            int attrSlots = Plan.AttributeSlotCount;
            if (attrSlots > 0)
            {
                int fromBase = RowFloatOffset(fromRowId);
                int toBase = RowFloatOffset(toRowId);
                Array.Copy(AttributeBaseValues!, fromBase, AttributeBaseValues!, toBase, attrSlots);
                Array.Copy(AttributeCapValues!, fromBase, AttributeCapValues!, toBase, attrSlots);
                Array.Copy(AttributeCurrentValues!, fromBase, AttributeCurrentValues!, toBase, attrSlots);
            }

            int attrWords = AttrDefinedWordCount(attrSlots);
            if (attrWords > 0)
            {
                int fromWords = RowWordOffset(fromRowId, attrWords);
                int toWords = RowWordOffset(toRowId, attrWords);
                Array.Copy(AttributeDefinedWords!, fromWords, AttributeDefinedWords!, toWords, attrWords);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCurrent(int rowId, int attributeId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            return AttributeCurrentValues![RowFloatOffset(rowId) + attributeId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBase(int rowId, int attributeId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints) &&
                constraints.ClampCurrentToBase)
            {
                return AttributeCapValues![RowFloatOffset(rowId) + attributeId];
            }

            return AttributeBaseValues![RowFloatOffset(rowId) + attributeId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAttribute(int rowId, int attributeId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            int wordIndex = attributeId >> 6;
            int bitIndex = attributeId & 63;
            int wordsPerRow = AttrDefinedWordCount(Plan.AttributeSlotCount);
            return (AttributeDefinedWords![RowWordOffset(rowId, wordsPerRow) + wordIndex] & (1UL << bitIndex)) != 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBase(int rowId, int attributeId, float value)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            MarkDefined(rowId, attributeId);
            int index = RowFloatOffset(rowId) + attributeId;
            AttributeBaseValues![index] = value;
            AttributeCapValues![index] = value;
            SetCurrentInternal(rowId, attributeId, value, clampToCapacity: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCurrent(int rowId, int attributeId, float value)
        {
            SetCurrentInternal(rowId, attributeId, value, clampToCapacity: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAggregatedCurrent(int rowId, int attributeId, float value)
        {
            SetCurrentInternal(rowId, attributeId, value, clampToCapacity: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetRawBase(int rowId, int attributeId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            return AttributeBaseValues![RowFloatOffset(rowId) + attributeId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetRawCap(int rowId, int attributeId)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            return AttributeCapValues![RowFloatOffset(rowId) + attributeId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRawCap(int rowId, int attributeId, float value)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            MarkDefined(rowId, attributeId);
            AttributeCapValues![RowFloatOffset(rowId) + attributeId] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRawCurrentUnconstrained(int rowId, int attributeId, float value)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            AttributeCurrentValues![RowFloatOffset(rowId) + attributeId] = value;
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
            _freeList = Array.Empty<int>();
            _freeCount = 0;
            EntityRowCount = 0;
            EntityRowCapacity = 0;
            _disposed = true;
        }

        private void SetCurrentInternal(int rowId, int attributeId, float value, bool clampToCapacity)
        {
            ThrowIfDisposed();
            ValidateRowId(rowId);
            ValidateAttributeId(attributeId);
            MarkDefined(rowId, attributeId);
            if (AttributeRegistry.TryGetConstraints(attributeId, out var constraints))
            {
                if (clampToCapacity && constraints.ClampCurrentToBase)
                {
                    float max = GetBase(rowId, attributeId);
                    if (value > max) value = max;
                }

                if (constraints.HasMin && value < constraints.Min) value = constraints.Min;
                if (constraints.HasMax && value > constraints.Max) value = constraints.Max;
            }

            AttributeCurrentValues![RowFloatOffset(rowId) + attributeId] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkDefined(int rowId, int attributeId)
        {
            int wordIndex = attributeId >> 6;
            int bitIndex = attributeId & 63;
            int wordsPerRow = AttrDefinedWordCount(Plan.AttributeSlotCount);
            AttributeDefinedWords![RowWordOffset(rowId, wordsPerRow) + wordIndex] |= 1UL << bitIndex;
        }

        private void ClearAttributeRow(int rowId)
        {
            int attrSlots = Plan.AttributeSlotCount;
            if (attrSlots > 0)
            {
                int baseIndex = RowFloatOffset(rowId);
                Array.Clear(AttributeBaseValues!, baseIndex, attrSlots);
                Array.Clear(AttributeCapValues!, baseIndex, attrSlots);
                Array.Clear(AttributeCurrentValues!, baseIndex, attrSlots);
            }

            int attrWords = AttrDefinedWordCount(attrSlots);
            if (attrWords > 0)
            {
                Array.Clear(AttributeDefinedWords!, RowWordOffset(rowId, attrWords), attrWords);
            }
        }

        private void AllocateColumns(int rowCapacity)
        {
            int attrSlots = Plan.AttributeSlotCount;
            int attrWords = AttrDefinedWordCount(attrSlots);
            int tagWords = Plan.TagUlongWordCount;
            // Index 0 unused (InvalidRow); live rows occupy 1..rowCapacity.
            int floatLen = attrSlots == 0 ? 0 : (rowCapacity + 1) * attrSlots;
            int definedLen = attrWords == 0 ? 0 : (rowCapacity + 1) * attrWords;
            int tagLen = tagWords == 0 ? 0 : (rowCapacity + 1) * tagWords;

            var newBase = floatLen == 0 ? Array.Empty<float>() : new float[floatLen];
            var newCap = floatLen == 0 ? Array.Empty<float>() : new float[floatLen];
            var newCurrent = floatLen == 0 ? Array.Empty<float>() : new float[floatLen];
            var newDefined = definedLen == 0 ? Array.Empty<ulong>() : new ulong[definedLen];
            var newTags = tagLen == 0 ? Array.Empty<ulong>() : new ulong[tagLen];

            if (AttributeBaseValues != null && attrSlots > 0 && EntityRowCount > 0)
            {
                int copyFloats = (EntityRowCount + 1) * attrSlots;
                Array.Copy(AttributeBaseValues, newBase, Math.Min(copyFloats, AttributeBaseValues.Length));
                Array.Copy(AttributeCapValues!, newCap, Math.Min(copyFloats, AttributeCapValues!.Length));
                Array.Copy(AttributeCurrentValues!, newCurrent, Math.Min(copyFloats, AttributeCurrentValues!.Length));
            }

            if (AttributeDefinedWords != null && attrWords > 0 && EntityRowCount > 0)
            {
                int copyWords = (EntityRowCount + 1) * attrWords;
                Array.Copy(AttributeDefinedWords, newDefined, Math.Min(copyWords, AttributeDefinedWords.Length));
            }

            if (TagBitWords != null && tagWords > 0 && EntityRowCount > 0)
            {
                int copyTags = (EntityRowCount + 1) * tagWords;
                Array.Copy(TagBitWords, newTags, Math.Min(copyTags, TagBitWords.Length));
            }

            AttributeBaseValues = newBase;
            AttributeCapValues = newCap;
            AttributeCurrentValues = newCurrent;
            AttributeDefinedWords = newDefined;
            TagBitWords = newTags;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int RowFloatOffset(int rowId) => rowId * Plan.AttributeSlotCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RowWordOffset(int rowId, int wordsPerRow) => rowId * wordsPerRow;

        public static int AttrDefinedWordCount(int attributeSlotCount)
        {
            if (attributeSlotCount <= 0)
            {
                return 0;
            }

            return (attributeSlotCount + GasLoadTimeCapacityPlan.TagBitsPerWord - 1) /
                   GasLoadTimeCapacityPlan.TagBitsPerWord;
        }

        public static int AttrDirtyWordCount(int attributeSlotCount) => AttrDefinedWordCount(attributeSlotCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateRowId(int rowId)
        {
            // 1-based ids in [1, EntityRowCapacity]
            if (rowId <= 0 || rowId > EntityRowCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(rowId), rowId, "Row id is outside store capacity.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateAttributeId(int attributeId)
        {
            if ((uint)attributeId >= (uint)Plan.AttributeSlotCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attributeId),
                    attributeId,
                    $"attributeId must be in [0, {Plan.AttributeSlotCount - 1}] for frozen plan.");
            }
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
