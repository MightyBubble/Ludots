using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Entity-side handle into session <see cref="GasWorldColumnStore"/> attribute columns.
    /// Capacity truth is the frozen plan / world store — not an embedded float array.
    /// </summary>
    public struct AttributeBuffer
    {
        /// <summary>0 matches default(AttributeBuffer) so archetype Create without Attach fails closed.</summary>
        public const int InvalidRow = 0;

        /// <summary>
        /// Obsolete bridge for call sites still using the old 64-slot constant.
        /// Prefer <see cref="GasLoadTimeCapacitySession.Plan"/>.AttributeSlotCount for loops.
        /// </summary>
        public const int MAX_ATTRS = 64;

        public int RowId;

        public static AttributeBuffer CreateAttached()
        {
            var store = GasLoadTimeCapacitySession.ActiveStore;
            return new AttributeBuffer { RowId = store.AllocateEntityRow() };
        }

        public static void Release(ref AttributeBuffer buffer)
        {
            if (buffer.RowId == InvalidRow)
            {
                return;
            }

            GasLoadTimeCapacitySession.ActiveStore.ReleaseEntityRow(buffer.RowId);
            buffer.RowId = InvalidRow;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCurrent(int attributeId)
        {
            return Store().GetCurrent(RequireRow(), attributeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetBase(int attributeId)
        {
            return Store().GetBase(RequireRow(), attributeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAttribute(int attributeId)
        {
            return Store().HasAttribute(RequireRow(), attributeId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBase(int attributeId, float value)
        {
            Store().SetBase(RequireRow(), attributeId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCurrent(int attributeId, float value)
        {
            Store().SetCurrent(RequireRow(), attributeId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAggregatedCurrent(int attributeId, float value)
        {
            Store().SetAggregatedCurrent(RequireRow(), attributeId, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float GetRawBase(int attributeId) => Store().GetRawBase(RequireRow(), attributeId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal float GetRawCap(int attributeId) => Store().GetRawCap(RequireRow(), attributeId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetRawCap(int attributeId, float value) => Store().SetRawCap(RequireRow(), attributeId, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetRawCurrentUnconstrained(int attributeId, float value) =>
            Store().SetRawCurrentUnconstrained(RequireRow(), attributeId, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int RequireRow()
        {
            if (RowId == InvalidRow || RowId < 0)
            {
                throw new InvalidOperationException(
                    "AttributeBuffer has no world-store row. Use AttributeBuffer.CreateAttached() or GasAttributeRows.Attach.");
            }

            return RowId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static GasWorldColumnStore Store() => GasLoadTimeCapacitySession.ActiveStore;
    }

    public static class GasAttributeRows
    {
        public static ref AttributeBuffer Attach(World world, Entity entity)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException("Cannot attach attribute row to a dead entity.");
            }

            var buffer = AttributeBuffer.CreateAttached();
            if (world.Has<AttributeBuffer>(entity))
            {
                ref var existing = ref world.Get<AttributeBuffer>(entity);
                AttributeBuffer.Release(ref existing);
                existing = buffer;
                return ref existing;
            }

            world.Add(entity, buffer);
            return ref world.Get<AttributeBuffer>(entity);
        }

        public static void ReleaseIfPresent(World world, Entity entity)
        {
            if (world == null || !world.IsAlive(entity) || !world.Has<AttributeBuffer>(entity))
            {
                return;
            }

            ref var buffer = ref world.Get<AttributeBuffer>(entity);
            AttributeBuffer.Release(ref buffer);
        }
    }
}
