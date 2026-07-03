using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavTileStore
    {
        private readonly Func<NavTileId, Stream> _openStream;
        private readonly object _gate = new object();
        private readonly Dictionary<NavTileId, NavTile> _loaded = new Dictionary<NavTileId, NavTile>(256);
        private uint _revision;

        public NavTileStore(Func<NavTileId, Stream> openStream)
        {
            _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
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
            lock (_gate)
            {
                if (_loaded.TryGetValue(id, out var loaded)) return loaded;
            }

            using var s = _openStream(id);
            NavTile tile = NavTileBinary.Read(s);
            lock (_gate)
            {
                if (_loaded.TryGetValue(id, out var loaded)) return loaded;
                _loaded[id] = tile;
            }

            return tile;
        }

        public NavTile Reload(NavTileId id)
        {
            using var s = _openStream(id);
            var tile = NavTileBinary.Read(s);
            lock (_gate)
            {
                _loaded[id] = tile;
                AdvanceRevision();
            }

            return tile;
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
