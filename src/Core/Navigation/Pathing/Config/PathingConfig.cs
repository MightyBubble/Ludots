using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public sealed class PathingConfig
    {
        public List<PathingAgentTypeConfig> AgentTypes { get; set; } = new();
    }

    public sealed class PathingAgentTypeConfig
    {
        public string Id { get; set; }
        public string ProfileId { get; set; }
        public int Layer { get; set; } = 0;
        public PathingSelectionConfig Selection { get; set; } = new();
        public PathingNavMeshConfig NavMesh { get; set; } = new();
        public PathingNodeGraphConfig NodeGraph { get; set; } = new();
    }

    public enum PathSelectionMode : byte
    {
        AutoCheapest = 0,
        PreferGraph = 1,
        PreferMesh = 2
    }

    public sealed class PathingSelectionConfig
    {
        public PathSelectionMode Mode { get; set; } = PathSelectionMode.AutoCheapest;
        public float GraphBias { get; set; } = 0f;
        public float MeshBias { get; set; } = 0f;
        public float GraphCostWeight { get; set; } = 1f;
        public float MeshCostWeight { get; set; } = 1f;
        public PathSelectionMode Fallback { get; set; } = PathSelectionMode.PreferMesh;
    }

    public sealed class PathingNavMeshConfig
    {
        public List<PathingAreaCostConfig> AreaCosts { get; set; } = new();
    }

    public sealed class PathingAreaCostConfig
    {
        public int AreaId { get; set; } = 0;
        public float Cost { get; set; } = 1f;
    }

    public sealed class PathingNodeGraphConfig
    {
        public int ProjectionMaxRadiusCm { get; set; } = 200000;
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
