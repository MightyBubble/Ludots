using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Map;

namespace Ludots.Core.Physics
{
    public class PhysicsWorld
    {
        // Map Grid Size = 1
        // Physics Cell Size = 16 Map Grids
        public const int CellSize = 16;
        public const int TileSizeInCells = WorldMap.TileSize / CellSize; // 256 / 16 = 16
        
        // World Size in Cells
        public int WorldWidthInCells { get; private set; }
        public int WorldHeightInCells { get; private set; }
        
        // Flat array of cells for the whole world
        // This avoids per-tile object overhead and makes cross-tile queries easy.
        private List<Entity>[] _cells; 

        public PhysicsWorld(int widthInChunks = 64, int heightInChunks = 64)
        {
            Initialize(widthInChunks, heightInChunks);
        }

        public void Initialize(int widthInChunks, int heightInChunks)
        {
            WorldWidthInCells = widthInChunks * TileSizeInCells;
            WorldHeightInCells = heightInChunks * TileSizeInCells;
            _cells = new List<Entity>[WorldWidthInCells * WorldHeightInCells];
        }

        private int GetCellIndex(int cellX, int cellY)
        {
            if (cellX < 0) cellX = 0;
            if (cellX >= WorldWidthInCells) cellX = WorldWidthInCells - 1;
            if (cellY < 0) cellY = 0;
            if (cellY >= WorldHeightInCells) cellY = WorldHeightInCells - 1;
            return cellY * WorldWidthInCells + cellX;
        }

        public void Add(Entity entity, IntRect bounds)
        {
            // Convert Map Grid coordinates to Physics Cell coordinates
            int minX = bounds.X / CellSize;
            int minY = bounds.Y / CellSize;
            int maxX = (bounds.X + bounds.Width) / CellSize;
            int maxY = (bounds.Y + bounds.Height) / CellSize;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = GetCellIndex(x, y);
                    if (_cells[idx] == null) _cells[idx] = new List<Entity>(4);
                    _cells[idx].Add(entity);
                }
            }
        }

        public void Remove(Entity entity, IntRect bounds)
        {
            int minX = bounds.X / CellSize;
            int minY = bounds.Y / CellSize;
            int maxX = (bounds.X + bounds.Width) / CellSize;
            int maxY = (bounds.Y + bounds.Height) / CellSize;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = GetCellIndex(x, y);
                    if (_cells[idx] != null)
                    {
                        _cells[idx].Remove(entity);
                    }
                }
            }
        }
        
        public void Query(IntRect bounds, HashSet<Entity> results)
        {
            results.Clear();
            int minX = bounds.X / CellSize;
            int minY = bounds.Y / CellSize;
            int maxX = (bounds.X + bounds.Width) / CellSize;
            int maxY = (bounds.Y + bounds.Height) / CellSize;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = GetCellIndex(x, y);
                    var cell = _cells[idx];
                    if (cell != null)
                    {
                        foreach (var entity in cell)
                        {
                            results.Add(entity);
                        }
                    }
                }
            }
        }

        public int Query(IntRect bounds, Span<Entity> buffer, out int dropped)
        {
            int count = 0;
            dropped = 0;
            int minX = bounds.X / CellSize;
            int minY = bounds.Y / CellSize;
            int maxX = (bounds.X + bounds.Width) / CellSize;
            int maxY = (bounds.Y + bounds.Height) / CellSize;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = GetCellIndex(x, y);
                    var cell = _cells[idx];
                    if (cell == null) continue;

                    for (int j = 0; j < cell.Count; j++)
                    {
                        if (count < buffer.Length)
                        {
                            buffer[count++] = cell[j];
                        }
                        else
                        {
                            dropped++;
                        }
                    }
                }
            }

            return count;
        }
    }
}
