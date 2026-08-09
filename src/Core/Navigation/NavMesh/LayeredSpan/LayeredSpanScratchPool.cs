using System;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// One fixed scratch slot owning every preallocated SoA stage buffer for a layered-span bake.
    /// </summary>
    public sealed class LayeredSpanScratchSlot
    {
        internal LayeredSpanScratchSlot(int slotIndex, NavLayeredSpanConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            SlotIndex = slotIndex;
            Raw = new LayeredSpanScratch(config.ColumnCapacity, config.SpanCapacity);
            Walkability = new LayeredSpanWalkabilityScratch(
                config.ColumnCapacity,
                config.ClassifiedSpanCapacity,
                config.WalkableSpanCapacity);
            Sheets = new LayeredSpanSurfaceSheetScratch(config.ColumnCapacity, config.SpanCapacity);
            Links = new LayeredSpanWalkLinkScratch(config.WalkableSpanCapacity, config.LinkCapacity);
            Radius = new LayeredSpanRadiusFieldScratch(
                config.SpanCapacity,
                config.SheetCapacity,
                config.PortalIntervalCapacity);
            Regions = new LayeredSpanRegionScratch(config.SpanCapacity, config.RegionCapacity);
            Contours = new LayeredSpanContourScratch(
                config.ColumnCapacity,
                config.SpanCapacity,
                config.SheetCapacity,
                config.ChartCapacity,
                config.RingCapacity,
                config.ContourVertexCapacity,
                config.ContourEdgeCapacity,
                config.SeamCapacity,
                config.PortalIntervalCapacity,
                config.CanonicalLinkCapacity,
                config.SplitPointCapacity);
            Triangulation = new LayeredSpanTriangulationScratch(
                config.TriangulationVertexCapacity,
                config.TriangulationTriangleCapacity,
                config.ConstrainedEdgeCapacity,
                config.BorderPortalCapacity,
                config.PolygonVertexCapacity,
                config.AdjacencyEdgeCapacity,
                config.BridgeCandidateCapacity,
                config.RingWorkCapacity,
                config.TemporaryConstraintFlagCapacity);

            PreallocatedChannelPayloadBytes = LayeredSpanScratchChannelPayload.Sum(
                Raw.PreallocatedChannelPayloadBytes,
                Walkability.PreallocatedChannelPayloadBytes,
                Sheets.PreallocatedChannelPayloadBytes,
                Links.PreallocatedChannelPayloadBytes,
                Radius.PreallocatedChannelPayloadBytes,
                Regions.PreallocatedChannelPayloadBytes,
                Contours.PreallocatedChannelPayloadBytes,
                Triangulation.PreallocatedChannelPayloadBytes);
        }

        public int SlotIndex { get; }

        public long PreallocatedChannelPayloadBytes { get; }

        public LayeredSpanScratch Raw { get; }

        public LayeredSpanWalkabilityScratch Walkability { get; }

        public LayeredSpanSurfaceSheetScratch Sheets { get; }

        public LayeredSpanWalkLinkScratch Links { get; }

        public LayeredSpanRadiusFieldScratch Radius { get; }

        public LayeredSpanRegionScratch Regions { get; }

        public LayeredSpanContourScratch Contours { get; }

        public LayeredSpanTriangulationScratch Triangulation { get; }
    }

    /// <summary>
    /// Fixed-slot scratch pool. Construction allocates every slot; warmed acquire/release allocate zero
    /// managed bytes, never block on exhaustion, and never fall back or silently skip.
    /// </summary>
    public sealed class LayeredSpanScratchPool
    {
        private readonly LayeredSpanScratchSlot[] _slots;
        private readonly int[] _freeIndices;
        private int _freeCount;
        private readonly object _gate = new();

        public LayeredSpanScratchPool(NavLayeredSpanConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            ScratchSlotCount = config.ScratchSlotCount;
            Config = config;
            _slots = new LayeredSpanScratchSlot[ScratchSlotCount];
            _freeIndices = new int[ScratchSlotCount];
            for (int i = 0; i < ScratchSlotCount; i++)
            {
                _slots[i] = new LayeredSpanScratchSlot(i, config);
                // Deterministic free-stack: highest index on top so first acquire yields slot 0.
                _freeIndices[i] = ScratchSlotCount - 1 - i;
            }

            _freeCount = ScratchSlotCount;

            long slotPayloadSum = 0;
            for (int i = 0; i < ScratchSlotCount; i++)
            {
                slotPayloadSum = checked(slotPayloadSum + _slots[i].PreallocatedChannelPayloadBytes);
            }

            PreallocatedChannelPayloadBytes = checked(
                slotPayloadSum + LayeredSpanScratchChannelPayload.Of(_freeIndices));
        }

        public int ScratchSlotCount { get; }

        public NavLayeredSpanConfig Config { get; }

        /// <summary>
        /// Exact sum of every owned scratch-slot channel payload plus the free-index int[] payload.
        /// SSOT for preallocated layered-span scratch working-set bytes; not a GC measurement.
        /// </summary>
        public long PreallocatedChannelPayloadBytes { get; }

        /// <summary>
        /// Exact preallocated channel payload bytes for <paramref name="config"/> by constructing a temporary pool (allocates).
        /// Prefer <see cref="PreallocatedChannelPayloadBytes"/> on an owned pool instance.
        /// </summary>
        public static long EstimateFixedManagedBytes(NavLayeredSpanConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return new LayeredSpanScratchPool(config).PreallocatedChannelPayloadBytes;
        }

        public int AvailableCount
        {
            get
            {
                lock (_gate)
                {
                    return _freeCount;
                }
            }
        }

        public LayeredSpanScratchSlot Acquire()
        {
            lock (_gate)
            {
                if (_freeCount <= 0)
                {
                    throw new InvalidOperationException(
                        $"LayeredSpanScratchPool exhausted: NavMeshBakeConfig.layeredSpan.scratchSlotCount ({ScratchSlotCount}).");
                }

                int index = _freeIndices[--_freeCount];
                return _slots[index];
            }
        }

        public void Release(LayeredSpanScratchSlot slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));

            lock (_gate)
            {
                if ((uint)slot.SlotIndex >= (uint)_slots.Length || !ReferenceEquals(_slots[slot.SlotIndex], slot))
                {
                    throw new InvalidOperationException(
                        "LayeredSpanScratchPool.Release requires a slot owned by this pool.");
                }

                for (int i = 0; i < _freeCount; i++)
                {
                    if (_freeIndices[i] == slot.SlotIndex)
                    {
                        throw new InvalidOperationException(
                            $"LayeredSpanScratchPool.Release detected double-release of slot {slot.SlotIndex}.");
                    }
                }

                if (_freeCount >= ScratchSlotCount)
                {
                    throw new InvalidOperationException(
                        "LayeredSpanScratchPool.Release would exceed scratchSlotCount free entries.");
                }

                _freeIndices[_freeCount++] = slot.SlotIndex;
            }
        }
    }
}
