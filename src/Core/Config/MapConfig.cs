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

        /// <summary>装载管线内部使用的片段来源标注（不参与序列化），供合并报告引用。</summary>
        [JsonIgnore]
        public string? MergeSourceUri { get; set; }

        /// <summary>
        /// 装载管线内部的待决实体墓碑（id + 来源标注，不参与序列化）。墓碑在继承链
        /// 展开前不消费——子图墓碑必须能命中父图实例；最终在 LoadMap 出口统一消化。
        /// </summary>
        [JsonIgnore]
        public List<(string InstanceId, string Source)>? PendingEntityTombstones { get; set; }

        /// <summary>装载管线内部的待决变量墓碑（name + 来源标注）。与实体墓碑同语义：继承链展开后统一消化。</summary>
        [JsonIgnore]
        public List<(string Name, string Source)>? PendingVariableTombstones { get; set; }

        public string ParentId { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>();
        public string ContinuousHeightmapAsset { get; set; }

        /// <summary>
        /// Optional explicit terrain surface presented by hosts. The visual heightmap
        /// remains available as the shared height truth regardless of this choice.
        /// </summary>
        public TerrainPresentationBindingConfig TerrainPresentation { get; set; }

        /// <summary>Path to the cooked structure collision asset for building surfaces, blockers, portals, and grounding.</summary>
        public string StructureCollisionAsset { get; set; } = string.Empty;

        /// <summary>When true, missing <see cref="StructureCollisionAsset"/> is a map-load error.</summary>
        public bool StructureAwareGrounding { get; set; }

        /// <summary>When true, missing <see cref="StructureCollisionAsset"/> is a map-load error.</summary>
        public bool StructureAwareNavigation { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public Dictionary<string, JsonNode> Metadata { get; set; } = new Dictionary<string, JsonNode>();

        /// <summary>
        /// Root board designation (#1567): the root board's extent anchors the host
        /// world frame and its services become the engine-level spatial services.
        /// Empty/omitted = first board. Boardless maps have no root; the engine boot
        /// world (game.json world) remains their host world.
        /// </summary>
        public string RootBoard { get; set; }

        /// <summary>
        /// Host world declaration for boardless maps (#1567): no board exists to anchor
        /// the world, so the map declares it directly. Mutually exclusive with Boards.
        /// </summary>
        public WorldConfig World { get; set; } = new WorldConfig();

        /// <summary>
        /// Map-level spatial budget (#1567): partition granularity and streaming
        /// capacity for the whole map. Authored values are the single budget every
        /// board runs on; board-level fields retire in slice 4b.
        /// </summary>
        public WorldTuningConfig Tuning { get; set; } = new WorldTuningConfig();
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
        /// Map-scoped variable declarations. The JSON field is strict-parsed at map-config
        /// load (MapManager); the per-session store is built by MapSession (Ludots.Core.Gameplay.MapTriggers).
        /// </summary>
        public List<MapVariableDeclaration> Variables { get; set; } = new List<MapVariableDeclaration>();

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

        /// <summary>
        /// Placement anchor of last resort (cm): lands as WorldPositionCm only when
        /// neither the template nor an explicit override supplies one. Both axes must
        /// be authored together.
        /// </summary>
        public int? PositionXCm { get; set; }
        public int? PositionYCm { get; set; }
        public Dictionary<string, JsonNode> Overrides { get; set; }
        public List<ParamOverrideData> PresenterParamOverrides { get; set; } = new List<ParamOverrideData>();

        /// <summary>
        /// 后代名字。path 是实例内的 localId 路径（不含 instanceId）；set 目前只认 Name。
        /// </summary>
        [JsonPropertyName("overridePaths")]
        public List<EntityPathNameOverride> OverridePaths { get; set; }

        /// <summary>
        /// 实例对外关系 authoring 段：from 即本实例，to 为绝对 instanceId 或组内可寻址路径。
        /// 跨 mod 合并按 (to, type) 后写赢；__delete 删边（合并层消化）。物化发生在地图装载站。
        /// </summary>
        public List<EntityRelationAuthoring>? Relations { get; set; }

        /// <summary>跨 mod 合并墓碑：与资产层 ConfigMerger 同键；只认 __delete，不引入 Disabled。</summary>
        [JsonPropertyName("__delete")]
        public bool? Delete { get; set; }
    }

    public sealed class EntityPathNameOverride
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("set")]
        public Dictionary<string, JsonNode> Set { get; set; }
    }

    public class EntityRelationAuthoring
    {
        public string To { get; set; }

        public string Type { get; set; }

        public Dictionary<string, int>? Metric { get; set; }

        [JsonPropertyName("__delete")]
        public bool? Delete { get; set; }
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
