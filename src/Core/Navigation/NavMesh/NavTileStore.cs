using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavTileStore
    {
        private readonly object _sync = new object();
        private readonly Func<NavTileId, Stream> _openStream;
        private readonly Dictionary<NavTileId, NavTile> _loaded = new Dictionary<NavTileId, NavTile>(256);

        public NavTileStore(
            Func<NavTileId, Stream> openStream,
            int tileWidthCm = 0,
            int tileHeightCm = 0,
            int worldMinXcm = 0,
            int worldMinZcm = 0,
            int bakedTileWidthCm = 0,
            int bakedTileHeightCm = 0)
        {
            _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
            TileWidthCm = tileWidthCm;
            TileHeightCm = tileHeightCm;
            WorldMinXcm = worldMinXcm;
            WorldMinZcm = worldMinZcm;
            BakedTileWidthCm = bakedTileWidthCm;
            BakedTileHeightCm = bakedTileHeightCm;
        }

        public int TileWidthCm { get; }

        public int TileHeightCm { get; }

        public int WorldMinXcm { get; }

        public int WorldMinZcm { get; }

        public int BakedTileWidthCm { get; }

        public int BakedTileHeightCm { get; }

        public int Revision
        {
            get
            {
                lock (_sync)
                {
                    return _revision;
                }
            }
        }

        private int _revision;

        public bool TryGet(NavTileId id, out NavTile tile)
        {
            lock (_sync)
            {
                return _loaded.TryGetValue(id, out tile);
            }
        }

        public NavTile GetOrLoad(NavTileId id)
        {
            lock (_sync)
            {
                if (_loaded.TryGetValue(id, out var tile)) return tile;
                using var s = _openStream(id);
                tile = NavTileBinary.Read(s);
                _loaded[id] = tile;
                return tile;
            }
        }

        public NavTile Reload(NavTileId id)
        {
            lock (_sync)
            {
                using var s = _openStream(id);
                var tile = NavTileBinary.Read(s);
                _loaded[id] = tile;
                _revision++;
                return tile;
            }
        }

        public NavTile Replace(NavTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            lock (_sync)
            {
                _loaded[tile.TileId] = tile;
                _revision++;
                return tile;
            }
        }

        public void Unload(NavTileId id)
        {
            lock (_sync)
            {
                if (_loaded.Remove(id))
                {
                    _revision++;
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                if (_loaded.Count > 0)
                {
                    _loaded.Clear();
                    _revision++;
                }
            }
        }
    }
}
