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
        /// </summary>
        public List<BoardConfig> Boards { get; set; } = new List<BoardConfig>();

        /// <summary>
        /// Trigger type names declared by this map (JSON data-first path).
        /// </summary>
        public List<string> TriggerTypes { get; set; } = new List<string>();

        /// <summary>
        /// Default camera state when this map is loaded.
        /// If null, the engine uses CameraState defaults.
        /// Editor reads/writes this to ensure camera consistency across tools.
        /// </summary>
        public CameraConfig DefaultCamera { get; set; }
    }

    /// <summary>
    /// Camera configuration for a map. Matches the CameraState orbit model.
    /// All fields are optional; null/0 means "use engine default".
    /// </summary>
    public class CameraConfig
    {
        public float? TargetXCm { get; set; }
        public float? TargetYCm { get; set; }
        public float? Yaw { get; set; }
        public float? Pitch { get; set; }
        public float? DistanceCm { get; set; }
        public float? FovYDeg { get; set; }
    }

    public class EntitySpawnData
    {
        public string Template { get; set; }
        public IntVector2 Position { get; set; }
        public Dictionary<string, JsonNode> Overrides { get; set; }
    }
}
