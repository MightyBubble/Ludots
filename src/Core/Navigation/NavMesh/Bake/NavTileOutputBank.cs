using System;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Fixed-capacity NavTile output bank for runtime-incremental LayeredSpan bake/publish.
    /// Construction allocates every slot; warmed Fill/Copy paths allocate zero managed bytes.
    /// </summary>
    public sealed class NavTileOutputBank
    {
        private readonly NavTile[] _slots;
        private readonly byte[] _checksumScratch;
        private int _cursor;

        public NavTileOutputBank(NavRuntimeIncrementalConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.StagedEntryCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(config),
                    "NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity must be > 0.");
            }

            if (config.OutputVertexCapacity <= 0 ||
                config.OutputTriangleCapacity <= 0 ||
                config.OutputPortalCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(config),
                    "NavMeshBakeConfig.runtimeIncremental output bank capacities must be > 0.");
            }

            Capacity = config.StagedEntryCapacity;
            OutputVertexCapacity = config.OutputVertexCapacity;
            OutputTriangleCapacity = config.OutputTriangleCapacity;
            OutputPortalCapacity = config.OutputPortalCapacity;
            _slots = new NavTile[Capacity];
            int maxSerialized = NavTileBinary.GetSerializedSize(
                CreateProbeTile(OutputVertexCapacity, OutputTriangleCapacity, OutputPortalCapacity));
            _checksumScratch = new byte[maxSerialized];
            for (int i = 0; i < Capacity; i++)
            {
                _slots[i] = NavTile.CreateBanked(
                    OutputVertexCapacity,
                    OutputTriangleCapacity,
                    OutputPortalCapacity);
            }
        }

        public int Capacity { get; }

        public int OutputVertexCapacity { get; }

        public int OutputTriangleCapacity { get; }

        public int OutputPortalCapacity { get; }

        public int Count => _cursor;

        /// <summary>
        /// Fixed preallocated geometry-channel bytes for every staged bank slot plus checksum scratch.
        /// </summary>
        public long PreallocatedChannelPayloadBytes
            => checked(
                ((long)Capacity * NavTile.ComputeBankedChannelPayloadBytes(
                    OutputVertexCapacity,
                    OutputTriangleCapacity,
                    OutputPortalCapacity)) +
                _checksumScratch.LongLength);

        public Span<byte> ChecksumScratch => _checksumScratch;

        public NavTile this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_cursor)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _slots[index];
            }
        }

        public void Reset() => _cursor = 0;

        public NavTile RentSlot(string capacityOwner = "NavMeshBakeConfig.runtimeIncremental.stagedEntryCapacity")
        {
            if (_cursor >= Capacity)
            {
                throw new InvalidOperationException(
                    $"{capacityOwner} exhausted ({Capacity}); required {_cursor + 1}.");
            }

            NavTile slot = _slots[_cursor++];
            slot.ClearTopology();
            return slot;
        }

        private static NavTile CreateProbeTile(int vertexCapacity, int triangleCapacity, int portalCapacity)
        {
            NavTile probe = NavTile.CreateBanked(vertexCapacity, triangleCapacity, portalCapacity);
            probe.SetCounts(vertexCapacity, triangleCapacity, portalCapacity);
            return probe;
        }
    }
}
