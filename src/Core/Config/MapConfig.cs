using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Config
{
    public class MapConfig
    {
        public string Id { get; set; }
        public string ParentId { get; set; } // Support for Map Inheritance
        public string DataFile { get; set; } // Path to .bin file relative to assets/Data/Maps
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<EntitySpawnData> Entities { get; set; } = new List<EntitySpawnData>();

        /// <summary>
        /// Per-map spatial configuration. When null, the engine falls back to global defaults from game.json.
        /// </summary>
        public MapSpatialConfig Spatial { get; set; }
    }

    /// <summary>
    /// Spatial configuration for a map instance: grid type, dimensions, cell sizes, and chunk size.
    /// All fields have sensible defaults matching the engine's global defaults.
    /// </summary>
    public class MapSpatialConfig
    {
        /// <summary>
        /// Spatial type: "Grid", "Hex", or "Hybrid" (both Grid and Hex active).
        /// Default is "Grid".
        /// </summary>
        public string SpatialType { get; set; } = "Grid";

        /// <summary>Map width in tiles/chunks.</summary>
        public int WidthInTiles { get; set; } = 64;

        /// <summary>Map height in tiles/chunks.</summary>
        public int HeightInTiles { get; set; } = 64;

        /// <summary>Grid cell size in centimeters. Applies to Grid and Hybrid modes.</summary>
        public int GridCellSizeCm { get; set; } = 100;

        /// <summary>Hex edge length in centimeters. Applies to Hex and Hybrid modes.</summary>
        public int HexEdgeLengthCm { get; set; } = 400;

        /// <summary>Spatial partition chunk size in cells per side. Must be a power of two.</summary>
        public int ChunkSizeCells { get; set; } = 64;
    }

    public class EntitySpawnData
    {
        public string Template { get; set; }
        public IntVector2 Position { get; set; }
        public Dictionary<string, JsonNode> Overrides { get; set; }
    }
}
