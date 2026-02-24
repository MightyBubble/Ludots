using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Map
{
    public class WorldMap
    {
        // Dimensions now configurable
        public int WidthInTiles { get; private set; }
        public int HeightInTiles { get; private set; }
        public const int TileSize = 256;
        
        public int TotalWidth => WidthInTiles * TileSize;
        public int TotalHeight => HeightInTiles * TileSize;
        
        public const int MaxHeightLevel = 15;
        public const int WorldScale = 1000; // 1 Grid = 1000 IntVector units

        private MapTile[] _tiles;

        public WorldMap() : this(64, 64) { }

        public WorldMap(int widthInChunks, int heightInChunks)
        {
            Initialize(widthInChunks, heightInChunks);
        }

        public void Initialize(int widthInChunks, int heightInChunks)
        {
            WidthInTiles = widthInChunks;
            HeightInTiles = heightInChunks;
            _tiles = new MapTile[WidthInTiles * HeightInTiles];
        }

        public MapTile GetOrCreateTile(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= WidthInTiles || tileY < 0 || tileY >= HeightInTiles)
                return null;

            int index = tileY * WidthInTiles + tileX;
            if (_tiles[index] == null)
            {
                _tiles[index] = new MapTile();
            }
            return _tiles[index];
        }

        public byte GetHeight(int gridX, int gridY)
        {
            int tileX = gridX / TileSize;
            int tileY = gridY / TileSize;
            int localX = gridX % TileSize;
            int localY = gridY % TileSize;

            var tile = GetOrCreateTile(tileX, tileY);
            return tile?.GetHeight(localX, localY) ?? 0;
        }
        
        public void SetHeight(int gridX, int gridY, int height)
        {
            if (height < 0) height = 0;
            if (height > MaxHeightLevel) height = MaxHeightLevel;
            
            int tileX = gridX / TileSize;
            int tileY = gridY / TileSize;
            int localX = gridX % TileSize;
            int localY = gridY % TileSize;

            var tile = GetOrCreateTile(tileX, tileY);
            tile?.SetHeight(localX, localY, (byte)height);
        }

        public bool IsBlocked(int gridX, int gridY)
        {
            int tileX = gridX / TileSize;
            int tileY = gridY / TileSize;
            int localX = gridX % TileSize;
            int localY = gridY % TileSize;

            var tile = GetOrCreateTile(tileX, tileY);
            return tile == null || tile.IsBlocked(localX, localY);
        }

        /// <summary>
        /// Converts Scaled World Position (IntVector2) to Grid Coordinates.
        /// </summary>
        public static IntVector2 WorldToGrid(IntVector2 worldPos)
        {
            return new IntVector2(worldPos.X / WorldScale, worldPos.Y / WorldScale);
        }
        
        /// <summary>
        /// Converts Grid Coordinates to World Position Center.
        /// </summary>
        public static IntVector2 GridToWorld(int gridX, int gridY)
        {
             return new IntVector2(gridX * WorldScale + WorldScale / 2, gridY * WorldScale + WorldScale / 2);
        }
    }
}
