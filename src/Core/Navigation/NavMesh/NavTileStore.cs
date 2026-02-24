using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.Core.Navigation.NavMesh
{
    public sealed class NavTileStore
    {
        private readonly Func<NavTileId, Stream> _openStream;
        private readonly Dictionary<NavTileId, NavTile> _loaded = new Dictionary<NavTileId, NavTile>(256);

        public NavTileStore(Func<NavTileId, Stream> openStream)
        {
            _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        }

        public bool TryGet(NavTileId id, out NavTile tile) => _loaded.TryGetValue(id, out tile);

        public NavTile GetOrLoad(NavTileId id)
        {
            if (_loaded.TryGetValue(id, out var tile)) return tile;
            using var s = _openStream(id);
            tile = NavTileBinary.Read(s);
            _loaded[id] = tile;
            return tile;
        }

        public NavTile Reload(NavTileId id)
        {
            using var s = _openStream(id);
            var tile = NavTileBinary.Read(s);
            _loaded[id] = tile;
            return tile;
        }

        public void Unload(NavTileId id) => _loaded.Remove(id);

        public void Clear() => _loaded.Clear();
    }
}
