using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Config
{
    public class MapConfig
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
        public string VisualHeightmapAsset { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public Dictionary<string, JsonNode> Metadata { get; set; } = new Dictionary<string, JsonNode>();
        public List<EntitySpawnData> Entities { get; set; } = new List<EntitySpawnData>();

        /// <summary>
        /// Board configurations for this map. Each board is a spatial domain.
        /// </summary>
        public List<BoardConfig> Boards { get; set; } = new List<BoardConfig>();

        /// <summary>
        /// Map-owned visual height truth. When declared, map load must install this
        /// as the core <see cref="IVisualHeightmap"/> service instead of relying on a
        /// startup-time flat heightmap.
        /// </summary>
        public VisualHeightmapBindingConfig VisualHeightmap { get; set; }

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
    /// If VirtualCameraId is set, the named virtual camera profile is activated first;
    /// explicit fields then override that runtime instance.
    /// </summary>
    public class CameraConfig
    {
        /// <summary>
        /// Optional virtual camera profile ID (e.g. "Moba", "Rts", "Default").
        /// </summary>
        public string VirtualCameraId { get; set; }

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
        public List<ParamOverrideData> PerformerParamOverrides { get; set; } = new List<ParamOverrideData>();
    }

    public class ParamOverrideData
    {
        public string ParamKey { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ParamLane? Lane { get; set; }

        public float FloatValue { get; set; }
        public int IntValue { get; set; }
        public float[] VectorValue { get; set; } = Array.Empty<float>();
    }
}
