using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Map
{
    public class WorldMap
    {
        // Dimensions now configurable
        public int WidthInMacroTiles { get; private set; }
        public int HeightInMacroTiles { get; private set; }
        public const int TileSize = SpatialScaleDefaults.MacroTileCells;
        
        public int TotalWidth => WidthInMacroTiles * TileSize;
        public int TotalHeight => HeightInMacroTiles * TileSize;
        
        public const int MaxHeightLevel = SpatialScaleDefaults.LogicTerrainMaxHeightLevel;
        public const int WorldScale = 1000; // 1 Grid = 1000 IntVector units

        private MapTile[] _tiles;

        public WorldMap() : this(
            SpatialScaleDefaults.DefaultWorldWidthMacroTiles,
            SpatialScaleDefaults.DefaultWorldHeightMacroTiles) { }

        public WorldMap(int widthInMacroTiles, int heightInMacroTiles)
        {
            Initialize(widthInMacroTiles, heightInMacroTiles);
        }

        public void Initialize(int widthInMacroTiles, int heightInMacroTiles)
        {
            if (widthInMacroTiles <= 0) throw new ArgumentOutOfRangeException(nameof(widthInMacroTiles));
            if (heightInMacroTiles <= 0) throw new ArgumentOutOfRangeException(nameof(heightInMacroTiles));
            WidthInMacroTiles = widthInMacroTiles;
            HeightInMacroTiles = heightInMacroTiles;
            _tiles = new MapTile[WidthInMacroTiles * HeightInMacroTiles];
        }

        public MapTile GetOrCreateTile(int tileX, int tileY)
        {
            if (tileX < 0 || tileX >= WidthInMacroTiles || tileY < 0 || tileY >= HeightInMacroTiles)
                return null;

            int index = tileY * WidthInMacroTiles + tileX;
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
