using System;
using Ludots.Core.Map;

namespace Ludots.Core.Map.Generator
{
    public class MapGenerator
    {
        public void GenerateSingleTile(WorldMap map, int tileX, int tileY)
        {
            var tile = map.GetOrCreateTile(tileX, tileY);
            if (tile == null) return;

            // Generate 256x256 heightmap using noise
            float scale = 0.02f; // Noise frequency
            
            for (int y = 0; y < MapTile.Size; y++)
            {
                for (int x = 0; x < MapTile.Size; x++)
                {
                    // Global coordinates (though we only care about local for noise sampling)
                    float sampleX = (tileX * MapTile.Size + x) * scale;
                    float sampleY = (tileY * MapTile.Size + y) * scale;

                    double noiseValue = SimpleNoise.Noise(sampleX, sampleY);
                    
                    // Map -1..1 to 0..15
                    int height = (int)((noiseValue + 1.0) * 0.5 * 15);
                    height = Math.Clamp(height, 0, 15);

                    tile.SetHeight(x, y, (byte)height);
                    
                    // Simple blocking rule: water (height < 3) is blocked
                    bool blocked = height < 3;
                    tile.SetBlocked(x, y, blocked);
                }
            }
        }
    }
}
