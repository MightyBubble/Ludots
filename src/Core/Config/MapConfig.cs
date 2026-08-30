using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Config
{
    public class MapConfig
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
        public string ContinuousHeightmapAsset { get; set; }

        /// <summary>Path to the cooked structure collision asset for building surfaces, blockers, portals, and grounding.</summary>
        public string StructureCollisionAsset { get; set; } = string.Empty;

        /// <summary>When true, missing <see cref="StructureCollisionAsset"/> is a map-load error.</summary>
        public bool StructureAwareGrounding { get; set; }

        /// <summary>When true, missing <see cref="StructureCollisionAsset"/> is a map-load error.</summary>
        public bool StructureAwareNavigation { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public Dictionary<string, JsonNode> Metadata { get; set; } = new Dictionary<string, JsonNode>();
        public List<EntitySpawnData> Entities { get; set; } = new List<EntitySpawnData>();
        public List<TeamBindingData> Teams { get; set; } = new List<TeamBindingData>();
        public List<PlayerBindingData> Players { get; set; } = new List<PlayerBindingData>();
        public ParticipantRelationshipConfig ParticipantRelationships { get; set; } = new ParticipantRelationshipConfig();

        /// <summary>
        /// Board configurations for this map. Each board is a spatial domain.
        /// </summary>
        public List<BoardConfig> Boards { get; set; } = new List<BoardConfig>();

        /// <summary>
        /// Map-owned visual height truth. When declared, map load must install this
        /// as the core <see cref="IContinuousHeightmap"/> service instead of relying on a
        /// startup-time flat heightmap.
        /// </summary>
        public ContinuousHeightmapBindingConfig ContinuousHeightmap { get; set; }

        /// <summary>
        /// Trigger type names declared by this map (JSON data-first path).
        /// </summary>
        public List<string> TriggerTypes { get; set; } = new List<string>();

        /// <summary>
        /// TriggerGraph mounts declared by this map. Raw authoring nodes;
        /// strict parsing happens at mount time (Ludots.Core.Gameplay.MapTriggers).
        /// </summary>
        public JsonNode TriggerGraphs { get; set; }

        /// <summary>
        /// Fixed ticks per think wave for this map (integer &gt;= 1).
        /// Null means the engine default interval applies.
        /// </summary>
        public int? HeartbeatIntervalTicks { get; set; }

        /// <summary>
        /// Data-declared trigger regions for this map. Raw authoring nodes;
        /// strict parsing happens at region-mount time (Ludots.Core.Gameplay.MapTriggers).
        /// </summary>
        public JsonNode Regions { get; set; }

        /// <summary>
        /// Map-scoped variable declarations. The JSON field is strict-parsed at map-config
        /// load (MapManager); the per-session store is built by MapSession (Ludots.Core.Gameplay.MapTriggers).
        /// </summary>
        public List<MapVariableDeclaration> Variables { get; set; } = new List<MapVariableDeclaration>();

        /// <summary>
        /// Placed-instance exposure policy for graph authoring (#1108): "all" (default)
        /// exposes every placed instance; "declared" narrows variable materialization to
        /// <see cref="WatchedInstances"/>. The declared branch fails closed at map load
        /// until the HITL threshold decision lands; it never narrows #1106 event subscription.
        /// </summary>
        public string InstanceExposure { get; set; }

        /// <summary>
        /// Watch-list backing the "declared" <see cref="InstanceExposure"/> policy (#1108).
        /// </summary>
        public List<string> WatchedInstances { get; set; } = new List<string>();

        /// <summary>
        /// Data-declared death rule: when the declared attribute's current value reaches
        /// zero, map entities go through the destroy pipeline, feeding EntityDied /
        /// EntityAliveCountChanged for TriggerGraphs. Null = no death policy.
        /// </summary>
        public Ludots.Core.Gameplay.MapTriggers.MapDeathRule DeathRule { get; set; }

        /// <summary>
        /// Default camera state when this map is loaded.
        /// If null, the engine uses CameraState defaults.
        /// Editor reads/writes this to ensure camera consistency across tools.
        /// </summary>
        public CameraConfig DefaultCamera { get; set; }

        /// <summary>
        /// Field layers enabled on this map. Layer ids reference Fields/layers.json;
        /// the per-session store is created at map load (Ludots.Core.Fields).
        /// Null = this map hosts no field layers.
        /// </summary>
        public MapFieldsConfig Fields { get; set; }
    }

    /// <summary>
    /// Map-side enablement of field layers: which declared layers exist on this map.
    /// Later config fragments replace the list as a whole.
    /// </summary>
    public class MapFieldsConfig
    {
        public List<string> Layers { get; set; } = new List<string>();
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
        public string InstanceId { get; set; }
        public string Template { get; set; }
        public IntVector2 Position { get; set; }
        public Dictionary<string, JsonNode> Overrides { get; set; }
        public List<ParamOverrideData> PresenterParamOverrides { get; set; } = new List<ParamOverrideData>();
    }

    public class TeamBindingData
    {
        public int TeamId { get; set; }
        public string RepresentativeInstanceId { get; set; }
    }

    public class PlayerBindingData
    {
        public int PlayerId { get; set; }
        public int TeamId { get; set; }
        public string RepresentativeInstanceId { get; set; }
    }

    public class ParticipantRelationshipConfig
    {
        public List<TeamRelationshipBindingData> Teams { get; set; } = new List<TeamRelationshipBindingData>();
        public List<PlayerRelationshipBindingData> Players { get; set; } = new List<PlayerRelationshipBindingData>();
        public List<PlayerTeamRelationshipBindingData> PlayerTeams { get; set; } = new List<PlayerTeamRelationshipBindingData>();
    }

    public class TeamRelationshipBindingData
    {
        public int TeamA { get; set; }
        public int TeamB { get; set; }
        public string TypeId { get; set; } = string.Empty;
        public string Attitude { get; set; } = string.Empty;
        public bool Symmetric { get; set; } = true;
    }

    public class PlayerRelationshipBindingData
    {
        public int PlayerA { get; set; }
        public int PlayerB { get; set; }
        public string TypeId { get; set; } = string.Empty;
        public bool Symmetric { get; set; } = true;
    }

    public class PlayerTeamRelationshipBindingData
    {
        public int PlayerId { get; set; }
        public int TeamId { get; set; }
        public string TypeId { get; set; } = string.Empty;
        /// <summary>Optional stance name for the playerRep→teamRep edge; empty means no stance is declared.</summary>
        public string Attitude { get; set; } = string.Empty;
        public bool Symmetric { get; set; }
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
