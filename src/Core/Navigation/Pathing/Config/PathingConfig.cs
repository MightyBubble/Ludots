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
        public float Cost { get; set; }

        /// <summary>
        /// 缺省等于 cost。这是 schema 默认，装载器不另写一个数；读取处用 cost 补这个空值。
        /// </summary>
        public float? SpeedDivisor { get; set; }

        /// <summary>
        /// 缺省为可通行。false 表示该 agent 进不了这个区域。
        /// </summary>
        public bool? Passable { get; set; }
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
