using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public readonly struct OrderSpatialPayloadHandle
    {
        public OrderSpatialPayloadHandle(byte slot, uint generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public byte Slot { get; }
        public uint Generation { get; }
        public bool IsValid => Generation != 0;
    }

    public unsafe struct OrderSpatialPayloadBuffer
    {
        public const int SlotCapacity = OrderBuffer.MAX_QUEUED_ORDERS + 3;
        private const int PointCapacity = OrderSpatial.MaxPoints;

        private ushort _occupiedMask;
        private fixed uint _generations[SlotCapacity];
        private fixed byte _pointCounts[SlotCapacity];
        private fixed int _pointX[SlotCapacity * PointCapacity];
        private fixed int _pointY[SlotCapacity * PointCapacity];
        private fixed int _pointZ[SlotCapacity * PointCapacity];

        public bool TryAllocate(
            ReadOnlySpan<int> pointX,
            ReadOnlySpan<int> pointY,
            int pointCount,
            out OrderSpatialPayloadHandle handle)
        {
            handle = default;
            if (pointCount <= OrderSpatial.MaxInlinePoints ||
                pointCount > PointCapacity ||
                pointX.Length < pointCount ||
                pointY.Length < pointCount)
            {
                return false;
            }

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                return false;
            }

            fixed (uint* generations = _generations)
            fixed (byte* pointCounts = _pointCounts)
            fixed (int* x = _pointX)
            fixed (int* y = _pointY)
            fixed (int* z = _pointZ)
            {
                uint generation = generations[slot] + 1;
                if (generation == 0)
                {
                    generation = 1;
                }

                generations[slot] = generation;
                pointCounts[slot] = checked((byte)pointCount);
                int offset = slot * PointCapacity;
                for (int i = 0; i < pointCount; i++)
                {
                    x[offset + i] = pointX[i];
                    y[offset + i] = 0;
                    z[offset + i] = pointY[i];
                }

                _occupiedMask |= (ushort)(1 << slot);
                handle = new OrderSpatialPayloadHandle((byte)slot, generation);
                return true;
            }
        }

        public readonly int GetPointCount(in OrderSpatialPayloadHandle handle)
        {
            RequireLiveHandle(in handle);
            fixed (byte* pointCounts = _pointCounts)
            {
                return pointCounts[handle.Slot];
            }
        }

        public readonly bool TryGetPoint(
            in OrderSpatialPayloadHandle handle,
            int pointIndex,
            out Vector3 pointWorldCm)
        {
            int pointCount = GetPointCount(in handle);
            if ((uint)pointIndex >= (uint)pointCount)
            {
                pointWorldCm = default;
                return false;
            }

            int offset = handle.Slot * PointCapacity + pointIndex;
            fixed (int* x = _pointX)
            fixed (int* y = _pointY)
            fixed (int* z = _pointZ)
            {
                pointWorldCm = new Vector3(x[offset], y[offset], z[offset]);
                return true;
            }
        }

        public void Release(in OrderSpatialPayloadHandle handle)
        {
            RequireLiveHandle(in handle);
            _occupiedMask &= (ushort)~(1 << handle.Slot);
            fixed (byte* pointCounts = _pointCounts)
            {
                pointCounts[handle.Slot] = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void RequireLiveHandle(in OrderSpatialPayloadHandle handle)
        {
            if (handle.Slot >= SlotCapacity ||
                (_occupiedMask & (1 << handle.Slot)) == 0)
            {
                throw new InvalidOperationException(
                    $"ORDER.SPATIAL.ERR.StalePayloadHandle: slot={handle.Slot}, generation={handle.Generation}.");
            }

            fixed (uint* generations = _generations)
            {
                if (handle.Generation == 0 || generations[handle.Slot] != handle.Generation)
                {
                    throw new InvalidOperationException(
                        $"ORDER.SPATIAL.ERR.StalePayloadHandle: slot={handle.Slot}, generation={handle.Generation}.");
                }
            }
        }

        private readonly int FindFreeSlot()
        {
            for (int slot = 0; slot < SlotCapacity; slot++)
            {
                if ((_occupiedMask & (1 << slot)) == 0)
                {
                    return slot;
                }
            }

            return -1;
        }
    }
}
