using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Config
{
    public class MapConfig
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<EntitySpawnData> Entities { get; set; } = new List<EntitySpawnData>();

        /// <summary>
        /// Board configurations for this map. Each board is a spatial domain.
        /// Replaces the old Spatial and DataFile fields.
        /// </summary>
        public List<BoardConfig> Boards { get; set; } = new List<BoardConfig>();

        /// <summary>
        /// Trigger type names declared by this map (JSON data-first path).
        /// Merged with MapDefinition.TriggerTypes at load time.
        /// </summary>
        public List<string> TriggerTypes { get; set; } = new List<string>();
    }

    public class EntitySpawnData
    {
        public string Template { get; set; }
        public IntVector2 Position { get; set; }
        public Dictionary<string, JsonNode> Overrides { get; set; }
    }
}
