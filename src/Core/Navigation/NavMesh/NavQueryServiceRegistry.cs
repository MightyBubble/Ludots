using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Per-board nav tile geometry: tile extents and the board-local origin tiles are
    /// addressed against. Authored on the board's NavTileGridConfig; runtime addressing
    /// consumes this declaration only.
    /// </summary>
    public readonly struct NavBoardTileGeometry : IEquatable<NavBoardTileGeometry>
    {
        public readonly int TileWidthCm;
        public readonly int TileHeightCm;
        public readonly int OriginXcm;
        public readonly int OriginZcm;
        public readonly int WidthChunks;
        public readonly int HeightChunks;

        public NavBoardTileGeometry(int tileWidthCm, int tileHeightCm, int originXcm, int originZcm)
            : this(tileWidthCm, tileHeightCm, originXcm, originZcm, 0, 0)
        {
        }

        public NavBoardTileGeometry(
            int tileWidthCm,
            int tileHeightCm,
            int originXcm,
            int originZcm,
            int widthChunks,
            int heightChunks)
        {
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm));
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm));
            if (widthChunks < 0) throw new ArgumentOutOfRangeException(nameof(widthChunks));
            if (heightChunks < 0) throw new ArgumentOutOfRangeException(nameof(heightChunks));
            TileWidthCm = tileWidthCm;
            TileHeightCm = tileHeightCm;
            OriginXcm = originXcm;
            OriginZcm = originZcm;
            WidthChunks = widthChunks;
            HeightChunks = heightChunks;
        }

        public bool HasDeclaredExtent => WidthChunks > 0 && HeightChunks > 0;

        public bool ContainsTile(int chunkX, int chunkY) =>
            !HasDeclaredExtent ||
            (chunkX >= 0 && chunkX < WidthChunks && chunkY >= 0 && chunkY < HeightChunks);

        public bool Equals(NavBoardTileGeometry other) =>
            TileWidthCm == other.TileWidthCm &&
            TileHeightCm == other.TileHeightCm &&
            OriginXcm == other.OriginXcm &&
            OriginZcm == other.OriginZcm &&
            WidthChunks == other.WidthChunks &&
            HeightChunks == other.HeightChunks;

        public override bool Equals(object obj) => obj is NavBoardTileGeometry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TileWidthCm, TileHeightCm, OriginXcm, OriginZcm, WidthChunks, HeightChunks);
    }

    public readonly struct NavQueryServiceKey : IEquatable<NavQueryServiceKey>
    {
        public readonly string BoardId;
        public readonly int Layer;
        public readonly int Profile;

        public NavQueryServiceKey(int layer, int profile)
            : this(string.Empty, layer, profile)
        {
        }

        public NavQueryServiceKey(string boardId, int layer, int profile)
        {
            BoardId = boardId ?? string.Empty;
            Layer = layer;
            Profile = profile;
        }

        public bool Equals(NavQueryServiceKey other) =>
            string.Equals(BoardId, other.BoardId, StringComparison.Ordinal) &&
            Layer == other.Layer &&
            Profile == other.Profile;

        public override bool Equals(object obj) => obj is NavQueryServiceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(BoardId, Layer, Profile);
    }

    public sealed class NavQueryServiceRegistry
    {
        private readonly Dictionary<NavQueryServiceKey, NavTileStore> _stores;
        private readonly Dictionary<string, NavBoardTileGeometry> _boardGeometry;
        private readonly NavBoardTileGeometry _singleBoardGeometry;
        private readonly bool _usesSingleBoardGeometry;

        public NavQueryServiceRegistry(Dictionary<NavQueryServiceKey, NavTileStore> stores, int tileWidthCm, int tileHeightCm)
            : this(stores, new Dictionary<string, NavBoardTileGeometry>(0), fallbackWidthCm: tileWidthCm, fallbackHeightCm: tileHeightCm)
        {
        }

        public NavQueryServiceRegistry(
            Dictionary<NavQueryServiceKey, NavTileStore> stores,
            Dictionary<string, NavBoardTileGeometry> boardGeometry,
            int fallbackWidthCm,
            int fallbackHeightCm)
        {
            _stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _boardGeometry = boardGeometry ?? throw new ArgumentNullException(nameof(boardGeometry));
            if (fallbackWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(fallbackWidthCm));
            if (fallbackHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(fallbackHeightCm));
            _singleBoardGeometry = new NavBoardTileGeometry(fallbackWidthCm, fallbackHeightCm, 0, 0);
            _usesSingleBoardGeometry = _boardGeometry.Count == 0;

            if (_usesSingleBoardGeometry && _stores.Count > 0)
            {
                foreach (NavQueryServiceKey key in _stores.Keys)
                {
                    if (key.BoardId.Length != 0)
                    {
                        throw new InvalidOperationException(
                            "A board-scoped nav query key requires per-board tile geometry; " +
                            "pass the board geometry map instead of a single fallback tile size.");
                    }
                }
            }
        }

        public int TileWidthCm => _singleBoardGeometry.TileWidthCm;

        public int TileHeightCm => _singleBoardGeometry.TileHeightCm;

        public bool TryGetBoardGeometry(string boardId, out NavBoardTileGeometry geometry)
        {
            if (_usesSingleBoardGeometry)
            {
                geometry = _singleBoardGeometry;
                return true;
            }

            return _boardGeometry.TryGetValue(boardId ?? string.Empty, out geometry);
        }

        public NavBoardTileGeometry RequireBoardGeometry(string boardId)
        {
            if (TryGetBoardGeometry(boardId, out NavBoardTileGeometry geometry)) return geometry;
            throw new InvalidOperationException(
                $"Nav query registry has no tile geometry for board '{boardId}'. Author the board's NavTileGrid and bake it before querying.");
        }

        public bool TryGetStore(int layer, int profile, out NavTileStore store)
        {
            if (_stores.TryGetValue(new NavQueryServiceKey(layer, profile), out store)) return true;
            if (_usesSingleBoardGeometry) return false;

            foreach (KeyValuePair<NavQueryServiceKey, NavTileStore> pair in _stores)
            {
                if (pair.Key.Layer == layer && pair.Key.Profile == profile)
                {
                    store = pair.Value;
                    return true;
                }
            }

            store = null;
            return false;
        }

        public bool TryGetStore(string boardId, int layer, int profile, out NavTileStore store)
        {
            if (_stores.TryGetValue(new NavQueryServiceKey(boardId, layer, profile), out store)) return true;
            return _usesSingleBoardGeometry && TryGetStore(layer, profile, out store);
        }

        public bool TryCreateQuery(int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            return TryCreateQuery(string.Empty, layer, profile, areaCosts, out service);
        }

        public bool TryCreateQuery(string boardId, int layer, int profile, NavAreaCostTable areaCosts, out NavQueryService service)
        {
            if (TryGetStore(boardId, layer, profile, out var store) &&
                TryGetBoardGeometry(boardId, out NavBoardTileGeometry geometry))
            {
                service = new NavQueryService(
                    store,
                    layer,
                    areaCosts,
                    geometry);
                return true;
            }

            service = null;
            return false;
        }

        public IEnumerable<string> BoardIds => _usesSingleBoardGeometry
            ? Array.Empty<string>()
            : _boardGeometry.Keys;
    }
}
