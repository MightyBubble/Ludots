using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// The full identity a nav tile store is bound to. Tiles are spatial payloads keyed by
    /// (chunkX, chunkY, layer); the board and profile are properties of the store that loads
    /// them, not of the tile payload, so the store must carry them to validate that a loaded
    /// or replaced tile belongs to the artifact set this board/profile was baked for.
    /// </summary>
    public readonly struct NavTileStoreScope : IEquatable<NavTileStoreScope>
    {
        public readonly string MapId;
        public readonly string BoardId;
        public readonly int Layer;
        public readonly string ProfileId;

        public NavTileStoreScope(string mapId, string boardId, int layer, string profileId)
        {
            MapId = mapId ?? string.Empty;
            BoardId = boardId ?? string.Empty;
            Layer = layer;
            ProfileId = profileId ?? string.Empty;
        }

        public bool Equals(NavTileStoreScope other) =>
            string.Equals(MapId, other.MapId, StringComparison.Ordinal) &&
            string.Equals(BoardId, other.BoardId, StringComparison.Ordinal) &&
            Layer == other.Layer &&
            string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is NavTileStoreScope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(MapId, BoardId, Layer, ProfileId);
        public override string ToString() => $"{MapId}/{BoardId}/layer{Layer}/profile_{ProfileId}";
    }

    public sealed class NavTileStore
    {
        private readonly Func<NavTileId, Stream> _openStream;
        private readonly object _gate = new object();
        private readonly Dictionary<NavTileId, NavTile> _loaded = new Dictionary<NavTileId, NavTile>(256);
        private readonly Dictionary<NavTileId, NavTileManifestEntry>? _manifestEntries;
        private readonly NavTileStoreScope? _scope;
        private readonly string _manifestBuildHash = string.Empty;
        private uint _revision;

        public NavTileStore(Func<NavTileId, Stream> openStream)
            : this(openStream, manifest: null, scope: null)
        {
        }

        /// <summary>
        /// Creates a store that validates every artifact against its declared manifest entry
        /// on load. A tile whose checksum or tile version disagrees with the manifest is
        /// rejected instead of being served as if it matched the recorded build.
        /// </summary>
        public NavTileStore(Func<NavTileId, Stream> openStream, NavTileManifest? manifest)
            : this(openStream, manifest, scope: null)
        {
        }

        /// <summary>
        /// Creates a store bound to one (map, board, layer, profile) identity. Load and
        /// replace validate against the manifest entry for this exact scope: a tile listed for
        /// another profile or another board is never loadable through this store.
        /// </summary>
        public NavTileStore(Func<NavTileId, Stream> openStream, NavTileManifest? manifest, NavTileStoreScope? scope)
        {
            _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
            _scope = scope;
            if (manifest != null)
            {
                _manifestBuildHash = manifest.BuildHash;
                _manifestEntries = new Dictionary<NavTileId, NavTileManifestEntry>(manifest.Tiles.Length);
                for (int i = 0; i < manifest.Tiles.Length; i++)
                {
                    NavTileManifestEntry entry = manifest.Tiles[i];
                    if (scope != null &&
                        (entry.Layer != scope.Value.Layer ||
                         !string.Equals(entry.ProfileId, scope.Value.ProfileId, StringComparison.Ordinal)))
                    {
                        // The manifest may cover other layers/profiles of the same board; this
                        // store only serves its own (layer, profile) slice.
                        continue;
                    }

                    _manifestEntries[new NavTileId(entry.ChunkX, entry.ChunkY, entry.Layer)] = entry;
                }
            }
        }

        public uint Revision
        {
            get
            {
                lock (_gate)
                {
                    return _revision;
                }
            }
        }

        public bool TryGet(NavTileId id, out NavTile tile)
        {
            lock (_gate)
            {
                return _loaded.TryGetValue(id, out tile);
            }
        }

        public NavTile[] SnapshotLoadedTiles()
        {
            lock (_gate)
            {
                var tiles = new NavTile[_loaded.Count];
                _loaded.Values.CopyTo(tiles, 0);
                return tiles;
            }
        }

        public int CopyLoadedTiles(NavTile[] scratch)
        {
            if (scratch == null) throw new ArgumentNullException(nameof(scratch));

            lock (_gate)
            {
                if (_loaded.Count > scratch.Length)
                {
                    throw new InvalidOperationException(
                        $"CopyLoadedTiles scratch capacity ({scratch.Length}) is below the resident tile count ({_loaded.Count}).");
                }

                int index = 0;
                foreach (KeyValuePair<NavTileId, NavTile> pair in _loaded)
                {
                    scratch[index++] = pair.Value;
                }

                return index;
            }
        }

        public bool TryRunStableRead<T>(Func<T> read, out T result, int maxAttempts = 2)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                uint revisionBefore = Revision;
                T candidate = read();
                uint revisionAfter = Revision;
                if (revisionBefore == revisionAfter)
                {
                    result = candidate;
                    return true;
                }
            }

            result = default;
            return false;
        }

        public NavTile GetOrLoad(NavTileId id)
        {
            AssertScopeLayer(id);
            lock (_gate)
            {
                if (_loaded.TryGetValue(id, out var loaded)) return loaded;
            }

            using var s = _openStream(id);
            NavTile tile = NavTileBinary.Read(s);
            ValidateAgainstManifest(id, tile);
            lock (_gate)
            {
                if (_loaded.TryGetValue(id, out var loaded)) return loaded;
                _loaded[id] = tile;
            }

            return tile;
        }

        public NavTile Reload(NavTileId id)
        {
            AssertScopeLayer(id);
            using var s = _openStream(id);
            var tile = NavTileBinary.Read(s);
            ValidateAgainstManifest(id, tile);
            lock (_gate)
            {
                _loaded[id] = tile;
                AdvanceRevision();
            }

            return tile;
        }

        private void AssertScopeLayer(NavTileId id)
        {
            if (_scope != null && id.Layer != _scope.Value.Layer)
            {
                throw new InvalidDataException(
                    $"Nav tile {id} layer does not match the store scope {_scope.Value} (layer {_scope.Value.Layer}).");
            }
        }

        private void ValidateAgainstManifest(NavTileId id, NavTile tile)
        {
            if (_manifestEntries == null) return;

            if (!_manifestEntries.TryGetValue(id, out NavTileManifestEntry? entry))
            {
                throw new InvalidDataException(
                    $"Nav tile {id} is not listed in the map's manifest (buildHash {_manifestBuildHash}); the artifact set is incomplete. Re-bake the map.");
            }

            string expectedChecksum = "fnv1a64:" + tile.Checksum.ToString("x16");
            if (!string.Equals(entry.TileChecksum, expectedChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Nav tile {id} checksum {expectedChecksum} does not match the manifest entry '{entry.TileChecksum}' (buildHash {_manifestBuildHash}). Re-bake the map.");
            }

            if (entry.TileVersion != tile.TileVersion)
            {
                throw new InvalidDataException(
                    $"Nav tile {id} declares tile version {tile.TileVersion} but the manifest records {entry.TileVersion}. Re-bake the map.");
            }
        }

        public uint Replace(NavTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            lock (_gate)
            {
                _loaded[tile.TileId] = tile;
                return AdvanceRevision();
            }
        }

        public void Unload(NavTileId id)
        {
            lock (_gate)
            {
                if (_loaded.Remove(id))
                {
                    AdvanceRevision();
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                if (_loaded.Count == 0)
                {
                    return;
                }

                _loaded.Clear();
                AdvanceRevision();
            }
        }

        private uint AdvanceRevision()
        {
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
            return _revision;
        }
    }
}
