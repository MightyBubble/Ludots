using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public sealed class PathingConfig
    {
        [JsonConverter(typeof(PathingAgentTypeCatalogConverter))]
        public List<PathingAgentTypeConfig> AgentTypes { get; set; } = new();
    }

    public sealed class PathingAgentTypeConfig
    {
        public string Id { get; set; }
        public string ProfileId { get; set; }
        public PathingSelectionConfig Selection { get; set; }
        public PathingNavMeshConfig NavMesh { get; set; }
        public PathingNodeGraphConfig NodeGraph { get; set; }
    }

    public enum PathSelectionMode : byte
    {
        AutoCheapest = 0,
        PreferGraph = 1,
        PreferMesh = 2,
        Direct = 3
    }

    public sealed class PathingSelectionConfig
    {
        public PathSelectionMode Mode { get; set; } = PathSelectionMode.AutoCheapest;
        public float GraphBias { get; set; } = 0f;
        public float MeshBias { get; set; } = 0f;
        public float GraphCostWeight { get; set; } = 1f;
        public float MeshCostWeight { get; set; } = 1f;
    }

    public sealed class PathingNavMeshConfig
    {
        public List<PathingAreaCostConfig> AreaCosts { get; set; } = new();
    }

    public sealed class PathingAreaCostConfig
    {
        public int AreaId { get; set; }
        public double Cost { get; set; }

        /// <summary>false 表示该 agent 进不了这个区域。</summary>
        public bool Passable { get; set; }
    }

    public sealed class PathingNodeGraphConfig
    {
        public int ProjectionMaxRadiusCm { get; set; } = 200000;
        public bool UseDynamicOverlay { get; set; } = false;
        public List<string> RequiredTagsAll { get; set; } = new();
        public List<string> ForbiddenTagsAny { get; set; } = new();
        public List<PathingTagCostRuleConfig> TagCostRules { get; set; } = new();
    }

    public sealed class PathingTagCostRuleConfig
    {
        public string Tag { get; set; }
        public float CostMul { get; set; } = 1f;
        public float CostAdd { get; set; } = 0f;
        public bool Block { get; set; } = false;
    }
}
