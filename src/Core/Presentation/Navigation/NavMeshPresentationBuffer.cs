using System;
using System.Globalization;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Presentation.Navigation
{
    /// <summary>
    /// Per-frame fixed-capacity view of one selected resident NavMesh store.
    /// Tile topology remains owned by <see cref="NavTileStore"/>; this buffer only publishes stable references,
    /// tile version/checksum columns, and source metadata (layer, profile, mode, algorithm, store revision, voxel params).
    /// </summary>
    public sealed class NavMeshPresentationBuffer
    {
        private readonly NavTile[] _tiles;
        private readonly uint[] _tileVersions;
        private readonly ulong[] _tileChecksums;
        private int _tileCount;
        private ulong _frameGeneration;

        public NavMeshPresentationBuffer(int tileCapacity)
        {
            if (tileCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileCapacity),
                    tileCapacity,
                    "presentation.navMeshTileCapacity must be > 0 for NavMesh presentation.");
            }

            _tiles = new NavTile[tileCapacity];
            _tileVersions = new uint[tileCapacity];
            _tileChecksums = new ulong[tileCapacity];
        }

        public int TileCount => _tileCount;
        public int TileCapacity => _tiles.Length;
        public ulong FrameGeneration => _frameGeneration;
        public int Layer { get; private set; }
        public int Profile { get; private set; }
        public string ProfileId { get; private set; } = string.Empty;
        public NavBakeMode Mode { get; private set; }
        public NavBakeAlgorithmKind Algorithm { get; private set; }
        public uint StoreRevision { get; private set; }
        public uint StateRevision { get; private set; }
        public NavBuildConfig BuildConfig { get; private set; }
        public NavMeshPresentationStyle Style { get; private set; }

        public ReadOnlySpan<NavTile> Tiles => _tiles.AsSpan(0, _tileCount);
        public ReadOnlySpan<uint> TileVersions => _tileVersions.AsSpan(0, _tileCount);
        public ReadOnlySpan<ulong> TileChecksums => _tileChecksums.AsSpan(0, _tileCount);

        internal void BeginFrame(
            int layer,
            int profile,
            string profileId,
            NavBakeMode mode,
            NavBakeAlgorithmKind algorithm,
            uint storeRevision,
            uint stateRevision,
            in NavBuildConfig buildConfig,
            in NavMeshPresentationStyle style)
        {
            _tileCount = 0;
            Layer = layer;
            Profile = profile;
            ProfileId = profileId;
            Mode = mode;
            Algorithm = algorithm;
            StoreRevision = storeRevision;
            StateRevision = stateRevision;
            BuildConfig = buildConfig;
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
                    $"presentation.navMeshTileCapacity ({_tiles.Length}) exhausted for NavMesh presentation; required {_tileCount + 1}.");
            }

            int index = _tileCount++;
            _tiles[index] = tile;
            _tileVersions[index] = tile.TileVersion;
            _tileChecksums[index] = tile.Checksum;
        }

        /// <summary>
        /// Deterministic single-line source metadata for diagnostics: layer, profile index/id, bake mode,
        /// algorithm, store revision, voxel build parameters, and first-tile version/checksum when present.
        /// </summary>
        public string FormatMetadataLine()
        {
            if (Mode is not (NavBakeMode.Offline or NavBakeMode.RuntimeIncremental))
            {
                throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Unknown NavBakeMode in NavMesh presentation metadata.");
            }

            if (Algorithm is not (NavBakeAlgorithmKind.Recast or NavBakeAlgorithmKind.Cdt))
            {
                throw new ArgumentOutOfRangeException(nameof(Algorithm), Algorithm, "Unknown NavBakeAlgorithmKind in NavMesh presentation metadata.");
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(192);
            builder.Append("navmesh layer=").Append(Layer)
                .Append(" profile=").Append(Profile)
                .Append(" id=").Append(ProfileId)
                .Append(" mode=").Append(NavBakeNames.FormatMode(Mode))
                .Append(" algorithm=").Append(NavBakeNames.FormatAlgorithm(Algorithm))
                .Append(" storeRev=").Append(StoreRevision)
                .Append(" stateRev=").Append(StateRevision)
                .Append(" build={hScale=").Append(BuildConfig.HeightScaleMeters.ToString("R", CultureInfo.InvariantCulture))
                .Append(",upDot=").Append(BuildConfig.MinWalkableUpDot.ToString("R", CultureInfo.InvariantCulture))
                .Append(",cliff=").Append(BuildConfig.CliffHeightThreshold)
                .Append("} tiles=").Append(_tileCount);
            if (_tileCount > 0)
            {
                NavTile first = _tiles[0];
                builder.Append(" firstTile=").Append(first.TileId)
                    .Append("v").Append(first.TileVersion)
                    .Append("/").Append(first.Checksum.ToString("X16", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
