using System;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Presentation.Navigation
{
    /// <summary>
    /// Per-frame fixed-capacity SoA view of one selected resident NavMesh store plus tile lifecycle states.
    /// Tile topology remains owned by <see cref="NavTileStore"/>; this buffer only publishes stable references and metadata.
    /// </summary>
    public sealed class NavMeshPresentationBuffer
    {
        private readonly NavTile[] _tiles;
        private readonly uint[] _tileVersions;
        private readonly ulong[] _tileChecksums;
        private readonly NavBakeTileCoord[] _tileStateCoords;
        private readonly NavMeshPresentationTileState[] _tileStates;
        private readonly int[] _tileStateIndexSlots;
        private readonly uint[] _tileStateIndexEpochs;
        private readonly int _tileStateIndexMask;
        private int _tileCount;
        private int _tileStateCount;
        private uint _tileStateEpoch;
        private ulong _frameGeneration;

        public NavMeshPresentationBuffer(int tileCapacity, int tileStateCapacity)
        {
            if (tileCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileCapacity),
                    tileCapacity,
                    "NavMeshBakeConfig.runtimeIncremental.residentTileCapacity must be > 0 for NavMesh presentation.");
            }

            if (tileStateCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileStateCapacity),
                    tileStateCapacity,
                    "presentation.navMeshTileStateCapacity must be > 0 for NavMesh presentation.");
            }

            _tiles = new NavTile[tileCapacity];
            _tileVersions = new uint[tileCapacity];
            _tileChecksums = new ulong[tileCapacity];
            _tileStateCoords = new NavBakeTileCoord[tileStateCapacity];
            _tileStates = new NavMeshPresentationTileState[tileStateCapacity];
            int indexCapacity = NextPowerOfTwo(checked(tileStateCapacity * 2));
            _tileStateIndexSlots = new int[indexCapacity];
            _tileStateIndexEpochs = new uint[indexCapacity];
            _tileStateIndexMask = indexCapacity - 1;
            _tileStateEpoch = 1u;
        }

        public int TileCount => _tileCount;
        public int TileStateCount => _tileStateCount;
        public int TileCapacity => _tiles.Length;
        public int TileStateCapacity => _tileStateCoords.Length;
        public ulong FrameGeneration => _frameGeneration;
        public int Layer { get; private set; }
        public int Profile { get; private set; }
        public uint StoreRevision { get; private set; }
        public ulong StoreGeneration { get; private set; }
        public uint StateRevision { get; private set; }
        public NavQueryTileSpace TileSpace { get; private set; }
        public NavMeshPresentationStyle Style { get; private set; }

        public ReadOnlySpan<NavTile> Tiles => _tiles.AsSpan(0, _tileCount);
        public ReadOnlySpan<uint> TileVersions => _tileVersions.AsSpan(0, _tileCount);
        public ReadOnlySpan<ulong> TileChecksums => _tileChecksums.AsSpan(0, _tileCount);
        public ReadOnlySpan<NavBakeTileCoord> TileStateCoords => _tileStateCoords.AsSpan(0, _tileStateCount);
        public ReadOnlySpan<NavMeshPresentationTileState> TileStates => _tileStates.AsSpan(0, _tileStateCount);

        internal void BeginFrame(
            int layer,
            int profile,
            in NavQueryTileSpace tileSpace,
            uint storeRevision,
            ulong storeGeneration,
            uint stateRevision,
            in NavMeshPresentationStyle style)
        {
            _tileCount = 0;
            _tileStateCount = 0;
            if (_tileStateEpoch == uint.MaxValue)
            {
                Array.Clear(_tileStateIndexEpochs, 0, _tileStateIndexEpochs.Length);
                _tileStateEpoch = 1u;
            }
            else
            {
                _tileStateEpoch++;
            }
            Layer = layer;
            Profile = profile;
            TileSpace = tileSpace;
            StoreRevision = storeRevision;
            StoreGeneration = storeGeneration;
            StateRevision = stateRevision;
            Style = style;
            _frameGeneration = _frameGeneration == ulong.MaxValue ? 1UL : _frameGeneration + 1UL;
        }

        internal void AddTile(NavTile tile)
        {
            if (tile == null)
            {
                throw new ArgumentNullException(nameof(tile));
            }

            if (_tileCount >= _tiles.Length)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.residentTileCapacity ({_tiles.Length}) exhausted for NavMesh presentation; required {_tileCount + 1}.");
            }

            int index = _tileCount++;
            _tiles[index] = tile;
            _tileVersions[index] = tile.TileVersion;
            _tileChecksums[index] = tile.Checksum;
        }

        internal void SetTileState(in NavBakeTileCoord coord, NavMeshPresentationTileState state)
        {
            if (state is not NavMeshPresentationTileState.Pending and
                not NavMeshPresentationTileState.Rebuilding and
                not NavMeshPresentationTileState.Committed)
            {
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown NavMesh presentation tile state.");
            }

            int bucket = HashCoord(in coord) & _tileStateIndexMask;
            for (int probe = 0; probe < _tileStateIndexSlots.Length; probe++)
            {
                if (_tileStateIndexEpochs[bucket] != _tileStateEpoch)
                {
                    if (_tileStateCount >= _tileStateCoords.Length)
                    {
                        throw new InvalidOperationException(
                            $"presentation.navMeshTileStateCapacity ({_tileStateCoords.Length}) exhausted; required {_tileStateCount + 1}.");
                    }

                    int index = _tileStateCount++;
                    _tileStateCoords[index] = coord;
                    _tileStates[index] = state;
                    _tileStateIndexSlots[bucket] = index;
                    _tileStateIndexEpochs[bucket] = _tileStateEpoch;
                    return;
                }

                int existing = _tileStateIndexSlots[bucket];
                if (_tileStateCoords[existing].Equals(coord))
                {
                    _tileStates[existing] = state;
                    return;
                }

                bucket = (bucket + 1) & _tileStateIndexMask;
            }

            throw new InvalidOperationException("NavMesh presentation tile-state index exhausted despite configured load factor <= 0.5.");
        }

        internal void SortTileStates()
        {
            for (int i = 1; i < _tileStateCount; i++)
            {
                NavBakeTileCoord coord = _tileStateCoords[i];
                NavMeshPresentationTileState state = _tileStates[i];
                int j = i - 1;
                while (j >= 0 && CompareCoords(_tileStateCoords[j], coord) > 0)
                {
                    _tileStateCoords[j + 1] = _tileStateCoords[j];
                    _tileStates[j + 1] = _tileStates[j];
                    j--;
                }

                _tileStateCoords[j + 1] = coord;
                _tileStates[j + 1] = state;
            }
        }

        private static int HashCoord(in NavBakeTileCoord coord)
        {
            unchecked
            {
                uint x = (uint)coord.ChunkX;
                uint y = (uint)coord.ChunkY;
                uint hash = (x * 0x9E3779B1u) ^ (y * 0x85EBCA77u);
                return (int)(hash ^ (hash >> 16));
            }
        }

        private static int CompareCoords(NavBakeTileCoord left, NavBakeTileCoord right)
        {
            int y = left.ChunkY.CompareTo(right.ChunkY);
            return y != 0 ? y : left.ChunkX.CompareTo(right.ChunkX);
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result = checked(result << 1);
            }

            return result;
        }
    }
}
