using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Map
{
    public class WorldMap
    {
        // Dimensions now configurable
        public int WidthInPages { get; private set; }
        public int HeightInPages { get; private set; }
        public const int TileSize = SpatialScaleDefaults.TerrainPageCells;
        
        public int TotalWidth => WidthInPages * TileSize;
        public int TotalHeight => HeightInPages * TileSize;
        
        public const int MaxHeightLevel = SpatialScaleDefaults.LogicTerrainMaxHeightLevel;
        public const int WorldScale = 1000; // 1 Grid = 1000 IntVector units

        private MapTile[] _tiles;

        public WorldMap() : this(
            SpatialScaleDefaults.DefaultBoardWidthPages,
            SpatialScaleDefaults.DefaultBoardHeightPages) { }

        public WorldMap(int widthInPages, int heightInPages)
        {
            Initialize(widthInPages, heightInPages);
        }

        public void Initialize(int widthInPages, int heightInPages)
        {
            if (widthInPages <= 0) throw new ArgumentOutOfRangeException(nameof(widthInPages));
            if (heightInPages <= 0) throw new ArgumentOutOfRangeException(nameof(heightInPages));
            WidthInPages = widthInPages;
            HeightInPages = heightInPages;
            _tiles = new MapTile[WidthInPages * HeightInPages];
        }

        public MapTile GetOrCreateTile(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= WidthInPages || tileY < 0 || tileY >= HeightInPages)
                return null;

            int index = tileY * WidthInPages + tileX;
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
